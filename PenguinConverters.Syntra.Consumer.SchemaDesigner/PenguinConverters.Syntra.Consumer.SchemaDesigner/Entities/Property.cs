namespace PenguinConverters.Syntra.Consumer.SchemaDesigner.Entities;

/// <summary>
/// Tracks metadata about observed property values across multiple entities.
/// Determines the appropriate SQL type based on the data patterns encountered.
/// </summary>
public class Property
{
    #region Fields

    private int _observationCount;
    private int _nullCount;
    private int _maxLength;
    private bool _hasUnicode;
    private bool _hasBoolean;
    private bool _hasInteger;
    private bool _hasLong;
    private bool _hasDouble;
    private bool _hasDateTime;
    private bool _hasGuid;
    private bool _hasString;
    private bool _hasByteArray;
    private long _minIntValue = long.MaxValue;
    private long _maxIntValue = long.MinValue;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Property"/> class.
    /// </summary>
    /// <param name="name">The property name.</param>
    public Property(string name)
    {
        Name = name;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the total number of observations recorded for this property.
    /// </summary>
    public int ObservationCount => _observationCount;

    /// <summary>
    /// Gets the number of null values observed.
    /// </summary>
    public int NullCount => _nullCount;

    /// <summary>
    /// Gets the maximum string length observed across all values.
    /// </summary>
    public int MaxLength => _maxLength;

    /// <summary>
    /// Gets a value indicating whether any observed values contained non-ASCII Unicode characters.
    /// </summary>
    public bool HasUnicode => _hasUnicode;

    #endregion

    #region Methods

    /// <summary>
    /// Records an observation of a property value, updating the statistical profile.
    /// </summary>
    /// <param name="value">The observed value, or <c>null</c>.</param>
    public void Observe(object? value)
    {
        Interlocked.Increment(ref _observationCount);

        if (value is null)
        {
            Interlocked.Increment(ref _nullCount);
            return;
        }

        switch (value)
        {
            case bool:
                _hasBoolean = true;
                break;

            case byte or sbyte or short or ushort or int or uint:
                _hasInteger = true;
                long intVal = Convert.ToInt64(value);
                UpdateIntRange(intVal);
                break;

            case long or ulong:
                _hasLong = true;
                long longVal = Convert.ToInt64(value);
                UpdateIntRange(longVal);
                break;

            case float or double or decimal:
                _hasDouble = true;
                break;

            case DateTime or DateTimeOffset:
                _hasDateTime = true;
                break;

            case Guid:
                _hasGuid = true;
                break;

            case byte[]:
                _hasByteArray = true;
                int byteLen = ((byte[])value).Length;
                UpdateMaxLength(byteLen);
                break;

            case string strValue:
                _hasString = true;
                UpdateMaxLength(strValue.Length);

                if (!_hasUnicode && strValue.Any(c => c > 127))
                {
                    _hasUnicode = true;
                }
                break;

            default:
                _hasString = true;
                string str = value.ToString() ?? string.Empty;
                UpdateMaxLength(str.Length);
                break;
        }
    }

    /// <summary>
    /// Converts this property's observed metadata into a <see cref="Column"/> definition.
    /// </summary>
    /// <returns>A <see cref="Column"/> reflecting the inferred SQL type.</returns>
    public Column ToColumn()
    {
        Column column = new Column(Name)
        {
            IsNullable = _nullCount > 0 || _observationCount == 0
        };

        // Determine the best SQL type based on observed values
        if (_hasByteArray)
        {
            column.SqlType = SqlColumnType.VarBinary;
            column.MaxLength = _maxLength > 0 ? _maxLength : null;
        }
        else if (_hasBoolean && !_hasString && !_hasInteger && !_hasLong && !_hasDouble && !_hasDateTime && !_hasGuid)
        {
            column.SqlType = SqlColumnType.Bit;
        }
        else if (_hasDateTime && !_hasString && !_hasInteger && !_hasLong && !_hasDouble && !_hasGuid)
        {
            column.SqlType = SqlColumnType.DateTime2;
        }
        else if (_hasGuid && !_hasString && !_hasInteger && !_hasLong && !_hasDouble && !_hasDateTime)
        {
            column.SqlType = SqlColumnType.UniqueIdentifier;
        }
        else if ((_hasInteger || _hasLong) && !_hasString && !_hasDouble && !_hasDateTime && !_hasGuid)
        {
            column.SqlType = InferIntegerType();
        }
        else if (_hasDouble && !_hasString && !_hasDateTime && !_hasGuid)
        {
            column.SqlType = SqlColumnType.Float;
        }
        else
        {
            // Default to string types
            column.SqlType = _hasUnicode ? SqlColumnType.NVarChar : SqlColumnType.VarChar;
            column.MaxLength = _maxLength > 0 ? _maxLength : null;
        }

        return column;
    }

    /// <summary>
    /// Infers the integer SQL type for the observed values, widening but never narrowing.
    /// </summary>
    /// <remarks>
    /// Deliberately does <em>not</em> return <see cref="SqlColumnType.TinyInt"/> or
    /// <see cref="SqlColumnType.SmallInt"/>, even when every observed value would fit.
    ///
    /// Inference runs over a sample, but the generated <c>CREATE TABLE</c> is used against real
    /// data for the lifetime of the table. Narrowing to the sample makes the schema fail the
    /// first time reality exceeds it: profile 10,000 users whose employee numbers happen to fall
    /// in 1-200 and a <c>TINYINT</c> column overflows the moment number 4711 arrives. The saving
    /// is three bytes per row; the cost is a failed sync in production.
    ///
    /// So the floor is <see cref="SqlColumnType.Int"/>, and the only widening is to
    /// <see cref="SqlColumnType.BigInt"/> when a value genuinely exceeds <see cref="int"/> range.
    /// Both narrower members remain on the enum: they are valid for hand-authored schema, they
    /// are simply not inferred from a sample.
    /// </remarks>
    private SqlColumnType InferIntegerType()
    {
        if (_minIntValue < int.MinValue || _maxIntValue > int.MaxValue)
        {
            return SqlColumnType.BigInt;
        }

        return SqlColumnType.Int;
    }

    /// <summary>
    /// Thread-safe update of the maximum observed length.
    /// </summary>
    private void UpdateMaxLength(int length)
    {
        int current;
        do
        {
            current = _maxLength;
            if (length <= current) return;
        }
        while (Interlocked.CompareExchange(ref _maxLength, length, current) != current);
    }

    /// <summary>
    /// Updates the observed integer value range.
    /// </summary>
    private void UpdateIntRange(long value)
    {
        long current;
        do
        {
            current = _minIntValue;
            if (value >= current) break;
        }
        while (Interlocked.CompareExchange(ref _minIntValue, value, current) != current);

        do
        {
            current = _maxIntValue;
            if (value <= current) break;
        }
        while (Interlocked.CompareExchange(ref _maxIntValue, value, current) != current);
    }

    #endregion
}
