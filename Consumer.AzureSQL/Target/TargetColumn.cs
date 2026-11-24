namespace PenguinConverters.Syntra.Consumer.AzureSQL.Target;

/// <summary>
/// Describes one writable column of the target table as the database catalog defines it.
/// </summary>
internal sealed class TargetColumn
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="TargetColumn"/> class.
    /// </summary>
    /// <param name="name">The column name as the catalog spells it.</param>
    /// <param name="baseType">The upper-case system type name, such as <c>NVARCHAR</c>.</param>
    /// <param name="declaration">The full type declaration, such as <c>NVARCHAR(255)</c>.</param>
    /// <param name="collation">The column collation, or <c>null</c> for a type that has none.</param>
    /// <param name="maxLength">The storage length in bytes; <c>-1</c> for a MAX type.</param>
    public TargetColumn(string name, string baseType, string declaration, string? collation, short maxLength = 0)
    {
        Name = name;
        BaseType = baseType;
        Declaration = declaration;
        Collation = collation;
        MaxLength = maxLength;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the column name as the catalog spells it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the upper-case system type name without length, precision or scale.
    /// </summary>
    public string BaseType { get; }

    /// <summary>
    /// Gets the full type declaration including length, precision or scale.
    /// </summary>
    public string Declaration { get; }

    /// <summary>
    /// Gets the column collation, or <c>null</c> for a type that has none.
    /// </summary>
    public string? Collation { get; }

    /// <summary>
    /// Gets the storage length in bytes; <c>-1</c> for a MAX type.
    /// </summary>
    public short MaxLength { get; }

    #endregion
}
