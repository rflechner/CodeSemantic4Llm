using System.Text.RegularExpressions;

namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>
/// The language for future generated semantic descriptions of source code.
/// This does not control which programming language an analyzer parses.
/// </summary>
public sealed class SemanticIndexingConfiguration
{
    public const string DefaultLanguage = "en";

    // A deliberately bounded BCP 47 subset: language, optional script and optional region.
    private static readonly Regex LanguageTagPattern = new(
        @"^[A-Za-z]{2,3}(?:-[A-Za-z]{4})?(?:-(?:[A-Za-z]{2}|[0-9]{3}))?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    public SemanticIndexingConfiguration(string? language = DefaultLanguage, LlmSummaryConfiguration? llm = null)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("The semantic indexing language cannot be empty.", nameof(language));
        }

        var normalized = language.Trim();

        if (!LanguageTagPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                $"Invalid semantic indexing language code '{normalized}'. " +
                "Use a language tag such as 'en', 'fr', 'fr-CH', or 'zh-Hant-TW'.",
                nameof(language));
        }

        var segments = normalized.Split('-');

        for (var index = 0; index < segments.Length; index++)
        {
            if (index == 0)
            {
                segments[index] = segments[index].ToLowerInvariant();
            }
            else if (segments[index].Length == 4)
            {
                segments[index] = char.ToUpperInvariant(segments[index][0])
                    + segments[index][1..].ToLowerInvariant();
            }
            else if (segments[index].Length == 2)
            {
                segments[index] = segments[index].ToUpperInvariant();
            }
        }

        Language = string.Join("-", segments);
        Llm = llm ?? new LlmSummaryConfiguration();
    }

    /// <summary>Normalized human-language tag for future semantic descriptions.</summary>
    public string Language { get; }

    /// <summary>Opt-in LLM descriptions for indexed methods and type declarations.</summary>
    public LlmSummaryConfiguration Llm { get; }
}
