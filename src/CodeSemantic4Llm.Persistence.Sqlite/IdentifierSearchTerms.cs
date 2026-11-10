using System.Text.RegularExpressions;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>Produces derived PascalCase, camelCase, acronym and separator tokens without changing symbol identity.</summary>
internal static class IdentifierSearchTerms
{
    private static readonly Regex Boundaries = new(
        @"(?<=[\p{Ll}\p{N}])(?=\p{Lu})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})|[_\W]+",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Split(string name) => Boundaries.Replace(name, " ");
}
