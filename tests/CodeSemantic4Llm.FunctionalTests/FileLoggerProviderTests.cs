using System.Text.Json;
using CodeSemantic4Llm.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CodeSemantic4Llm.LogTests", Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(_root, "server.jsonl");

    [Fact]
    public void Appends_flushed_json_lines_with_correlation_and_exception_details()
    {
        using (var provider = new FileLoggerProvider(LogPath))
        {
            var logger = provider.CreateLogger("Test");
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["RequestId"] = "request\n7", ["ToolName"] = "get_file_diagnostics", ["source"] = "DO_NOT_LOG_SOURCE"
            });
            var exception = new InvalidOperationException("outer failure", new IOException("inner failure"));
            logger.LogError(exception, "A call failed.");

            // Reading before disposal verifies flushing and concurrent reader access.
            var line = Assert.Single(File.ReadAllLines(LogPath));
            using var entry = JsonDocument.Parse(line);
            Assert.Equal("request\n7", entry.RootElement.GetProperty("requestId").GetString());
            Assert.Equal("get_file_diagnostics", entry.RootElement.GetProperty("toolName").GetString());
            Assert.Contains("inner failure", entry.RootElement.GetProperty("exception").GetString());
            Assert.DoesNotContain("DO_NOT_LOG_SOURCE", line);
        }

        using (var restarted = new FileLoggerProvider(LogPath))
        {
            restarted.CreateLogger("Test").LogInformation("Next server run.");
        }

        Assert.Equal(2, File.ReadAllLines(LogPath).Length);
        Assert.NotEqual(0xEF, File.ReadAllBytes(LogPath)[0]);
    }

    [Fact]
    public async Task Concurrent_writers_keep_complete_log_records()
    {
        using var provider = new FileLoggerProvider(LogPath);
        var logger = provider.CreateLogger("Test");
        await Parallel.ForEachAsync(Enumerable.Range(0, 100), TestContext.Current.CancellationToken, (number, _) =>
        {
            logger.LogInformation("Record {Number}", number);
            return ValueTask.CompletedTask;
        });

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(100, lines.Length);
        Assert.All(lines, line =>
        {
            using var entry = JsonDocument.Parse(line);
            Assert.Equal("Information", entry.RootElement.GetProperty("level").GetString());
        });
    }

    [Fact]
    public void Rotation_retains_only_the_configured_archives_and_the_newest_record()
    {
        using var provider = new FileLoggerProvider(LogPath, maximumBytes: 512, archiveCount: 2);
        var logger = provider.CreateLogger("Test");

        for (var number = 0; number < 12; number++)
        {
            logger.LogInformation("Record {Number}: {Padding}", number, new string('x', 150));
        }

        Assert.Equal(3, Directory.GetFiles(_root).Length);
        Assert.True(File.Exists(LogPath + ".1"));
        Assert.True(File.Exists(LogPath + ".2"));
        Assert.Contains("Record 11", File.ReadAllText(LogPath));
        Assert.DoesNotContain("Record 0:", string.Join("", Directory.GetFiles(_root).Select(File.ReadAllText)));
    }

    [Fact]
    public void File_logging_failures_do_not_break_code_operations()
    {
        using var provider = new FileLoggerProvider(LogPath, maximumBytes: 1);
        var logger = provider.CreateLogger("Test");
        logger.LogInformation("First record.");
        Directory.CreateDirectory(LogPath + ".1");

        var failure = Record.Exception(() =>
        {
            logger.LogError("Rotation cannot replace a directory.");
            logger.LogInformation("Logging is now disabled.");
        });

        Assert.Null(failure);
        Assert.Single(File.ReadAllLines(LogPath));
    }

    [Fact]
    public void Debug_and_trace_payloads_are_not_written()
    {
        using var provider = new FileLoggerProvider(LogPath);
        var logger = provider.CreateLogger("Test");
        logger.LogDebug("DO_NOT_LOG_DEBUG_SOURCE");
        logger.LogTrace("DO_NOT_LOG_TRACE_SOURCE");

        Assert.Empty(File.ReadAllText(LogPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
