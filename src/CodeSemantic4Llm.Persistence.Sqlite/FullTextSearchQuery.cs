using System.Text.RegularExpressions;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>
/// Translates a short natural-language question into a bounded, literal FTS5 prefix
/// disjunction. It never passes caller-controlled FTS operators or SQL through.
/// </summary>
internal static class FullTextSearchQuery
{
    private const int MaximumCharacters = 512;
    private const int MaximumTerms = 12;

    private static readonly Regex Words = new(
        @"[\p{L}\p{M}\p{N}]+",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common English and French question/grammar words, not technical terms.
        "a", "an", "and", "are", "as", "at", "by", "can", "do", "does", "for",
        "from", "how", "i", "in", "is", "it", "my", "of", "on", "or", "our",
        "that", "the", "this", "to", "we", "what", "when", "where", "which",
        "who", "with", "you", "your",
        "au", "aux", "avec", "ce", "ces", "comment", "dans", "de", "des", "du",
        "elle", "en", "est", "et", "il", "je", "la", "le", "les", "ma", "mes",
        "mon", "nous", "ou", "où", "par", "pour", "que", "quel", "quelle",
        "quelles", "quels", "qui", "quoi", "se", "sont", "sur", "ta", "tes",
        "ton", "tu", "un", "une", "vous"
    };

    public static string Build(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (query.Length > MaximumCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query), $"Search query must not exceed {MaximumCharacters} characters.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var terms = new List<string>(MaximumTerms);

        foreach (Match word in Words.Matches(query))
        {
            var term = word.Value;

            if (term.Length < 2 || term.Length > 64 || StopWords.Contains(term) || !seen.Add(term))
            {
                continue;
            }

            // Quoted token + trailing prefix operator: user punctuation, quotes, column
            // filters, boolean operators and FTS5 expressions cannot alter the query.
            terms.Add($"\"{term}\"*");

            if (terms.Count == MaximumTerms)
            {
                break;
            }
        }

        if (terms.Count == 0)
        {
            throw new ArgumentException("Search query must contain at least one searchable word.", nameof(query));
        }

        return string.Join(" OR ", terms);
    }
}
