using System.Security.Cryptography;
using System.Text;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Reads original files in place, validating each path component before and after the read.</summary>
public sealed class RepositorySourceReader : IRepositorySourceReader
{
    public async Task<SourceFileSnapshot> ReadAsync(
        string repositoryRoot, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = ValidatePath(repositoryRoot, relativePath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        ValidatePath(repositoryRoot, relativePath);

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        return new SourceFileSnapshot(new SourceDocument(relativePath, content), hash);
    }

    private static string ValidatePath(string repositoryRoot, string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':')
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("A source path must be relative to its repository and cannot contain traversal segments.");
        }

        var path = Path.GetFullPath(repositoryRoot);
        RejectLink(path);

        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
            RejectLink(path);
        }

        return path;
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Refusing to read a symbolic link while indexing: '{path}'.");
        }
    }
}
