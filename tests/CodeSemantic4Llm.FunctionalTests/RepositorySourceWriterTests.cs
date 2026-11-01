using System.Security.Cryptography;
using System.Text;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

public sealed class RepositorySourceWriterTests
{
    [Fact]
    public async Task Replaces_source_atomically_and_keeps_the_utf8_bom()
    {
        var root = MakeRoot();

        try
        {
            var path = Path.Combine(root, "Worker.cs");
            byte[] existing = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("class Worker {}")];
            await File.WriteAllBytesAsync(path, existing, TestContext.Current.CancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(existing));
            var writer = new RepositorySourceWriter();

            var newHash = await writer.ReplaceAsync(root, "Worker.cs", hash,
                "class Worker { void Run() {} }", TestContext.Current.CancellationToken);

            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.Equal("class Worker { void Run() {} }", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), newHash);
            Assert.Single(Directory.EnumerateFiles(root));
        }
        finally
        {
            TemporaryDirectory.TryRemove(root);
        }
    }

    [Fact]
    public async Task Refuses_a_stale_hash_without_changing_the_file()
    {
        var root = MakeRoot();

        try
        {
            var path = Path.Combine(root, "Worker.cs");
            await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
            var writer = new RepositorySourceWriter();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                writer.ReplaceAsync(root, "Worker.cs", new string('0', 64),
                    "replacement", TestContext.Current.CancellationToken));
            Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Single(Directory.EnumerateFiles(root));
        }
        finally
        {
            TemporaryDirectory.TryRemove(root);
        }
    }

    [Fact]
    public async Task Refuses_parent_traversal_and_symlinked_files()
    {
        SymbolicLinks.SkipWhenUnsupported();

        var root = MakeRoot();
        var outside = MakeRoot();

        try
        {
            var target = Path.Combine(outside, "outside.cs");
            await File.WriteAllTextAsync(target, "outside", TestContext.Current.CancellationToken);
            File.CreateSymbolicLink(Path.Combine(root, "Worker.cs"), target);
            var writer = new RepositorySourceWriter();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("outside")));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                writer.ReplaceAsync(root, "../outside.cs", hash, "bad", TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<IOException>(() =>
                writer.ReplaceAsync(root, "Worker.cs", hash, "bad", TestContext.Current.CancellationToken));
            Assert.Equal("outside", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
        }
        finally
        {
            TemporaryDirectory.TryRemove(root);
            TemporaryDirectory.TryRemove(outside);
        }
    }

    private static string MakeRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodeSemantic4Llm.WriteTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
