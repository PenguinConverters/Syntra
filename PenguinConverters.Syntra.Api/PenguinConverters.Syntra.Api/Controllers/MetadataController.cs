using System.Data;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using PenguinConverters.Syntra.Api.Settings;

namespace PenguinConverters.Syntra.Api.Controllers;

/// <summary>
/// Serves the OData $metadata endpoint, dynamically building an Entity Data Model (EDM)
/// from the SQL Server database schema.
/// </summary>
/// <remarks>
/// The EDM is constructed at runtime by querying INFORMATION_SCHEMA.TABLES and
/// INFORMATION_SCHEMA.COLUMNS. Entity types are derived from views matching the
/// <c>SP_S1FE_*_READ</c> naming pattern. SQL types are mapped to EDM primitive types
/// following the OData CSDL specification.
/// </remarks>
[ApiController]
[Route("api/$metadata")]
[Authorize]
public class MetadataController : ControllerBase
{
    private static readonly XNamespace EdmxNamespace = "http://docs.oasis-open.org/odata/ns/edmx";
    private static readonly XNamespace EdmNamespace = "http://docs.oasis-open.org/odata/ns/edm";

    private readonly ILogger<MetadataController> _logger;
    private readonly DatabaseSettings _dbSettings;
    private readonly ITokenAcquisition _tokenAcquisition;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataController"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="dbSettings">The database settings.</param>
    /// <param name="tokenAcquisition">The token acquisition service.</param>
    public MetadataController(
        ILogger<MetadataController> logger,
        IOptions<DatabaseSettings> dbSettings,
        ITokenAcquisition tokenAcquisition)
    {
        _logger = logger;
        _dbSettings = dbSettings.Value;
        _tokenAcquisition = tokenAcquisition;
    }

    /// <summary>
    /// Returns the OData $metadata document (CSDL) describing all available entity types
    /// and entity sets derived from the database schema.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An XML document in CSDL format.</returns>
    /// <response code="200">Returns the CSDL metadata document.</response>
    [HttpGet]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMetadata(CancellationToken cancellationToken)
    {
        try
        {
            await using SqlConnection connection = await CreateConnectionAsync(cancellationToken);

            List<string> entities = await DiscoverEntitiesAsync(connection, cancellationToken);
            Dictionary<string, List<ColumnInfo>> columnsByEntity = await GetColumnMetadataAsync(connection, entities, cancellationToken);

            XDocument edmx = BuildEdmxDocument(entities, columnsByEntity);

            return Content(edmx.ToString(), "application/xml");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate $metadata document");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Failed to generate metadata document." });
        }
    }

    /// <summary>
    /// Discovers available entity sets by querying for views/procedures matching SP_S1FE_*_READ.
    /// </summary>
    private static async Task<List<string>> DiscoverEntitiesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        List<string> entities = new List<string>();

        const string sql = @"
            SELECT DISTINCT
                CASE
                    WHEN TABLE_NAME LIKE 'SP_S1FE_%_READ'
                        THEN SUBSTRING(TABLE_NAME, 9, LEN(TABLE_NAME) - 13)
                    ELSE TABLE_NAME
                END AS EntityName,
                TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME LIKE 'SP_S1FE_%_READ'
               OR TABLE_TYPE = 'VIEW'
            UNION
            SELECT DISTINCT
                SUBSTRING(SPECIFIC_NAME, 9, LEN(SPECIFIC_NAME) - 13) AS EntityName,
                SPECIFIC_NAME AS TABLE_NAME
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'PROCEDURE'
              AND SPECIFIC_NAME LIKE 'SP_S1FE_%_READ'
            ORDER BY EntityName";

        await using SqlCommand command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entities.Add(reader.GetString(0));
        }

        return entities.Distinct().ToList();
    }

    /// <summary>
    /// Retrieves column metadata from INFORMATION_SCHEMA.COLUMNS for each discovered entity.
    /// </summary>
    private static async Task<Dictionary<string, List<ColumnInfo>>> GetColumnMetadataAsync(
        SqlConnection connection, List<string> entities, CancellationToken cancellationToken)
    {
        Dictionary<string, List<ColumnInfo>> result = new Dictionary<string, List<ColumnInfo>>();

        foreach (string entity in entities)
        {
            string viewName = $"SP_S1FE_{entity}_READ";

            const string sql = @"
                SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                       IS_NULLABLE, NUMERIC_PRECISION, NUMERIC_SCALE, ORDINAL_POSITION
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @ViewName
                ORDER BY ORDINAL_POSITION";

            await using SqlCommand command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@ViewName", viewName);

            List<ColumnInfo> columns = new List<ColumnInfo>();
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new ColumnInfo
                {
                    Name = reader.GetString(0),
                    DataType = reader.GetString(1),
                    MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    IsNullable = reader.GetString(3) == "YES",
                    NumericPrecision = reader.IsDBNull(4) ? null : reader.GetByte(4),
                    NumericScale = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    OrdinalPosition = reader.GetInt32(6)
                });
            }

            if (columns.Count > 0)
                result[entity] = columns;
        }

        return result;
    }

    /// <summary>
    /// Builds the EDMX document containing entity types, entity sets, and a default container.
    /// </summary>
    private static XDocument BuildEdmxDocument(
        List<string> entities, Dictionary<string, List<ColumnInfo>> columnsByEntity)
    {
        XElement schemaElement = new XElement(EdmNamespace + "Schema",
            new XAttribute("Namespace", "PenguinConverters.Syntra"),
            new XAttribute("xmlns", EdmNamespace.NamespaceName));

        XElement containerElement = new XElement(EdmNamespace + "EntityContainer",
            new XAttribute("Name", "SyntraContainer"));

        foreach (string entity in entities)
        {
            if (!columnsByEntity.TryGetValue(entity, out List<ColumnInfo>? columns))
                continue;

            // Build EntityType
            XElement entityType = new XElement(EdmNamespace + "EntityType",
                new XAttribute("Name", entity));

            // Use first column as key if it ends with "Id" or "Identity", otherwise use first column
            ColumnInfo keyColumn = columns.FirstOrDefault(c =>
                c.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
                c.Name.EndsWith("Identity", StringComparison.OrdinalIgnoreCase)) ?? columns[0];

            entityType.Add(new XElement(EdmNamespace + "Key",
                new XElement(EdmNamespace + "PropertyRef",
                    new XAttribute("Name", keyColumn.Name))));

            foreach (ColumnInfo column in columns)
            {
                XElement property = new XElement(EdmNamespace + "Property",
                    new XAttribute("Name", column.Name),
                    new XAttribute("Type", MapSqlTypeToEdm(column.DataType)));

                if (column.IsNullable)
                    property.Add(new XAttribute("Nullable", "true"));
                else
                    property.Add(new XAttribute("Nullable", "false"));

                if (column.MaxLength.HasValue && column.MaxLength.Value > 0 && column.MaxLength.Value < int.MaxValue)
                    property.Add(new XAttribute("MaxLength", column.MaxLength.Value));

                if (column.NumericPrecision.HasValue)
                    property.Add(new XAttribute("Precision", column.NumericPrecision.Value));

                if (column.NumericScale.HasValue && column.NumericScale.Value > 0)
                    property.Add(new XAttribute("Scale", column.NumericScale.Value));

                entityType.Add(property);
            }

            schemaElement.Add(entityType);

            // Add EntitySet to container
            containerElement.Add(new XElement(EdmNamespace + "EntitySet",
                new XAttribute("Name", entity),
                new XAttribute("EntityType", $"PenguinConverters.Syntra.{entity}")));
        }

        schemaElement.Add(containerElement);

        XDocument edmx = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(EdmxNamespace + "Edmx",
                new XAttribute("Version", "4.0"),
                new XAttribute(XNamespace.Xmlns + "edmx", EdmxNamespace.NamespaceName),
                new XElement(EdmxNamespace + "DataServices",
                    schemaElement)));

        return edmx;
    }

    /// <summary>
    /// Maps SQL Server data types to OData EDM primitive types.
    /// </summary>
    /// <param name="sqlType">The SQL Server data type name.</param>
    /// <returns>The corresponding EDM type name.</returns>
    internal static string MapSqlTypeToEdm(string sqlType)
    {
        return sqlType.ToLowerInvariant() switch
        {
            "int" => "Edm.Int32",
            "bigint" => "Edm.Int64",
            "smallint" => "Edm.Int16",
            "tinyint" => "Edm.Byte",
            "bit" => "Edm.Boolean",
            "decimal" or "numeric" => "Edm.Decimal",
            "float" => "Edm.Double",
            "real" => "Edm.Single",
            "money" or "smallmoney" => "Edm.Decimal",
            "char" or "varchar" or "text" => "Edm.String",
            "nchar" or "nvarchar" or "ntext" => "Edm.String",
            "datetime" or "datetime2" or "smalldatetime" => "Edm.DateTimeOffset",
            "date" => "Edm.Date",
            "time" => "Edm.TimeOfDay",
            "datetimeoffset" => "Edm.DateTimeOffset",
            "uniqueidentifier" => "Edm.Guid",
            "binary" or "varbinary" or "image" => "Edm.Binary",
            "xml" => "Edm.String",
            "timestamp" or "rowversion" => "Edm.Binary",
            "sql_variant" => "Edm.String",
            "geography" => "Edm.GeographyPoint",
            "geometry" => "Edm.GeometryPoint",
            _ => "Edm.String"
        };
    }

    /// <summary>
    /// Creates a SQL connection using app-level credentials (metadata is not user-specific).
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
                _logger.LogWarning(ex, "Failed to acquire app token for metadata, attempting without token");
            }
        }

        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Represents column metadata from INFORMATION_SCHEMA.COLUMNS.
    /// </summary>
    private sealed class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public int? MaxLength { get; set; }
        public bool IsNullable { get; set; }
        public byte? NumericPrecision { get; set; }
        public int? NumericScale { get; set; }
        public int OrdinalPosition { get; set; }
    }
}
