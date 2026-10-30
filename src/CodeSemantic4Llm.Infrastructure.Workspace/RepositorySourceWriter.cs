using System.Security.Cryptography;
using System.Text;
using CodeSemantic4Llm.Application.Abstractions;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>
/// Writes only operator-selected checkout files through a temporary sibling and replacement rename.
/// Uses an expected SHA-256 hash and does not write any source copy to the index cache.
/// </summary>
public sealed class RepositorySourceWriter : IRepositorySourceWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public async Task<string> ReplaceAsync(
        string repositoryRoot, string relativePath, string expectedContentHash,
        string replacementSource, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentHash);
        ArgumentNullException.ThrowIfNull(replacementSource);
        cancellationToken.ThrowIfCancellationRequested();

        var path = RepositorySourcePath.Validate(repositoryRoot, relativePath);
        var original = await File.ReadAllBytesAsync(path, cancellationToken);
        EnsureExpectedHash(original, expectedContentHash);

        // The source reader supports BOM detection. This initial editor explicitly limits
        // writes to UTF-8, so that other source encodings are never silently converted.
        var utf8Bom = original.AsSpan().StartsWith(Utf8Bom);
        if (original.AsSpan().StartsWith([0xFF, 0xFE]) ||
            original.AsSpan().StartsWith([0xFE, 0xFF]) ||
            original.AsSpan().StartsWith([0x00, 0x00, 0xFE, 0xFF]) ||
            original.AsSpan().StartsWith([0xFF, 0xFE, 0x00, 0x00]))
        {
            throw new NotSupportedException("Method editing currently supports UTF-8 source files only.");
        }

        // Reject invalid UTF-8 instead of writing replacement characters into a source file.
        _ = StrictUtf8.GetString(original.AsSpan(utf8Bom ? Utf8Bom.Length : 0));
        var replacement = StrictUtf8.GetBytes(replacementSource);
        byte[] bytes = utf8Bom ? [.. Utf8Bom, .. replacement] : replacement;
        var newHash = Convert.ToHexString(SHA256.HashData(bytes));
        var temporary = Path.Combine(Path.GetDirectoryName(path)!,
            $".codesemantic4llm-{Guid.NewGuid():N}.tmp");

        try
        {
            // The temporary file stays within the original directory, keeping rename
            // on the same filesystem. Neither source code nor temp paths are indexed.
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 64 * 1024, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            }

            cancellationToken.ThrowIfCancellationRequested();
            RepositorySourcePath.Validate(repositoryRoot, relativePath);
            var current = await File.ReadAllBytesAsync(path, cancellationToken);
            EnsureExpectedHash(current, expectedContentHash);
            cancellationToken.ThrowIfCancellationRequested();
            RepositorySourcePath.Validate(repositoryRoot, relativePath);

            File.Move(temporary, path, overwrite: true);
            return newHash;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void EnsureExpectedHash(byte[] bytes, string expectedContentHash)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        if (!string.Equals(hash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The source file changed since it was read. No edit was applied; read and index it again.");
        }
    }
}
