namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Resolves existing repository files without traversing links or leaving the checkout.</summary>
internal static class RepositorySourcePath
{
    public static string Validate(string repositoryRoot, string relativePath, bool allowMissing = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var segments = relativePath.Replace('\\', '/').Split('/');

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
            segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException(
                "A source path must be relative to its repository and cannot contain traversal segments.",
                nameof(relativePath));
        }

        var path = Path.GetFullPath(repositoryRoot);
        RejectLink(path);

        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            if (!allowMissing || Path.Exists(path) || new FileInfo(path).LinkTarget is not null)
            {
                RejectLink(path);
            }
        }

        return path;
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Refusing to access a symbolic link inside the source checkout: '{path}'.");
        }
    }
}
