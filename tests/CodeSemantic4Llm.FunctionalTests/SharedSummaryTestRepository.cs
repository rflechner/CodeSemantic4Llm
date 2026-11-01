using System.Diagnostics;
using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Persistence.Sqlite;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

internal sealed class SharedSummaryTestRepository : IDisposable
{
    public string Workspace { get; } = Path.Combine(Path.GetTempPath(), "CodeSemantic4Llm.Shared", Guid.NewGuid().ToString("N"));
    public string Root => Path.Combine(Workspace, "checkout");
    public string SummaryPath => Path.Combine(Root, MarkdownSummaryCache.RelativePath("Sample.cs"));
    public RecordingSummaryGenerator Generator { get; } = new();
    public MarkdownSummaryCodec Codec { get; } = new();
    public MarkdownSummaryCache Cache => new(Codec);
    public GitRepositoryStateProvider State { get; } = new();
    public IndexStoragePaths Paths { get; private set; }
    public SqliteIndexStore Store => new(Paths);
    public IndexRepository Indexer => new(State, new YamlRepositoryConfigurationLoader(),
        new FileSystemRepositoryFileEnumerator(), new RepositorySourceReader(), Store, [new CSharpSourceAnalyzer()],
        summaryExtractors: [new CSharpSymbolSourceExtractor()], summaryGenerator: Generator, sharedSummaryCache: Cache);
    public ExportSharedSummaries Exporter => new(State, new YamlRepositoryConfigurationLoader(), new RepositorySourceReader(),
        new SqliteSummaryExportReader(Paths), Cache, Indexer);
    public CancellationToken Token => TestContext.Current.CancellationToken;

    public SharedSummaryTestRepository() => Paths = new(Path.Combine(Workspace, "cache-one"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Root);
        await GitAsync("init", "--initial-branch=main");
        await GitAsync("config", "user.name", "Summary Tests");
        await GitAsync("config", "user.email", "summaries@example.test");
        WriteConfiguration();
        File.WriteAllText(Path.Combine(Root, "Sample.cs"), "namespace Demo; public class Sample { public int Total() => 42; }\n");
    }

    public void UseFreshDatabase() => Paths = new(Path.Combine(Workspace, "cache-" + Guid.NewGuid().ToString("N")));

    public void WriteConfiguration(
        string model = "test-model", string language = "en", string apiKey = "private-key", string? systemPrompt = null) =>
        File.WriteAllText(Path.Combine(Root, "codesemantic4llm.yaml"), $$"""
            version: 1
            analyzers: [dotnet-csharp]
            sharedSummaries: true
            semanticIndexing:
              language: {{language}}
              llm:
                enabled: true
                baseUrl: http://localhost:1234/v1
                model: {{model}}
                apiKey: {{apiKey}}
                systemPrompt: {{System.Text.Json.JsonSerializer.Serialize(systemPrompt)}}
            """);

    public async Task<string> GitAsync(params string[] arguments)
    {
        var result = await RunGitAsync(arguments);
        Assert.True(result.Code == 0, result.Error);
        return result.Output;
    }

    public async Task<(int Code, string Output, string Error)> RunGitAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_EDITOR"] = "true";
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(Token);
        var error = process.StandardError.ReadToEndAsync(Token);
        await process.WaitForExitAsync(Token);
        return (process.ExitCode, await output, await error);
    }

    public void Dispose() => TemporaryDirectory.TryRemove(Workspace);

    internal sealed class RecordingSummaryGenerator : ISymbolSummaryGenerator
    {
        public int Calls { get; private set; }
        public string Text { get; set; } = "Calculates a nebula invoice.";
        public string CacheVersion { get; set; } = "prompt-v1";
        public bool Fail { get; set; }
        public bool Timeout { get; set; }
        public Action? OnGenerate { get; set; }

        public Task<string> SummarizeAsync(SymbolDefinition symbol, string declarationSource,
            SemanticIndexingConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            OnGenerate?.Invoke();
            if (Fail)
            {
                if (Timeout) throw new TaskCanceledException("The LLM provider timed out.");
                throw new IOException("Generation failed.");
            }
            return Task.FromResult(Text);
        }
    }
}
