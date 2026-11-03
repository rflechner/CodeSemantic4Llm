using CodeSemantic4Llm.Application.Abstractions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>
/// Recursively discovers existing paths in deterministic directory order, without entering
/// excluded directories, directory symlinks, or Git working trees outside the root.
/// </summary>
public sealed class FileSystemRepositoryFileEnumerator : IRepositoryFileEnumerator
{
    public IEnumerable<string> EnumerateFiles(
        string repositoryRoot,
        IReadOnlyList<string> exclusionPatterns,
        CancellationToken cancellationToken = default,
        string? folder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(exclusionPatterns);

        var root = new DirectoryInfo(Path.GetFullPath(repositoryRoot));

        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Repository root '{root.FullName}' does not exist.");
        }

        var excludedFiles = new Matcher(StringComparison.OrdinalIgnoreCase);
        var excludedDirectories = new Matcher(StringComparison.OrdinalIgnoreCase);

        foreach (var pattern in exclusionPatterns)
        {
            excludedFiles.AddInclude(pattern);

            // Only exclusions of a whole subtree may prevent traversal.
            // A file glob such as **/*.g.cs must not suppress unrelated files in that folder.
            if (pattern.EndsWith("/**", StringComparison.Ordinal))
            {
                excludedDirectories.AddInclude(pattern);
            }
        }

        var start = folder is null ? root : ResolveFolder(root, folder, excludedDirectories);
        if (start is null)
        {
            yield break;
        }

        var pending = new Stack<DirectoryInfo>();
        pending.Push(start);

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var file in directory.EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var path = Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');

                if (!excludedFiles.Match(path).HasMatches)
                {
                    yield return path;
                }
            }

            // Reverse before pushing so that the next directory visited is alphabetically first.
            foreach (var child in directory.EnumerateDirectories()
                         .OrderByDescending(d => d.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var path = Path.GetRelativePath(root.FullName, child.FullName).Replace('\\', '/');

                if (!excludedDirectories.Match(path + "/__indexing_directory_probe__").HasMatches)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static DirectoryInfo? ResolveFolder(DirectoryInfo root, string folder, Matcher exclusions)
    {
        var relative = folder.Replace('\\', '/').TrimEnd('/');
        if (relative.StartsWith("./", StringComparison.Ordinal))
        {
            relative = relative[2..];
        }

        // Validate each component before enumerating children so links cannot escape the checkout.
        RepositorySourcePath.Validate(root.FullName, relative);
        var directory = root;
        foreach (var segment in relative.Split('/'))
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var children = directory.EnumerateDirectories().Where(child => child.Name.Equals(segment, comparison));
            directory = children.OrderByDescending(child => child.Name == segment).FirstOrDefault()
                ?? throw new DirectoryNotFoundException($"Index folder '{folder}' must be an existing repository directory.");

            // Use on-disk casing so a subsequent checkout-wide run sees the same repository-relative paths.
            var prefix = Path.GetRelativePath(root.FullName, directory.FullName).Replace('\\', '/');
            if (exclusions.Match(prefix + "/__indexing_directory_probe__").HasMatches)
            {
                return null;
            }
        }

        return directory;
    }

}
