using System.Text;

namespace PenguinConverters.Syntra.Consumer.SchemaDesigner.Extensions;

/// <summary>
/// Provides buffered console output for efficient rendering of schema definitions.
/// Accumulates lines in a <see cref="StringBuilder"/> and flushes to the console in a single operation.
/// </summary>
public class ConsoleWriter
{
    private readonly StringBuilder _buffer = new();

    /// <summary>
    /// Writes a line of text to the buffer.
    /// </summary>
    /// <param name="text">The text to write.</param>
    public void WriteLine(string text)
    {
        _buffer.AppendLine(text);
    }

    /// <summary>
    /// Writes an empty line to the buffer.
    /// </summary>
    public void WriteLine()
    {
        _buffer.AppendLine();
    }

    /// <summary>
    /// Writes text to the buffer without a trailing newline.
    /// </summary>
    /// <param name="text">The text to write.</param>
    public void Write(string text)
    {
        _buffer.Append(text);
    }

    /// <summary>
    /// Flushes the buffered content to the console in a single write operation.
    /// Clears the buffer after flushing.
    /// </summary>
    public void Flush()
    {
        if (_buffer.Length > 0)
        {
            Console.Write(_buffer.ToString());
            _buffer.Clear();
        }
    }

    /// <summary>
    /// Returns the current buffered content without flushing.
    /// </summary>
    /// <returns>The buffered text.</returns>
    public override string ToString() => _buffer.ToString();
}
