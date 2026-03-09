namespace PenguinConverters.Syntra.Consumer.SchemaDesigner.Entities;

/// <summary>
/// Represents an inferred SQL column definition based on observed entity property values.
/// Supports SQL Server data types including VARCHAR, NVARCHAR, integer types, BIT, and DATETIME2.
/// </summary>
public class Column
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Column"/> class.
    /// </summary>
    /// <param name="name">The column name.</param>
    public Column(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the inferred SQL data type.
    /// </summary>
    public SqlColumnType SqlType { get; set; } = SqlColumnType.NVarChar;

    /// <summary>
    /// Gets or sets the maximum observed length for variable-length types.
    /// When <c>null</c>, the column uses <c>MAX</c> length specification.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the column allows NULL values.
    /// </summary>
    public bool IsNullable { get; set; } = true;

    /// <summary>
    /// Calculates the appropriate SQL length specification based on the observed maximum length.
    /// Rounds up to the next power of two or common boundary for efficiency.
    /// </summary>
    /// <returns>
    /// The length to use in the column definition, or <c>-1</c> to indicate <c>MAX</c>.
    /// </returns>
    public int CalculateLength()
    {
        if (MaxLength is null || MaxLength <= 0)
        {
            return -1; // MAX
        }

        int length = MaxLength.Value;

        // Add 20% padding for growth
        length = (int)(length * 1.2);

        // Round up to common SQL boundaries
        if (length <= 16) return 16;
        if (length <= 32) return 32;
        if (length <= 50) return 50;
        if (length <= 100) return 100;
        if (length <= 128) return 128;
        if (length <= 256) return 256;
        if (length <= 450) return 450;
        if (length <= 512) return 512;
        if (length <= 1024) return 1024;
        if (length <= 2048) return 2048;
        if (length <= 4000) return 4000;

        return -1; // MAX
    }

    /// <summary>
    /// Generates the T-SQL column definition for use in a CREATE TABLE statement.
    /// </summary>
    /// <returns>
    /// A T-SQL column definition string (e.g., <c>[Name] NVARCHAR(256) NOT NULL</c>).
    /// </returns>
    public string GetTSQLDefinition()
    {
        string nullable = IsNullable ? "NULL" : "NOT NULL";

        string typeString = SqlType switch
        {
            SqlColumnType.VarChar => FormatStringType("VARCHAR"),
            SqlColumnType.NVarChar => FormatStringType("NVARCHAR"),
            SqlColumnType.VarBinary => FormatStringType("VARBINARY"),
            SqlColumnType.TinyInt => "TINYINT",
            SqlColumnType.SmallInt => "SMALLINT",
            SqlColumnType.Int => "INT",
            SqlColumnType.BigInt => "BIGINT",
            SqlColumnType.Bit => "BIT",
            SqlColumnType.Float => "FLOAT",
            SqlColumnType.DateTime2 => "DATETIME2(7)",
            SqlColumnType.UniqueIdentifier => "UNIQUEIDENTIFIER",
            _ => "NVARCHAR(MAX)"
        };

        return $"[{Name}] {typeString} {nullable}";
    }

    /// <summary>
    /// Formats a variable-length type string with the calculated length.
    /// </summary>
    private string FormatStringType(string typeName)
    {
        int length = CalculateLength();
        string lengthStr = length < 0 ? "MAX" : length.ToString();
        return $"{typeName}({lengthStr})";
    }

    /// <summary>
    /// Returns a string representation of this column definition.
    /// </summary>
    public override string ToString() => GetTSQLDefinition();
}

/// <summary>
/// SQL Server column data types supported by the SchemaDesigner.
/// </summary>
public enum SqlColumnType
{
    /// <summary>
    /// ASCII variable-length string (VARCHAR).
    /// </summary>
    VarChar,

    /// <summary>
    /// Unicode variable-length string (NVARCHAR).
    /// </summary>
    NVarChar,

    /// <summary>
    /// Variable-length binary data (VARBINARY).
    /// </summary>
    VarBinary,

    /// <summary>
    /// 8-bit unsigned integer (0 to 255).
    /// </summary>
    TinyInt,

    /// <summary>
    /// 16-bit signed integer.
    /// </summary>
    SmallInt,

    /// <summary>
    /// 32-bit signed integer.
    /// </summary>
    Int,

    /// <summary>
    /// 64-bit signed integer.
    /// </summary>
    BigInt,

    /// <summary>
    /// Boolean (0 or 1).
    /// </summary>
    Bit,

    /// <summary>
    /// Floating-point number.
    /// </summary>
    Float,

    /// <summary>
    /// Date and time with fractional seconds precision.
    /// </summary>
    DateTime2,

    /// <summary>
    /// Globally unique identifier (GUID).
    /// </summary>
    UniqueIdentifier
}
