using System.Data;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using PenguinConverters.Syntra.Api.Entities;
using PenguinConverters.Syntra.Api.OData;
using PenguinConverters.Syntra.Api.Settings;

namespace PenguinConverters.Syntra.Api.Controllers;

/// <summary>
/// Main REST controller for the Syntra API. Exposes database views and stored procedures
/// as OData-compliant REST endpoints with full query support.
/// </summary>
/// <remarks>
/// <para>
/// All queries run under the calling user's Windows identity via On-Behalf-Of (OBO) flow,
/// enabling per-record authorization at the SQL Server level. When OBO is unavailable,
/// falls back to application-based access for service accounts with the Syntra.AccessAsApp role.
/// </para>
/// <para>
/// The <c>@syntra.metadata</c> parameter controls response shape:
/// <list type="bullet">
///   <item><c>0</c> - Data only (default)</item>
///   <item><c>1</c> - Minimal metadata (column names + types)</item>
///   <item><c>2</c> - Full property definitions (field metadata from stored procedure)</item>
///   <item><c>3</c> - Count of matching records</item>
///   <item><c>4</c> - Stored procedure parameter definitions</item>
/// </list>
/// </para>
/// </remarks>
[ApiController]
[Route("api/{entity}")]
[Authorize]
public class SyntraController : ControllerBase
{
    private readonly ILogger<SyntraController> _logger;
    private readonly DatabaseSettings _dbSettings;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly SqlQueryBuilder _queryBuilder = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SyntraController"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dbSettings">The database settings.</param>
    /// <param name="tokenAcquisition">The token acquisition service for OBO flow.</param>
    public SyntraController(
        ILogger<SyntraController> logger,
        IOptions<DatabaseSettings> dbSettings,
        ITokenAcquisition tokenAcquisition)
    {
        _logger = logger;
        _dbSettings = dbSettings.Value;
        _tokenAcquisition = tokenAcquisition;
    }

    /// <summary>
    /// Queries the specified entity using OData parameters.
    /// Data is streamed from SQL Server using <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <param name="entity">The entity name, mapped to a SQL view or SP_S1FE_{entity}_READ procedure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An OData response containing the matching entities.</returns>
    /// <response code="200">Returns the matching entities.</response>
    /// <response code="400">The query parameters are invalid.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The user does not have access to this entity.</response>
    [HttpGet]
    [ProducesResponseType(typeof(ODataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(string entity, CancellationToken cancellationToken)
    {
        IDictionary<string, string> queryParams = GetODataParameters();
        int metadataLevel = GetMetadataLevel(queryParams);

        // Metadata-only requests
        switch (metadataLevel)
        {
            case 3: // Count
                return await GetCount(entity, queryParams, cancellationToken);
            case 4: // Parameters
                return await GetParameters(entity, cancellationToken);
            case 2: // Properties
                return await GetProperties(entity, cancellationToken);
        }

        try
        {
            await using SqlConnection connection = await CreateConnectionAsync(cancellationToken);

            string viewName = $"SP_S1FE_{SanitizeEntityName(entity)}_READ";
            string sql = _queryBuilder.BuildQuery(viewName, queryParams, _dbSettings.DefaultMaxRows);

            await using SqlCommand command = new SqlCommand(sql, connection)
            {
                CommandTimeout = _dbSettings.CommandTimeoutSeconds
            };

            foreach (SqlParameter param in _queryBuilder.Parameters)
                command.Parameters.Add(param);

            List<Dictionary<string, object?>> results = new List<Dictionary<string, object?>>();
            long? count = null;

            // Optionally get count
            if (queryParams.TryGetValue("$count", out string? countStr) &&
                bool.TryParse(countStr, out bool includeCount) && includeCount)
            {
                count = await GetEntityCount(entity, queryParams, connection, cancellationToken);
            }

            await foreach (Dictionary<string, object?> row in ExecuteReaderAsync(command, cancellationToken))
            {
                results.Add(row);
            }

            ODataResponse response = new ODataResponse
            {
                Context = $"{GetBaseUrl()}/$metadata#SP_S1FE_{SanitizeEntityName(entity)}_READ",
                Count = count,
                Value = results
            };

            // Build nextLink if pagination applies
            if (queryParams.TryGetValue("$top", out string? topStr) &&
                int.TryParse(topStr, out int top) && results.Count == top)
            {
                int skip = 0;
                if (queryParams.TryGetValue("$skip", out string? skipStr))
                    int.TryParse(skipStr, out skip);

                response.NextLink = BuildNextLink(entity, queryParams, skip + top);
            }

            if (metadataLevel == 1)
            {
                // Minimal metadata: include column info in response
                return Ok(new
                {
                    response.Context,
                    response.Count,
                    response.NextLink,
                    response.Value,
                    Columns = results.Count > 0
                        ? results[0].Keys.ToArray()
                        : Array.Empty<string>()
                });
            }

            return Ok(response);
        }
        catch (SqlException ex) when (ex.Number == 229 || ex.Number == 230)
        {
            _logger.LogWarning(ex, "Access denied for entity {Entity} by user {User}",
                entity, User.Identity?.Name);
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid query parameters for entity {Entity}", entity);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Creates a new entity by executing SP_S1FE_{entity}_CREATE.
    /// </summary>
    /// <param name="entity">The entity name.</param>
    /// <param name="body">The entity properties as a JSON object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created entity or a success status.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Post(
        string entity,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        return await ExecuteStoredProcedure(entity, "CREATE", body, cancellationToken);
    }

    /// <summary>
    /// Updates an existing entity by executing SP_S1FE_{entity}_UPDATE.
    /// </summary>
    /// <param name="entity">The entity name.</param>
    /// <param name="body">The entity properties to update as a JSON object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated entity or a success status.</returns>
    [HttpPatch]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Patch(
        string entity,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        return await ExecuteStoredProcedure(entity, "UPDATE", body, cancellationToken);
    }

    /// <summary>
    /// Deletes an entity by executing SP_S1FE_{entity}_DELETE.
    /// </summary>
    /// <param name="entity">The entity name.</param>
    /// <param name="body">The entity key(s) identifying the record to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A success status.</returns>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(
        string entity,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        return await ExecuteStoredProcedure(entity, "DELETE", body, cancellationToken);
    }

    /// <summary>
    /// Executes a CRUD stored procedure (CREATE/UPDATE/DELETE) for the specified entity.
    /// </summary>
    private async Task<IActionResult> ExecuteStoredProcedure(
        string entity, string operation, JsonElement body, CancellationToken cancellationToken)
    {
        try
        {
            string sanitizedEntity = SanitizeEntityName(entity);
            string procedureName = $"SP_S1FE_{sanitizedEntity}_{operation}";

            Dictionary<string, object?> parameters = new Dictionary<string, object?>();
            if (body.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in body.EnumerateObject())
                {
                    parameters[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out long l) ? l : prop.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
            }

            await using SqlConnection connection = await CreateConnectionAsync(cancellationToken);

            string sql = _queryBuilder.BuildStoredProcedureCall(procedureName, parameters);

            await using SqlCommand command = new SqlCommand(sql, connection)
            {
                CommandTimeout = _dbSettings.CommandTimeoutSeconds
            };

            foreach (SqlParameter param in _queryBuilder.Parameters)
                command.Parameters.Add(param);

            // Try to read results if the SP returns a result set
            List<Dictionary<string, object?>> results = new List<Dictionary<string, object?>>();
            await foreach (Dictionary<string, object?> row in ExecuteReaderAsync(command, cancellationToken))
            {
                results.Add(row);
            }

            if (operation == "DELETE")
                return results.Count > 0 ? Ok(results) : NoContent();

            if (results.Count > 0)
            {
                return operation == "CREATE"
                    ? StatusCode(StatusCodes.Status201Created, new ODataResponse
                    {
                        Context = $"{GetBaseUrl()}/$metadata#{procedureName}",
                        Value = results
                    })
                    : Ok(new ODataResponse
                    {
                        Context = $"{GetBaseUrl()}/$metadata#{procedureName}",
                        Value = results
                    });
            }

            return operation == "CREATE" ? StatusCode(StatusCodes.Status201Created) : Ok();
        }
        catch (SqlException ex) when (ex.Number == 229 || ex.Number == 230)
        {
            _logger.LogWarning(ex, "Access denied for {Operation} on {Entity} by user {User}",
                operation, entity, User.Identity?.Name);
            return Forbid();
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error during {Operation} on {Entity}", operation, entity);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Returns the count of records matching the filter criteria.
    /// </summary>
    private async Task<IActionResult> GetCount(
        string entity, IDictionary<string, string> queryParams, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await CreateConnectionAsync(cancellationToken);

        string viewName = $"SP_S1FE_{SanitizeEntityName(entity)}_READ";
        string sql = _queryBuilder.BuildCountQuery(viewName, queryParams);

        await using SqlCommand command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _dbSettings.CommandTimeoutSeconds
        };

        foreach (SqlParameter param in _queryBuilder.Parameters)
            command.Parameters.Add(param);

        int count = (int)(await command.ExecuteScalarAsync(cancellationToken))!;

        return Ok(new { @odata_count = count });
    }

    /// <summary>
    /// Returns stored procedure parameter definitions for the entity.
    /// </summary>
    private async Task<IActionResult> GetParameters(string entity, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await CreateConnectionAsync(cancellationToken);

        string sanitizedEntity = SanitizeEntityName(entity);
        string sql = @"
            SELECT p.PARAMETER_NAME, p.DATA_TYPE, p.CHARACTER_MAXIMUM_LENGTH,
                   p.NUMERIC_PRECISION, p.NUMERIC_SCALE, p.PARAMETER_MODE
            FROM INFORMATION_SCHEMA.PARAMETERS p
            WHERE p.SPECIFIC_NAME LIKE @ProcPattern
            ORDER BY p.ORDINAL_POSITION";

        await using SqlCommand command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _dbSettings.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("@ProcPattern", $"SP_S1FE_{sanitizedEntity}_%");

        List<Dictionary<string, object?>> results = new List<Dictionary<string, object?>>();
        await foreach (Dictionary<string, object?> row in ExecuteReaderAsync(command, cancellationToken))
        {
            results.Add(row);
        }

        return Ok(new { entity, parameters = results });
    }

    /// <summary>
    /// Returns property definitions (column metadata) for the entity view.
    /// </summary>
    private async Task<IActionResult> GetProperties(string entity, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await CreateConnectionAsync(cancellationToken);

        string sanitizedEntity = SanitizeEntityName(entity);
        string sql = @"
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
                   c.IS_NULLABLE, c.COLUMN_DEFAULT, c.NUMERIC_PRECISION, c.NUMERIC_SCALE,
                   c.ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_NAME = @ViewName
            ORDER BY c.ORDINAL_POSITION";

        await using SqlCommand command = new SqlCommand(sql, connection)
        {
            CommandTimeout = _dbSettings.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("@ViewName", $"SP_S1FE_{sanitizedEntity}_READ");

        List<Dictionary<string, object?>> results = new List<Dictionary<string, object?>>();
        await foreach (Dictionary<string, object?> row in ExecuteReaderAsync(command, cancellationToken))
        {
            results.Add(row);
        }

        return Ok(new { entity, properties = results });
    }

    /// <summary>
    /// Gets the count of entities matching the query parameters.
    /// </summary>
    private async Task<long> GetEntityCount(
        string entity,
        IDictionary<string, string> queryParams,
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        SqlQueryBuilder countBuilder = new SqlQueryBuilder();
        string viewName = $"SP_S1FE_{SanitizeEntityName(entity)}_READ";
        string countSql = countBuilder.BuildCountQuery(viewName, queryParams);

        await using SqlCommand countCommand = new SqlCommand(countSql, connection)
        {
            CommandTimeout = _dbSettings.CommandTimeoutSeconds
        };

        foreach (SqlParameter param in countBuilder.Parameters)
            countCommand.Parameters.Add(param);

        object? result = await countCommand.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Executes a SQL command and streams results as an async enumerable of dictionaries.
    /// Each dictionary maps column names to their values.
    /// </summary>
    private static async IAsyncEnumerable<Dictionary<string, object?>> ExecuteReaderAsync(
        SqlCommand command, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using SqlDataReader reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess, cancellationToken);

        string[] columnNames = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            columnNames[i] = reader.GetName(i);

        while (await reader.ReadAsync(cancellationToken))
        {
            Dictionary<string, object?> row = new Dictionary<string, object?>(columnNames.Length);
            for (int i = 0; i < columnNames.Length; i++)
            {
                object value = reader.GetValue(i);
                row[columnNames[i]] = value == DBNull.Value ? null : value;
            }
            yield return row;
        }
    }

    /// <summary>
    /// Creates a SQL connection with the appropriate authentication.
    /// Uses OBO flow to acquire a SQL access token when the user has user_impersonation scope.
    /// Falls back to application-based access for service accounts with Syntra.AccessAsApp role.
    /// </summary>
    private async Task<SqlConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new SqlConnection(_dbSettings.ConnectionString);

        if (_dbSettings.UseIntegratedSecurity)
        {
            // Windows Integrated Authentication (Negotiate/Kerberos)
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        // Try OBO flow first: acquire SQL access token on behalf of the user
        try
        {
            HashSet<string> scopes = User.FindAll("scp")
                .SelectMany(c => c.Value.Split(' '))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (scopes.Contains("user_impersonation"))
            {
                string accessToken = await _tokenAcquisition.GetAccessTokenForUserAsync(
                    [_dbSettings.SqlResource], user: User as ClaimsPrincipal);

                connection.AccessToken = accessToken;
                _logger.LogDebug("Using OBO token for user {User}", User.Identity?.Name);
            }
            else if (User.IsInRole("Syntra.AccessAsApp"))
            {
                // Application-based access for service accounts
                string accessToken = await _tokenAcquisition.GetAccessTokenForAppAsync(
                    _dbSettings.SqlResource);

                connection.AccessToken = accessToken;
                _logger.LogDebug("Using app token for service account {User}", User.Identity?.Name);
            }
            else
            {
                _logger.LogWarning(
                    "User {User} has neither user_impersonation scope nor Syntra.AccessAsApp role",
                    User.Identity?.Name);
                throw new UnauthorizedAccessException(
                    "Insufficient permissions. Required: user_impersonation scope or Syntra.AccessAsApp role.");
            }
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _logger.LogWarning(ex,
                "OBO token acquisition failed for user {User}, attempting app-level access",
                User.Identity?.Name);

            // Fallback to app-level token
            string accessToken = await _tokenAcquisition.GetAccessTokenForAppAsync(
                _dbSettings.SqlResource);
            connection.AccessToken = accessToken;
        }

        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Extracts OData query parameters from the request query string.
    /// </summary>
    private IDictionary<string, string> GetODataParameters()
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string key in Request.Query.Keys)
        {
            if (key.StartsWith('$') || key.StartsWith("@syntra.", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = Request.Query[key].ToString();
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the metadata level from the @syntra.metadata parameter.
    /// </summary>
    private static int GetMetadataLevel(IDictionary<string, string> queryParams)
    {
        if (queryParams.TryGetValue("@syntra.metadata", out string? level) &&
            int.TryParse(level, out int metadataLevel))
        {
            return metadataLevel;
        }
        return 0;
    }

    /// <summary>
    /// Builds the next link URL for pagination.
    /// </summary>
    private string BuildNextLink(string entity, IDictionary<string, string> queryParams, int nextSkip)
    {
        string baseUrl = $"{GetBaseUrl()}/{entity}";
        List<string> parameters = new List<string>();

        foreach ((string key, string value) in queryParams)
        {
            if (key.Equals("$skip", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("$skiptoken", StringComparison.OrdinalIgnoreCase))
                continue;

            parameters.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        parameters.Add($"$skip={nextSkip}");

        return $"{baseUrl}?{string.Join("&", parameters)}";
    }

    /// <summary>
    /// Gets the base URL for constructing OData context and next links.
    /// </summary>
    private string GetBaseUrl()
    {
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api";
    }

    /// <summary>
    /// Sanitizes an entity name to prevent SQL injection. Only allows alphanumeric characters and underscores.
    /// </summary>
    private static string SanitizeEntityName(string entity)
    {
        string sanitized = new string(entity.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (string.IsNullOrEmpty(sanitized))
            throw new ArgumentException($"Invalid entity name: '{entity}'");
        return sanitized;
    }
}
