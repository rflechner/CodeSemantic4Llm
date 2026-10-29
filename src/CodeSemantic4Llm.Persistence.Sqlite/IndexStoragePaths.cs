using System.Security.Cryptography;
using System.Text;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>Maps checkout identities to cache directories without using branch names as filesystem paths.</summary>
public sealed class IndexStoragePaths
{
    public const string EnvironmentVariable = "CODESEMANTIC4LLM_INDEX_ROOT";

    public IndexStoragePaths(string storageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        Root = Path.GetFullPath(storageRoot);
    }

    public string Root { get; }

    public static IndexStoragePaths FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured))
            {
                throw new ArgumentException($"{EnvironmentVariable} must be an absolute path.");
            }

            return new IndexStoragePaths(configured);
        }

        return new IndexStoragePaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeSemantic4Llm", "indexes"));
    }

    public string GetDatabasePath(string repositoryRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        if (string.Equals(Path.TrimEndingDirectorySeparator(Root), root, comparison) || Root.StartsWith(prefix, comparison))
        {
            throw new ArgumentException("Index storage must be outside the source repository.");
        }

        var identity = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Path.Combine(Root, key, "index.sqlite");
    }
}
