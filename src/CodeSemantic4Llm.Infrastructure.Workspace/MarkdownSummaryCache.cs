using System.Text;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Reads and atomically exports derived Markdown inside a fixed, link-free repository directory.</summary>
public sealed class MarkdownSummaryCache(ISharedSummaryCodec codec) : ISharedSummaryCache
{
    public const string DirectoryName = ".codesemantic4llm/summaries";
    private const int MaximumBytes = 4 * 1024 * 1024;

    public async Task<SharedSummaryRead> ReadAsync(string root, string sourcePath, CancellationToken cancellationToken = default)
    {
        var relativePath = RelativePath(sourcePath);
        var path = RepositorySourcePath.Validate(root, relativePath, allowMissing: true);
        if (!File.Exists(path))
        {
            return new("", null);
        }

        // Bound the read itself, including files that grow after the initial filesystem check.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaximumBytes)
        {
            throw new IOException($"Shared summary '{relativePath}' exceeds 4 MiB.");
        }

        var bytes = new byte[checked((int)stream.Length + 1)];
        var count = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken);
        if (count == bytes.Length)
        {
            throw new IOException($"Shared summary '{relativePath}' changed while being read. Retry indexing.");
        }

        RepositorySourcePath.Validate(root, relativePath);
        var text = Encoding.UTF8.GetString(bytes, 0, count).Replace("\r\n", "\n", StringComparison.Ordinal);
        var digest = SummaryProfile.Hash(text);
        try
        {
            var document = codec.Decode(text);
            return new(digest, document.Source.RelativePath == sourcePath ? document : null);
        }
        catch (FormatException)
        {
            // Invalid/unmerged documents are cache misses, never authority for indexed declarations.
            return new(digest, null);
        }
    }

    public async Task<bool> WriteAsync(
        string root, SharedSummaryFile file, bool overwrite, CancellationToken cancellationToken = default)
    {
        var relativePath = RelativePath(file.Source.RelativePath);
        var destination = RepositorySourcePath.Validate(root, relativePath, allowMissing: true);
        var content = codec.Encode(file);
        if (Encoding.UTF8.GetByteCount(content) > MaximumBytes)
        {
            throw new IOException("Shared summary exceeds the 4 MiB byte limit.");
        }

        var previous = await ReadAsync(root, file.Source.RelativePath, cancellationToken);
        if (previous.Digest == SummaryProfile.Hash(content))
        {
            return false;
        }

        if (previous.Digest.Length > 0 && !overwrite)
        {
            throw new IOException($"Shared summary already differs: '{relativePath}'. Review it or explicitly use --overwrite.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        RepositorySourcePath.Validate(root, relativePath, allowMissing: true);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await output.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
            }

            var observed = await ReadAsync(root, file.Source.RelativePath, cancellationToken);
            if (observed.Digest != previous.Digest)
            {
                throw new IOException($"Shared summary changed during export: '{relativePath}'. Retry after reviewing it.");
            }

            RepositorySourcePath.Validate(root, relativePath, allowMissing: true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite);
            return true;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static string RelativePath(string sourcePath) => DirectoryName + "/" + sourcePath + ".md";
}
