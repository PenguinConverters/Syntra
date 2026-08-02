using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using PenguinConverters.Syntra.Api.Settings;

namespace PenguinConverters.Syntra.Api.Controllers;

/// <summary>
/// Dynamically generates an OpenAPI 3.0 specification from the database schema at runtime.
/// No compilation is needed — the spec is built on each request by inspecting SQL Server
/// stored procedures and their metadata.
/// </summary>
/// <remarks>
/// Entity discovery is based on stored procedures matching the <c>SP_S1FE_*</c> naming pattern.
/// Parameter metadata is retrieved from INFORMATION_SCHEMA.PARAMETERS, and column/schema
/// information is derived from <c>FN_DBObjectParameters</c> and <c>FN_DBObjectDefinitionJson</c>
/// when available.
/// </remarks>
[ApiController]
[Route("api/openapi.json")]
[AllowAnonymous]
public class OpenApiController : ControllerBase
{
    private readonly ILogger<OpenApiController> _logger;
    private readonly DatabaseSettings _dbSettings;
    private readonly ITokenAcquisition _tokenAcquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenApiController"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dbSettings">The database settings.</param>
    /// <param name="tokenAcquisition">The token acquisition service.</param>
    public OpenApiController(
        ILogger<OpenApiController> logger,
        IOptions<DatabaseSettings> dbSettings,
        ITokenAcquisition tokenAcquisition)
    {
        _logger = logger;
        _dbSettings = dbSettings.Value;
        _tokenAcquisition = tokenAcquisition;
    }

    /// <summary>
    /// Returns a dynamically generated OpenAPI 3.0 specification as JSON.
    /// Discovers entities from SP_S1FE_* stored procedures and generates paths,
    /// schemas, and parameters from SQL Server metadata.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OpenAPI 3.0 JSON document.</returns>
    /// <response code="200">Returns the OpenAPI specification.</response>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOpenApiSpec(CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = await CreateConnectionAsync(cancellationToken);

            List<EntityInfo> entities = await DiscoverEntitiesAsync(connection, cancellationToken);
            Dictionary<string, List<ParameterInfo>> entityParameters = await GetEntityParametersAsync(connection, entities, cancellationToken);
            Dictionary<string, List<ColumnInfo>> entityColumns = await GetEntityColumnsAsync(connection, entities, cancellationToken);

            JsonObject spec = BuildOpenApiDocument(entities, entityParameters, entityColumns);

            return Content(
                spec.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate OpenAPI specification");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Failed to generate OpenAPI specification." });
        }
    }

    /// <summary>
    /// Discovers entity names from stored procedures matching SP_S1FE_*_READ/CREATE/UPDATE/DELETE.
    /// </summary>
    private static async Task<List<EntityInfo>> DiscoverEntitiesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        Dictionary<string, EntityInfo> entityMap = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase);

        const string sql = @"
            SELECT SPECIFIC_NAME, ROUTINE_TYPE
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'PROCEDURE'
              AND SPECIFIC_NAME LIKE 'SP_S1FE_%'
            ORDER BY SPECIFIC_NAME";

        await using SqlCommand command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            string spName = reader.GetString(0);

            // Parse entity name and operation from SP_S1FE_{Entity}_{Operation}
            if (!spName.StartsWith("SP_S1FE_", StringComparison.OrdinalIgnoreCase))
                continue;

            string remainder = spName[8..]; // After "SP_S1FE_"
            int lastUnderscore = remainder.LastIndexOf('_');
            if (lastUnderscore < 0) continue;

            string entityName = remainder[..lastUnderscore];
            string operation = remainder[(lastUnderscore + 1)..].ToUpperInvariant();

            if (!entityMap.TryGetValue(entityName, out EntityInfo? info))
            {
                info = new EntityInfo { Name = entityName };
                entityMap[entityName] = info;
            }

            switch (operation)
            {
                case "READ": info.HasRead = true; break;
                case "CREATE": info.HasCreate = true; break;
                case "UPDATE": info.HasUpdate = true; break;
                case "DELETE": info.HasDelete = true; break;
            }
        }

        return entityMap.Values.OrderBy(e => e.Name).ToList();
    }

    /// <summary>
    /// Retrieves stored procedure parameters for each entity's operations from
    /// INFORMATION_SCHEMA.PARAMETERS and optionally FN_DBObjectParameters.
    /// </summary>
    private static async Task<Dictionary<string, List<ParameterInfo>>> GetEntityParametersAsync(
        SqlConnection connection, List<EntityInfo> entities, CancellationToken cancellationToken)
    {
        Dictionary<string, List<ParameterInfo>> result = new Dictionary<string, List<ParameterInfo>>();

        foreach (EntityInfo entity in entities)
        {
            List<ParameterInfo> parameters = new List<ParameterInfo>();

            // Query INFORMATION_SCHEMA.PARAMETERS for all SP_S1FE_{entity}_* procedures
            const string sql = @"
                SELECT p.SPECIFIC_NAME, p.PARAMETER_NAME, p.DATA_TYPE,
                       p.CHARACTER_MAXIMUM_LENGTH, p.PARAMETER_MODE,
                       p.NUMERIC_PRECISION, p.NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.PARAMETERS p
                WHERE p.SPECIFIC_NAME LIKE @Pattern
                  AND p.PARAMETER_NAME <> ''
                ORDER BY p.SPECIFIC_NAME, p.ORDINAL_POSITION";

            await using SqlCommand command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Pattern", $"SP_S1FE_{entity.Name}_%");

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                parameters.Add(new ParameterInfo
                {
                    ProcedureName = reader.GetString(0),
                    Name = reader.GetString(1).TrimStart('@'),
                    DataType = reader.GetString(2),
                    MaxLength = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Mode = reader.GetString(4),
                    NumericPrecision = reader.IsDBNull(5) ? null : reader.GetByte(5),
                    NumericScale = reader.IsDBNull(6) ? null : reader.GetInt32(6)
                });
            }

            result[entity.Name] = parameters;

            // Attempt to enrich with FN_DBObjectParameters if available
            await TryEnrichWithDbObjectParameters(connection, entity.Name, parameters, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Attempts to retrieve additional parameter metadata from the FN_DBObjectParameters function.
    /// </summary>
    private static async Task TryEnrichWithDbObjectParameters(
        SqlConnection connection, string entityName,
        List<ParameterInfo> parameters, CancellationToken cancellationToken)
    {
        try
        {
            const string sql = @"
                SELECT *
                FROM dbo.FN_DBObjectParameters(@ObjectName)";

            await using SqlCommand command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ObjectName", $"SP_S1FE_{entityName}_CREATE");

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                // Enrich existing parameters with additional metadata if available
                string? paramName = reader["ParameterName"]?.ToString()?.TrimStart('@');
                ParameterInfo? existing = parameters.FirstOrDefault(p =>
                    p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));

                if (existing is not null
                    && reader.GetSchemaTable()?.Columns.Contains("Description") is true)
                {
                    int descOrdinal = reader.GetOrdinal("Description");
                    if (!reader.IsDBNull(descOrdinal))
                        existing.Description = reader.GetString(descOrdinal);
                }
            }
        }
        catch (SqlException)
        {
            // FN_DBObjectParameters may not exist — this is expected
        }
    }

    /// <summary>
    /// Retrieves column metadata for READ views to build response schemas.
    /// </summary>
    private static async Task<Dictionary<string, List<ColumnInfo>>> GetEntityColumnsAsync(
        SqlConnection connection, List<EntityInfo> entities, CancellationToken cancellationToken)
    {
        Dictionary<string, List<ColumnInfo>> result = new Dictionary<string, List<ColumnInfo>>();

        foreach (EntityInfo entity in entities.Where(e => e.HasRead))
        {
            List<ColumnInfo> columns = new List<ColumnInfo>();

            const string sql = @"
                SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                       IS_NULLABLE, NUMERIC_PRECISION, NUMERIC_SCALE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @ViewName
                ORDER BY ORDINAL_POSITION";

            await using SqlCommand command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ViewName", $"SP_S1FE_{entity.Name}_READ");

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new ColumnInfo
                {
                    Name = reader.GetString(0),
                    DataType = reader.GetString(1),
                    MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    IsNullable = reader.GetString(3) == "YES"
                });
            }

            // If no INFORMATION_SCHEMA columns, try FN_DBObjectDefinitionJson
            if (columns.Count == 0)
            {
                columns = await TryGetColumnsFromDefinitionJson(
                    connection, entity.Name, cancellationToken);
            }

            if (columns.Count > 0)
                result[entity.Name] = columns;
        }

        return result;
    }

    /// <summary>
    /// Attempts to retrieve column definitions from FN_DBObjectDefinitionJson.
    /// </summary>
    private static async Task<List<ColumnInfo>> TryGetColumnsFromDefinitionJson(
        SqlConnection connection, string entityName, CancellationToken cancellationToken)
    {
        List<ColumnInfo> columns = new List<ColumnInfo>();

        try
        {
            const string sql = "SELECT dbo.FN_DBObjectDefinitionJson(@ObjectName)";

            await using SqlCommand command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ObjectName", $"SP_S1FE_{entityName}_READ");

            object? result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is string json && !string.IsNullOrEmpty(json))
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("columns", out JsonElement colArray))
                {
                    foreach (JsonElement col in colArray.EnumerateArray())
                    {
                        columns.Add(new ColumnInfo
                        {
                            Name = col.GetProperty("name").GetString() ?? string.Empty,
                            DataType = col.GetProperty("type").GetString() ?? "nvarchar",
                            MaxLength = col.TryGetProperty("maxLength", out JsonElement ml) ? ml.GetInt32() : null,
                            IsNullable = col.TryGetProperty("nullable", out JsonElement n) && n.GetBoolean()
                        });
                    }
                }
            }
        }
        catch (SqlException)
        {
            // FN_DBObjectDefinitionJson may not exist — this is expected
        }

        return columns;
    }

    /// <summary>
    /// Builds the complete OpenAPI 3.0 document from discovered entities and metadata.
    /// </summary>
    private JsonObject BuildOpenApiDocument(
        List<EntityInfo> entities,
        Dictionary<string, List<ParameterInfo>> entityParameters,
        Dictionary<string, List<ColumnInfo>> entityColumns)
    {
        string baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api";

        JsonObject doc = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject
            {
                ["title"] = "Syntra API",
                ["description"] = "Syntra IAM/Automation REST API - dynamically generated from database schema.",
                ["version"] = "1.0.0",
                ["contact"] = new JsonObject
                {
                    ["name"] = "Syntra Team",
                    ["url"] = "https://syntra.penguinconverters.com"
                }
            },
            ["servers"] = new JsonArray
            {
                new JsonObject { ["url"] = baseUrl, ["description"] = "Syntra API" }
            }
        };

        JsonObject paths = new JsonObject();
        JsonObject schemas = new JsonObject();

        foreach (EntityInfo entity in entities)
        {
            string entityPath = $"/{entity.Name}";
            JsonObject pathItem = new JsonObject();

            // GET operation
            if (entity.HasRead)
            {
                pathItem["get"] = BuildGetOperation(entity, entityColumns);
            }

            // POST operation
            if (entity.HasCreate)
            {
                List<ParameterInfo> createParams = entityParameters.GetValueOrDefault(entity.Name)?
                    .Where(p => p.ProcedureName.EndsWith("_CREATE", StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? [];
                pathItem["post"] = BuildMutationOperation(entity, "Create", createParams, "201");
            }

            // PATCH operation
            if (entity.HasUpdate)
            {
                List<ParameterInfo> updateParams = entityParameters.GetValueOrDefault(entity.Name)?
                    .Where(p => p.ProcedureName.EndsWith("_UPDATE", StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? [];
                pathItem["patch"] = BuildMutationOperation(entity, "Update", updateParams, "200");
            }

            // DELETE operation
            if (entity.HasDelete)
            {
                List<ParameterInfo> deleteParams = entityParameters.GetValueOrDefault(entity.Name)?
                    .Where(p => p.ProcedureName.EndsWith("_DELETE", StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? [];
                pathItem["delete"] = BuildMutationOperation(entity, "Delete", deleteParams, "204");
            }

            paths[entityPath] = pathItem;

            // Build response schema from columns
            if (entityColumns.TryGetValue(entity.Name, out List<ColumnInfo>? columns))
            {
                schemas[entity.Name] = BuildEntitySchema(entity.Name, columns);
            }
        }

        // Add ODataResponse wrapper schema
        schemas["ODataResponse"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["@odata.context"] = new JsonObject { ["type"] = "string" },
                ["@odata.count"] = new JsonObject { ["type"] = "integer", ["format"] = "int64" },
                ["@odata.nextLink"] = new JsonObject { ["type"] = "string", ["format"] = "uri" },
                ["value"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } }
            }
        };

        doc["paths"] = paths;
        doc["components"] = new JsonObject
        {
            ["schemas"] = schemas,
            ["securitySchemes"] = new JsonObject
            {
                ["bearer"] = new JsonObject
                {
                    ["type"] = "http",
                    ["scheme"] = "bearer",
                    ["bearerFormat"] = "JWT",
                    ["description"] = "JWT Bearer token (Azure AD / Entra ID)"
                },
                ["negotiate"] = new JsonObject
                {
                    ["type"] = "http",
                    ["scheme"] = "negotiate",
                    ["description"] = "Windows Integrated Authentication (Kerberos/NTLM)"
                }
            }
        };

        doc["security"] = new JsonArray
        {
            new JsonObject { ["bearer"] = new JsonArray() },
            new JsonObject { ["negotiate"] = new JsonArray() }
        };

        return doc;
    }

    /// <summary>
    /// Builds a GET operation for an entity including OData query parameters.
    /// </summary>
    private static JsonObject BuildGetOperation(EntityInfo entity, Dictionary<string, List<ColumnInfo>> entityColumns)
    {
        JsonObject operation = new JsonObject
        {
            ["summary"] = $"Query {entity.Name} entities",
            ["description"] = $"Retrieves {entity.Name} entities with OData query support.",
            ["operationId"] = $"get{entity.Name}",
            ["tags"] = new JsonArray { entity.Name },
            ["parameters"] = BuildODataParameters()
        };

        JsonObject responseSchema = entityColumns.ContainsKey(entity.Name)
            ? new JsonObject
            {
                ["allOf"] = new JsonArray
                {
                    new JsonObject { ["$ref"] = "#/components/schemas/ODataResponse" },
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["value"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject
                                {
                                    ["$ref"] = $"#/components/schemas/{entity.Name}"
                                }
                            }
                        }
                    }
                }
            }
            : new JsonObject { ["$ref"] = "#/components/schemas/ODataResponse" };

        operation["responses"] = new JsonObject
        {
            ["200"] = new JsonObject
            {
                ["description"] = $"A collection of {entity.Name} entities.",
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject
                    {
                        ["schema"] = responseSchema
                    }
                }
            },
            ["400"] = new JsonObject { ["description"] = "Invalid query parameters." },
            ["401"] = new JsonObject { ["description"] = "Authentication required." },
            ["403"] = new JsonObject { ["description"] = "Insufficient permissions." }
        };

        return operation;
    }

    /// <summary>
    /// Builds a POST/PATCH/DELETE operation from stored procedure parameters.
    /// </summary>
    private static JsonObject BuildMutationOperation(
        EntityInfo entity, string verb, List<ParameterInfo> parameters, string successCode)
    {
        JsonObject operation = new JsonObject
        {
            ["summary"] = $"{verb} a {entity.Name} entity",
            ["operationId"] = $"{verb.ToLowerInvariant()}{entity.Name}",
            ["tags"] = new JsonArray { entity.Name }
        };

        // Build request body from parameters
        if (parameters.Count > 0)
        {
            JsonObject properties = new JsonObject();
            JsonArray required = new JsonArray();

            foreach (ParameterInfo param in parameters)
            {
                JsonObject propSchema = new JsonObject
                {
                    ["type"] = MapSqlTypeToOpenApi(param.DataType)
                };

                string? format = MapSqlTypeToOpenApiFormat(param.DataType);
                if (format is not null)
                    propSchema["format"] = format;

                if (param.MaxLength.HasValue && param.MaxLength.Value > 0 && param.MaxLength.Value < int.MaxValue)
                    propSchema["maxLength"] = param.MaxLength.Value;

                if (param.Description is not null)
                    propSchema["description"] = param.Description;

                properties[param.Name] = propSchema;

                if (param.Mode == "IN")
                    required.Add(param.Name);
            }

            operation["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject
                    {
                        ["schema"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = properties,
                            ["required"] = required.Count > 0 ? required : null
                        }
                    }
                }
            };
        }

        operation["responses"] = new JsonObject
        {
            [successCode] = new JsonObject
            {
                ["description"] = $"{verb} operation completed successfully."
            },
            ["400"] = new JsonObject { ["description"] = "Invalid request body." },
            ["401"] = new JsonObject { ["description"] = "Authentication required." },
            ["403"] = new JsonObject { ["description"] = "Insufficient permissions." }
        };

        return operation;
    }

    /// <summary>
    /// Builds standard OData query parameters for GET operations.
    /// </summary>
    private static JsonArray BuildODataParameters()
    {
        return new JsonArray
        {
            BuildParameter("$filter", "query", "string", "OData filter expression (e.g., Name eq 'John')"),
            BuildParameter("$orderby", "query", "string", "Comma-separated list of sort expressions (e.g., Name asc, Created desc)"),
            BuildParameter("$top", "query", "integer", "Maximum number of records to return"),
            BuildParameter("$skip", "query", "integer", "Number of records to skip for pagination"),
            BuildParameter("$skiptoken", "query", "string", "Continuation token for server-driven pagination"),
            BuildParameter("$select", "query", "string", "Comma-separated list of columns to return"),
            BuildParameter("$count", "query", "boolean", "Include total count in response (@odata.count)"),
            BuildParameter("$apply", "query", "string", "Aggregate transformation (e.g., distinct)"),
            BuildParameter("@syntra.metadata", "query", "integer",
                "Metadata level: 0=data, 1=minimal, 2=properties, 3=count, 4=parameters")
        };
    }

    private static JsonObject BuildParameter(string name, string location, string type, string description)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["in"] = location,
            ["required"] = false,
            ["description"] = description,
            ["schema"] = new JsonObject { ["type"] = type }
        };
    }

    /// <summary>
    /// Builds an OpenAPI schema object for an entity from its column definitions.
    /// </summary>
    private static JsonObject BuildEntitySchema(string entityName, List<ColumnInfo> columns)
    {
        JsonObject properties = new JsonObject();
        JsonArray required = new JsonArray();

        foreach (ColumnInfo column in columns)
        {
            JsonObject prop = new JsonObject
            {
                ["type"] = MapSqlTypeToOpenApi(column.DataType)
            };

            string? format = MapSqlTypeToOpenApiFormat(column.DataType);
            if (format is not null)
                prop["format"] = format;

            if (column.MaxLength.HasValue && column.MaxLength.Value > 0 && column.MaxLength.Value < int.MaxValue)
                prop["maxLength"] = column.MaxLength.Value;

            if (column.IsNullable)
                prop["nullable"] = true;
            else
                required.Add(column.Name);

            properties[column.Name] = prop;
        }

        JsonObject schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
            schema["required"] = required;

        return schema;
    }

    /// <summary>
    /// Maps SQL Server data types to OpenAPI/JSON Schema types.
    /// </summary>
    private static string MapSqlTypeToOpenApi(string sqlType)
    {
        return sqlType.ToLowerInvariant() switch
        {
            "int" or "smallint" or "tinyint" or "bigint" => "integer",
            "bit" => "boolean",
            "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney" => "number",
            "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => "string",
            _ => "string"
        };
    }

    /// <summary>
    /// Maps SQL Server data types to OpenAPI format hints.
    /// </summary>
    private static string? MapSqlTypeToOpenApiFormat(string sqlType)
    {
        return sqlType.ToLowerInvariant() switch
        {
            "bigint" => "int64",
            "int" => "int32",
            "smallint" => "int16",
            "float" => "double",
            "real" => "float",
            "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
            "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "date-time",
            "date" => "date",
            "time" => "time",
            "uniqueidentifier" => "uuid",
            "binary" or "varbinary" or "image" => "byte",
            _ => null
        };
    }

    /// <summary>
    /// Creates a SQL connection for metadata discovery (uses app credentials).
    /// </summary>
    private async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new SqlConnection(_dbSettings.ConnectionString);

        if (!_dbSettings.UseIntegratedSecurity)
        {
            try
            {
                string accessToken = await _tokenAcquisition.GetAccessTokenForAppAsync(
                    _dbSettings.SqlResource);
                connection.AccessToken = accessToken;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to acquire app token for OpenAPI generation, attempting without token");
            }
        }

        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private sealed class EntityInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool HasRead { get; set; }
        public bool HasCreate { get; set; }
        public bool HasUpdate { get; set; }
        public bool HasDelete { get; set; }
    }

    private sealed class ParameterInfo
    {
        public string ProcedureName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public string Mode { get; set; } = "IN";
        public byte? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
        public string? Description { get; set; }
    }

    private sealed class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public bool IsNullable { get; set; }
    }
}
