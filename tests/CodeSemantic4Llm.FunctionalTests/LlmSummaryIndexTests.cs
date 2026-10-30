using System.Diagnostics;
using System.Text;
using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Persistence.Sqlite;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Exercises real Git, YAML, Roslyn and SQLite with a fake LLM; no network requests are made.</summary>
public sealed class LlmSummaryIndexTests : IDisposable
{
    private const string SourceSecret = "SOURCE_CONTENT_MUST_NOT_BE_PERSISTED_9e712";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "CodeSemantic4Llm.LlmTests", Guid.NewGuid().ToString("N"));
    private readonly GitRepositoryStateProvider _git = new();
    private readonly RecordingGenerator _generator = new();

    private string Root => Path.Combine(_workspace, "source");
    private string ConfigPath => Path.Combine(Root, "codesemantic4llm.yaml");
    private IndexStoragePaths Paths => new(Path.Combine(_workspace, "cache"));

    [Fact]
    public async Task Enabled_summaries_are_persisted_reused_regenerated_and_disabled_per_repository()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, model: "model-one");
        var indexer = CreateIndexer();

        var first = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, first.AnalyzedFiles);
        Assert.Equal(2, _generator.Requests.Count);
        Assert.All(_generator.Requests, request => Assert.Equal("fr", request.Language));
        Assert.Contains(_generator.Requests, request => request.Symbol.Kind == "method" &&
            request.Source.Contains("<summary>Computes a total.</summary>", StringComparison.Ordinal));
        Assert.Contains(_generator.Requests, request => request.Symbol.Kind == "class" &&
            request.Source.Contains("return value + 1", StringComparison.Ordinal) &&
            request.Source.Contains("<summary>Billing operations.</summary>", StringComparison.Ordinal));

        var store = new SqliteIndexStore(Paths);
        var scope = (await _git.GetStateAsync(Root, TestContext.Current.CancellationToken)).Scope;
        var savedMethod = Assert.Single(store.FindSymbols(scope, "Calculate", 10).Symbols);
        Assert.Equal(10, savedMethod.Summary!.Split('\n').Length);
        Assert.NotNull(Assert.Single(store.FindSymbols(scope, "Billing", 10).Symbols).Summary);

        var second = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, second.ReusedFiles);
        Assert.Equal(2, _generator.Requests.Count);
        Assert.Equal(savedMethod.Summary, Assert.Single(store.FindSymbols(scope, "Calculate", 10).Symbols).Summary);

        WriteYaml(enabled: true, model: "model-two");
        var third = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, third.ReusedFiles);
        Assert.Equal(4, _generator.Requests.Count);

        WriteYaml(enabled: false, model: "model-two");
        await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, _generator.Requests.Count);
        Assert.Null(Assert.Single(store.FindSymbols(scope, "Calculate", 10).Symbols).Summary);

        var bytes = File.ReadAllBytes(Paths.GetDatabasePath(Root));
        Assert.DoesNotContain(SourceSecret, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Failed_generation_preserves_last_published_snapshot()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, model: "model-one");
        var indexer = CreateIndexer();
        var first = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        var store = new SqliteIndexStore(Paths);

        File.AppendAllText(Path.Combine(Root, "Billing.cs"), "\npublic class NewType { public void Run() {} }\n");
        _generator.Fail = true;

        await Assert.ThrowsAsync<IOException>(() =>
            indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken));

        var saved = store.GetSnapshot(first.Snapshot.Scope);
        Assert.Equal(first.Snapshot.SnapshotId, saved!.SnapshotId);
        Assert.Empty(store.FindSymbols(first.Snapshot.Scope, "NewType", 10).Symbols);
        Assert.NotNull(Assert.Single(store.FindSymbols(first.Snapshot.Scope, "Calculate", 10).Symbols).Summary);
    }

    private IndexRepository CreateIndexer() => new(
        _git,
        new YamlRepositoryConfigurationLoader(),
        new FileSystemRepositoryFileEnumerator(),
        new RepositorySourceReader(),
        new SqliteIndexStore(Paths),
        [new CSharpSourceAnalyzer()],
        [new CSharpReferenceAnalyzer()],
        [new CSharpSymbolSourceExtractor()],
        _generator);

    private void WriteYaml(bool enabled, string model) => File.WriteAllText(ConfigPath,
        "version: 1\nanalyzers: [dotnet-csharp]\nsemanticIndexing:\n  language: fr\n" +
        "  llm:\n    enabled: " + (enabled ? "true" : "false") +
        "\n    baseUrl: http://127.0.0.1:1234/v1\n    model: " + model + "\n");

    private async Task CreateCheckoutAsync()
    {
        Directory.CreateDirectory(Root);
        await GitAsync("init", "--initial-branch=main", "--template=");
        File.WriteAllText(Path.Combine(Root, "Billing.cs"), """
            namespace Demo;
            /// <summary>Billing operations.</summary>
            public class Billing
            {
                /// <summary>Computes a total.</summary>
                public int Calculate(int value)
                {
                    // SOURCE_CONTENT_MUST_NOT_BE_PERSISTED_9e712
                    return value + 1;
                }
            }
            """);
        WriteYaml(enabled: false, model: "model-one");
        await GitAsync("add", ".");
        await GitAsync("commit", "-m", "Create test repository");
    }

    private async Task GitAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in new[] { "-C", Root,
            "-c", "user.name=Test", "-c", "user.email=test@example.invalid",
            "-c", "commit.gpgsign=false",
            "-c", "core.fsmonitor=false",
            "-c", $"core.hooksPath={Path.Combine(_workspace, "no-hooks")}" }.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await stdout;
        Assert.True(process.ExitCode == 0, await stderr);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private sealed class RecordingGenerator : ISymbolSummaryGenerator
    {
        public List<(SymbolDefinition Symbol, string Source, string Language)> Requests { get; } = [];
        public bool Fail { get; set; }

        public Task<string> SummarizeAsync(
            SymbolDefinition symbol, string declarationSource, SemanticIndexingConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Fail)
            {
                throw new IOException("Simulated LLM failure");
            }

            Requests.Add((symbol, declarationSource, configuration.Language));
            return Task.FromResult(string.Join("\n", Enumerable.Range(1, 12).Select(n => $"Summary line {n}")));
        }
    }
}
