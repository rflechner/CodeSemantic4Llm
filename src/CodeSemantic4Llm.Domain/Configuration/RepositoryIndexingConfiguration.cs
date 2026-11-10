using System.Collections.ObjectModel;
using CodeSemantic4Llm.Domain.Source;

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
        Array.AsReadOnly(["**/.git/**", "**/bin/**", "**/obj/**", "**/.codesemantic4llm/**"]);

    public RepositoryIndexingConfiguration(
        IEnumerable<string> analyzerIds,
        IEnumerable<string>? exclusions = null,
        SemanticIndexingConfiguration? semanticIndexing = null,
        IReadOnlyDictionary<string, SourceFileExtensions>? fileExtensions = null,
        string? projectName = null,
        McpToolSelection? tools = null,
        bool sharedSummaries = false,
        RepositoryWatchConfiguration? watch = null,
        McpPathFormat mcpPaths = McpPathFormat.Both)
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

        SemanticIndexing = semanticIndexing ?? new SemanticIndexingConfiguration();
        FileExtensions = NormalizeFileExtensions(fileExtensions);
        ProjectName = NormalizeProjectName(projectName);
        Tools = tools ?? McpToolSelection.All;
        SharedSummaries = sharedSummaries;
        Watch = watch ?? new RepositoryWatchConfiguration();

        if (!Enum.IsDefined(mcpPaths))
        {
            throw new ArgumentOutOfRangeException(nameof(mcpPaths));
        }

        McpPaths = mcpPaths;
    }

    /// <summary>MCP response presentation only; changing this does not invalidate the index.</summary>
    public McpPathFormat McpPaths { get; }

    public int Version => CurrentVersion;

    /// <summary>Imports compatible Git-distributed descriptions; export remains an explicit operator action.</summary>
    public bool SharedSummaries { get; }

    /// <summary>Local quiet-period settings for the long-running watch command; not an index fingerprint input.</summary>
    public RepositoryWatchConfiguration Watch { get; }

    /// <summary>Optional display name. Repository identity and index storage remain based on the checkout path.</summary>
    public string? ProjectName { get; }

    /// <summary>Canonical, ordered IDs such as dotnet-csharp.</summary>
    public IReadOnlyList<string> AnalyzerIds { get; }

    /// <summary>Effective repository-relative glob exclusions, including the built-in defaults.</summary>
    public IReadOnlyList<string> Exclusions { get; }

    /// <summary>Language used for future natural-language semantic descriptions, not for source parsing.</summary>
    public SemanticIndexingConfiguration SemanticIndexing { get; }

    /// <summary>
    /// MCP tools this repository allows its server to advertise. Every tool is selected when the
    /// YAML declares no tools section. A selection never grants a capability the operator withheld
    /// when starting the process.
    /// </summary>
    public McpToolSelection Tools { get; }

    /// <summary>
    /// Extension overrides keyed by enabled analyzer ID. An entry replaces that analyzer's
    /// defaults; omitted analyzers retain the defaults supplied by their adapter.
    /// </summary>
    public IReadOnlyDictionary<string, SourceFileExtensions> FileExtensions { get; }

    private static string? NormalizeProjectName(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Project name cannot be empty.", nameof(value));
        }

        if (value.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029'))
        {
            throw new ArgumentException("Project name cannot contain control characters or line breaks.", nameof(value));
        }

        return value.Trim();
    }

    private IReadOnlyDictionary<string, SourceFileExtensions> NormalizeFileExtensions(
        IReadOnlyDictionary<string, SourceFileExtensions>? overrides)
    {
        var normalized = new Dictionary<string, SourceFileExtensions>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, extensions) in overrides ?? new Dictionary<string, SourceFileExtensions>())
        {
            var analyzerId = id.Trim().ToLowerInvariant();

            if (!AnalyzerIds.Contains(analyzerId, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"File extensions reference analyzer '{id}', which is not enabled.");
            }

            ArgumentNullException.ThrowIfNull(extensions);

            if (!normalized.TryAdd(analyzerId, extensions))
            {
                throw new ArgumentException($"Duplicate file extension overrides for analyzer '{analyzerId}'.");
            }
        }

        return new ReadOnlyDictionary<string, SourceFileExtensions>(normalized);
    }

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
