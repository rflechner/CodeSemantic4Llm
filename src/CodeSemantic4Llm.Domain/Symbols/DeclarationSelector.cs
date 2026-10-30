namespace CodeSemantic4Llm.Domain.Symbols;

/// <summary>A human-readable code address, optionally narrowed by its identifier position.</summary>
public sealed record DeclarationSelector
{
    public DeclarationSelector(string relativePath, string name, int? line = null, int? column = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (line is <= 0 || column is <= 0 || (column.HasValue && !line.HasValue))
        {
            throw new ArgumentException("Use positive one-based positions; column requires line.");
        }

        RelativePath = relativePath.Replace('\\', '/');

        if (Path.IsPathRooted(RelativePath) || RelativePath.Contains(':') ||
            RelativePath.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Use a repository-relative source path without traversal.", nameof(relativePath));
        }

        Name = name;
        Line = line;
        Column = column;
    }

    public string RelativePath { get; }
    public string Name { get; }
    public int? Line { get; }
    public int? Column { get; }

    /// <summary>
    /// A literal, dot-delimited suffix for a qualified address, or null for a simple name.
    /// The leading dot prevents CliApplication.RunAsync from matching OtherCliApplication.RunAsync.
    /// </summary>
    public string? QualifiedNameSuffix => Name.Contains('.') ? "." + Name : null;

    /// <summary>
    /// Matches a simple, full or abbreviated qualified name at the requested source position.
    /// Callers must reject multiple matches rather than selecting an arbitrary overload or declaring type.
    /// </summary>
    public bool Matches(SymbolDefinition symbol) =>
        symbol.Location.RelativePath == RelativePath && MatchesName(symbol) &&
        (!Line.HasValue || symbol.Location.Line == Line) && (!Column.HasValue || symbol.Location.Column == Column);

    private bool MatchesName(SymbolDefinition symbol) =>
        symbol.Name == Name || symbol.QualifiedName == Name ||
        (QualifiedNameSuffix is { } suffix && symbol.QualifiedName.EndsWith(suffix, StringComparison.Ordinal));
}
