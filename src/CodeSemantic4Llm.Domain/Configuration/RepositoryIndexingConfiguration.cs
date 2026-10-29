namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>
/// Validated, repository-local indexing settings. The source checkout remains authoritative;
/// this configuration does not describe where to duplicate or store source files.
/// </summary>
public sealed class RepositoryIndexingConfiguration
{
    public const string FileName = "codesemantic4llm.yaml";

    public const int CurrentVersion = 1;

    /// <summary>Always excluded, at any depth, even when the YAML has no exclude section.</summary>
    public static IReadOnlyList<string> BuiltInExclusions { get; } =
        Array.AsReadOnly(["**/.git/**", "**/bin/**", "**/obj/**"]);

    public RepositoryIndexingConfiguration(
        IEnumerable<string> analyzerIds,
        IEnumerable<string>? exclusions = null)
    {
        ArgumentNullException.ThrowIfNull(analyzerIds);

        var normalizedIds = analyzerIds
            .Select(id => id?.Trim().ToLowerInvariant())
            .ToArray();

        if (normalizedIds.Length == 0 || normalizedIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty analyzer ID is required.", nameof(analyzerIds));
        }

        AnalyzerIds = Array.AsReadOnly(
            normalizedIds.Select(id => id!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        Exclusions = Array.AsReadOnly(
            BuiltInExclusions.Concat(exclusions ?? [])
                .Select(NormalizeExclusion)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public int Version => CurrentVersion;

    /// <summary>Canonical, ordered IDs such as dotnet-csharp.</summary>
    public IReadOnlyList<string> AnalyzerIds { get; }

    /// <summary>Effective repository-relative glob exclusions, including the built-in defaults.</summary>
    public IReadOnlyList<string> Exclusions { get; }

    private static string NormalizeExclusion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Exclusion patterns cannot be empty.");
        }

        var pattern = value.Trim().Replace('\\', '/');

        while (pattern.StartsWith("./", StringComparison.Ordinal))
        {
            pattern = pattern[2..];
        }

        if (pattern.StartsWith("/", StringComparison.Ordinal)
            || pattern.Contains(':', StringComparison.Ordinal)
            || pattern.Split('/').Any(segment => segment is "." or "..")
            || pattern.TrimEnd('/').Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid repository-relative exclusion pattern: '{value}'.");
        }

        // Plain names and trailing slashes denote directories; glob patterns may also target files.
        if (pattern.EndsWith("/", StringComparison.Ordinal))
        {
            // The trailing slash creates an empty segment only after the validation above,
            // so normalize it before adding the recursive suffix.
            pattern = pattern.TrimEnd('/') + "/**";
        }
        else if (!pattern.Contains('*') && !pattern.Contains('?') && !pattern.Contains('['))
        {
            pattern += "/**";
        }

        return pattern;
    }
}
