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
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
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

        WriteYaml(enabled: true, forceGeneration: true, model: "model-two");
        var third = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, third.ReusedFiles);
        Assert.Equal(4, _generator.Requests.Count);

        WriteYaml(enabled: false, model: "model-two");
        await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, _generator.Requests.Count);
        Assert.Equal("Computes a total.", Assert.Single(store.FindSymbols(scope, "Calculate", 10).Symbols).Summary);

        var bytes = File.ReadAllBytes(Paths.GetDatabasePath(scope.RootPath));
        Assert.DoesNotContain(SourceSecret, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Rotating_an_inline_key_reuses_summaries_without_persisting_credentials()
    {
        await CreateCheckoutAsync();
        const string originalKey = "inline-key-original-57a1";
        const string rotatedKey = "inline-key-rotated-57a1";
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one", apiKey: originalKey);
        var indexer = CreateIndexer();
        var first = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);

        WriteYaml(enabled: true, forceGeneration: true, model: "model-one", apiKey: rotatedKey);
        var second = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, second.ReusedFiles);
        Assert.Equal(0, second.AnalyzedFiles);
        Assert.Equal(2, _generator.Requests.Count);
        var bytes = File.ReadAllBytes(Paths.GetDatabasePath(first.Snapshot.Scope.RootPath));
        var storedText = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(originalKey, storedText);
        Assert.DoesNotContain(rotatedKey, storedText);
    }

    [Fact]
    public async Task Reasoning_effort_and_completion_budget_changes_regenerate_summaries()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
        var indexer = CreateIndexer();
        await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, _generator.Requests.Count);

        WriteYaml(enabled: true, forceGeneration: true, model: "model-one", reasoningEffort: "low");
        var changedEffort = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, changedEffort.ReusedFiles);
        Assert.Equal(4, _generator.Requests.Count);
        Assert.Equal("low", _generator.LastSettings!.ReasoningEffort);

        WriteYaml(enabled: true, forceGeneration: true, model: "model-one", reasoningEffort: "low", maxCompletionTokens: 4096);
        var changedBudget = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, changedBudget.ReusedFiles);
        Assert.Equal(6, _generator.Requests.Count);
        Assert.Equal(4096, _generator.LastSettings!.MaxCompletionTokens);

        var unchanged = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, unchanged.ReusedFiles);
        Assert.Equal(6, _generator.Requests.Count);

        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
        var removedSettings = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(0, removedSettings.ReusedFiles);
        Assert.Equal(8, _generator.Requests.Count);
        Assert.Null(_generator.LastSettings!.ReasoningEffort);
    }

    [Fact]
    public async Task Changing_or_removing_the_system_prompt_regenerates_summaries()
    {
        await CreateCheckoutAsync();
        var indexer = CreateIndexer();
        var expectedRequests = 0;

        foreach (var prompt in new[] { null, "Explain in {lang}.", "Describe side effects in {lang}.", null })
        {
            WriteYaml(enabled: true, forceGeneration: true, model: "model-one", systemPrompt: prompt);
            var changed = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
            expectedRequests += 2;

            Assert.Equal(0, changed.ReusedFiles);
            Assert.Equal(expectedRequests, _generator.Requests.Count);
            Assert.Equal(prompt, _generator.LastSettings!.SystemPrompt);

            var unchanged = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(1, unchanged.ReusedFiles);
            Assert.Equal(expectedRequests, _generator.Requests.Count);
        }
    }

    [Fact]
    public async Task Failed_generation_preserves_last_published_snapshot()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
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

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public async Task Empty_summaries_allow_publication_and_reuse_without_losing_source_documentation(string emptySummary)
    {
        await CreateCheckoutAsync();
        File.WriteAllText(Path.Combine(Root, "Undocumented.cs"), "public class Undocumented {}");
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
        _generator.SummaryOverride = emptySummary;
        var indexer = CreateIndexer();

        var first = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, first.AnalyzedFiles);
        Assert.Equal(3, _generator.Requests.Count);
        var store = new SqliteIndexStore(Paths);
        var scope = first.Snapshot.Scope;
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(first.Snapshot.SnapshotId, store.GetSnapshot(scope, cancellationToken)!.SnapshotId);
        Assert.Equal("Billing operations.", Assert.Single(store.FindSymbols(scope, "Billing", 10, cancellationToken).Symbols).Summary);
        Assert.Equal("Computes a total.", Assert.Single(store.FindSymbols(scope, "Calculate", 10, cancellationToken).Symbols).Summary);
        Assert.Null(Assert.Single(store.FindSymbols(scope, "Undocumented", 10, cancellationToken).Symbols).Summary);

        var second = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, second.ReusedFiles);
        Assert.Equal(3, _generator.Requests.Count);
        Assert.Null(Assert.Single(store.FindSymbols(scope, "Undocumented", 10, cancellationToken).Symbols).Summary);
        Assert.Equal("Computes a total.", Assert.Single(store.FindSymbols(scope, "Calculate", 10, cancellationToken).Symbols).Summary);
    }


    [Fact]
    public async Task Failed_summary_files_do_not_publish_but_successful_checkpoints_are_reused()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
        ConfigureParallelLlm();
        File.WriteAllText(Path.Combine(Root, "One.cs"), "public class One {}");
        File.WriteAllText(Path.Combine(Root, "Two.cs"), "public class Two {}");
        File.WriteAllText(Path.Combine(Root, "Three.cs"), "public class Three {}");
        _generator.FailingPaths.Add("Two.cs");
        var indexer = CreateIndexer();

        var error = await Assert.ThrowsAsync<IOException>(() =>
            indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("LLM summaries failed for 1", error.Message);
        _generator.FailingPaths.Clear();

        var completed = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, completed.ReusedFiles);
        Assert.Equal(1, completed.AnalyzedFiles);
        Assert.NotNull(Assert.Single(
            new SqliteIndexStore(Paths).FindSymbols(completed.Snapshot.Scope, "Two", 10).Symbols).Summary);
    }

    [Fact]
    public async Task Six_consecutive_failed_files_stop_without_replacing_a_published_snapshot()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
        ConfigureParallelLlm();
        var indexer = CreateIndexer();
        var previous = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);

        for (var i = 1; i <= 6; i++)
        {
            var path = $"Fail{i:00}.cs";
            File.WriteAllText(Path.Combine(Root, path), $"public class Fail{i:00} {{}}");
            _generator.FailingPaths.Add(path);
        }

        var failure = await Assert.ThrowsAsync<IOException>(() =>
            indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("6 consecutive files", failure.Message);
        var saved = new SqliteIndexStore(Paths).GetSnapshot(previous.Snapshot.Scope);
        Assert.Equal(previous.Snapshot.SnapshotId, saved!.SnapshotId);
    }

    [Fact]
    public async Task A_successful_file_resets_the_consecutive_llm_failure_counter()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
        ConfigureParallelLlm();

        for (var i = 1; i <= 11; i++)
        {
            var path = $"File{i:00}.cs";
            File.WriteAllText(Path.Combine(Root, path), $"public class File{i:00} {{}}");

            if (i != 6)
            {
                _generator.FailingPaths.Add(path);
            }
        }

        var failure = await Assert.ThrowsAsync<IOException>(() =>
            CreateIndexer().ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken));

        // Five failures, one success, five failures: never reaches the six-file cutoff.
        Assert.Contains("LLM summaries failed for 10", failure.Message);
    }

    [Fact]
    public async Task Switching_from_legacy_url_to_multiple_endpoints_does_not_regenerate_unchanged_files()
    {
        await CreateCheckoutAsync();
        WriteYaml(enabled: true, forceGeneration: true, model: "model-one");
        var indexer = CreateIndexer();
        await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, _generator.Requests.Count);

        ConfigureParallelLlm();
        var second = await indexer.ExecuteAsync(Root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, second.ReusedFiles);
        Assert.Equal(2, _generator.Requests.Count);
    }

    private void ConfigureParallelLlm()
    {
        var yaml = File.ReadAllText(ConfigPath).Replace(
            "    baseUrl: http://127.0.0.1:1234/v1\n",
            "    endpoints:\n" +
            "      - baseUrl: http://127.0.0.1:1234/v1\n" +
            "      - baseUrl: http://127.0.0.1:1235/v1\n",
            StringComparison.Ordinal);
        File.WriteAllText(ConfigPath, yaml);
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

    private void WriteYaml(
        bool enabled, string model, string? apiKey = null, string? reasoningEffort = null,
        int? maxCompletionTokens = null, bool forceGeneration = false, string? systemPrompt = null) => File.WriteAllText(ConfigPath,
        "version: 1\nanalyzers: [dotnet-csharp]\nsemanticIndexing:\n  language: fr\n" +
        "  llm:\n    enabled: " + (enabled ? "true" : "false") +
        "\n    forceGeneration: " + (forceGeneration ? "true" : "false") +
        "\n    baseUrl: http://127.0.0.1:1234/v1\n    model: " + model + "\n" +
        (apiKey is null ? "" : "    apiKey: " + apiKey + "\n") +
        (reasoningEffort is null ? "" : "    reasoningEffort: " + reasoningEffort + "\n") +
        (systemPrompt is null ? "" : "    systemPrompt: " + systemPrompt + "\n") +
        (maxCompletionTokens is null ? "" : "    maxCompletionTokens: " + maxCompletionTokens + "\n"));

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
        TemporaryDirectory.TryRemove(_workspace);
    }

    private sealed class RecordingGenerator : ISymbolSummaryGenerator
    {
        public List<(SymbolDefinition Symbol, string Source, string Language)> Requests { get; } = [];
        public bool Fail { get; set; }
        public HashSet<string> FailingPaths { get; } = [];
        public string? SummaryOverride { get; set; }
        public LlmSummaryConfiguration? LastSettings { get; private set; }

        public Task<string> SummarizeAsync(
            SymbolDefinition symbol, string declarationSource, SemanticIndexingConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Fail || FailingPaths.Contains(symbol.Location.RelativePath))
            {
                throw new IOException("Simulated LLM failure");
            }

            lock (Requests)
            {
                Requests.Add((symbol, declarationSource, configuration.Language));
                LastSettings = configuration.Llm;
            }
            return Task.FromResult(SummaryOverride ?? string.Join("\n", Enumerable.Range(1, 12).Select(n => $"Summary line {n}")));
        }
    }
}
