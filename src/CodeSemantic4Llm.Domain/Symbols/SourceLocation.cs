namespace CodeSemantic4Llm.Domain.Symbols;

/// <summary>A one-based location in the original repository checkout.</summary>
public sealed record SourceLocation
{
    public SourceLocation(string relativePath, int line, int column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(column);

        RelativePath = relativePath.Replace('\\', '/');
        Line = line;
        Column = column;
    }

    public string RelativePath { get; }

    public int Line { get; }

    public int Column { get; }
}
