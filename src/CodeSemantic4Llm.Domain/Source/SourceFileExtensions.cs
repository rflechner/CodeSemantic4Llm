using System.Collections;

namespace CodeSemantic4Llm.Domain.Source;

/// <summary>
/// An immutable, non-empty selection of final file extensions. Matching is case-insensitive;
/// entries are literal extensions such as .cs, not paths, globs, or compound suffixes.
/// </summary>
public sealed class SourceFileExtensions : IReadOnlyList<string>
{
    private readonly IReadOnlyList<string> _extensions;

    public SourceFileExtensions(IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        var normalized = extensions.Select(Normalize).Distinct(StringComparer.Ordinal).ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("At least one file extension is required.", nameof(extensions));
        }

        _extensions = Array.AsReadOnly(normalized);
    }

    public int Count => _extensions.Count;

    public string this[int index] => _extensions[index];

    /// <summary>Matches the final extension without reading the file or inspecting its content.</summary>
    public bool Matches(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return _extensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerator<string> GetEnumerator() => _extensions.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static string Normalize(string? extension)
    {
        var normalized = extension?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(normalized)
            || normalized.Length < 2
            || normalized[0] != '.'
            || !normalized[1..].All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            throw new ArgumentException(
                $"Invalid file extension '{extension}'. Use a leading dot followed by letters, digits, '_' or '-', such as '.cs'.");
        }

        return normalized;
    }
}
