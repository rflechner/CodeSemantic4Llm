using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Real YAML, Roslyn, Git and SQLite tests; every LLM call is replaced by a deterministic recorder.</summary>
public sealed class SharedSummaryCacheTests
{
    [Fact]
    public async Task Incoming_text_replaces_local_summaries_and_FTS_without_LLM_calls()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        File.WriteAllText(Path.Combine(repo.Root, "Other.cs"), "class Other {}\n");
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        Assert.Equal(2, await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token));
        Assert.DoesNotContain("private-key", File.ReadAllText(repo.SummaryPath));
        Assert.DoesNotContain("=> 42", File.ReadAllText(repo.SummaryPath));
        repo.UseFreshDatabase();
        repo.Generator.Fail = true;
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        var calls = repo.Generator.Calls;
        File.WriteAllText(repo.SummaryPath, File.ReadAllText(repo.SummaryPath).Replace("nebula", "quasar"));
        var changed = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        Assert.Equal(1, changed.AnalyzedFiles);
        Assert.Equal(1, changed.ReusedFiles);
        Assert.Equal(calls, repo.Generator.Calls);
        Assert.Equal(2, repo.Store.SearchCode(changed.Snapshot.Scope, "quasar", null, 10, repo.Token).Symbols.Count);
        Assert.Single(repo.Store.SearchCode(changed.Snapshot.Scope, "nebula", null, 10, repo.Token).Symbols);
        var unchanged = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        Assert.Equal(2, unchanged.ReusedFiles);
        Assert.Equal(0, await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("model")]
    [InlineData("language")]
    [InlineData("prompt")]
    [InlineData("systemPrompt")]
    [InlineData("deleted")]
    [InlineData("malformed")]
    public async Task Incompatible_or_removed_cache_regenerates_descriptions(string change)
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        var calls = repo.Generator.Calls;
        switch (change)
        {
            case "source": File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "// changed\n"); break;
            case "model": repo.WriteConfiguration(model: "new-model"); break;
            case "language": repo.WriteConfiguration(language: "fr"); break;
            case "prompt": repo.Generator.CacheVersion = "prompt-v2"; break;
            case "systemPrompt": repo.WriteConfiguration(systemPrompt: "Describe side effects in {lang}."); break;
            case "deleted": File.Delete(repo.SummaryPath); break;
            case "malformed": File.WriteAllText(repo.SummaryPath, "!"); break;
        }

        repo.Generator.Text = "Regenerated pulsar description.";
        var result = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        Assert.Equal(2, repo.Generator.Calls - calls);
        Assert.Empty(repo.Store.SearchCode(result.Snapshot.Scope, "nebula", null, 10, repo.Token).Symbols);
        Assert.Equal(2, repo.Store.SearchCode(result.Snapshot.Scope, "pulsar", null, 10, repo.Token).Symbols.Count);
    }

    [Fact]
    public async Task Custom_prompt_cache_is_portable_without_exporting_the_prompt_text()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        const string prompt = "CUSTOM_PROMPT_MUST_NOT_BE_EXPORTED. Explain in {lang}.";
        repo.WriteConfiguration(systemPrompt: prompt);
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);

        Assert.DoesNotContain(prompt, File.ReadAllText(repo.SummaryPath));
        repo.UseFreshDatabase();
        repo.Generator.Fail = true;
        var calls = repo.Generator.Calls;

        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);

        Assert.Equal(calls, repo.Generator.Calls);
    }

    [Fact]
    public async Task Deleted_and_renamed_sources_remove_stale_FTS_rows_and_isolate_branches()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        var main = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "Initial");
        await repo.GitAsync("switch", "-c", "feature");
        File.Move(Path.Combine(repo.Root, "Sample.cs"), Path.Combine(repo.Root, "Renamed.cs"));
        var feature = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        Assert.All(repo.Store.FindSymbols(feature.Snapshot.Scope, "Sample", 10, repo.Token).Symbols,
            symbol => Assert.Equal("Renamed.cs", symbol.Location.RelativePath));
        File.Delete(Path.Combine(repo.Root, "Renamed.cs"));
        feature = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        Assert.Empty(repo.Store.SearchCode(feature.Snapshot.Scope, "nebula", null, 10, repo.Token).Symbols);
        Assert.Equal(2, repo.Store.SearchCode(main.Snapshot.Scope, "nebula", null, 10, repo.Token).Symbols.Count);
    }

    [Fact]
    public async Task Export_preserves_differing_files_unless_explicitly_overwritten_and_rejects_stale_sources()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        var original = File.ReadAllText(repo.SummaryPath);
        File.WriteAllText(repo.SummaryPath, original.Replace("nebula", "local edit"));
        await Assert.ThrowsAsync<IOException>(() => repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token));
        Assert.Contains("local edit", File.ReadAllText(repo.SummaryPath));
        Assert.Equal(1, await repo.Exporter.ExecuteAsync(repo.Root, overwrite: true, cancellationToken: repo.Token));
        Assert.Equal(original, File.ReadAllText(repo.SummaryPath));
        File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "// edit\n");
        await Assert.ThrowsAsync<IOException>(() => repo.Exporter.ExecuteAsync(repo.Root, overwrite: true, cancellationToken: repo.Token));
        Assert.Equal(original, File.ReadAllText(repo.SummaryPath));
    }

    [Fact]
    public async Task A_cache_change_during_generation_rolls_back_the_complete_snapshot()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        var first = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "// invalidate\n");
        repo.Generator.OnGenerate = () => File.AppendAllText(repo.SummaryPath, "changed\n");
        await Assert.ThrowsAsync<IOException>(() => repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token));
        Assert.Equal(first.Snapshot.SnapshotId, repo.Store.GetSnapshot(first.Snapshot.Scope, repo.Token)!.SnapshotId);
    }

    [Fact]
    public void Authentication_does_not_invalidate_portable_profiles()
    {
        var one = new SemanticIndexingConfiguration("en", new LlmSummaryConfiguration(true, "http://localhost/v1", "model", "KEY_ONE", "secret-one"));
        var two = new SemanticIndexingConfiguration("en", new LlmSummaryConfiguration(true, "http://localhost/v1", "model", "KEY_TWO", "secret-two"));
        Assert.Equal(SummaryProfile.Create(one, "prompt"), SummaryProfile.Create(two, "prompt"));
        Assert.NotEqual(SummaryProfile.Create(one, "prompt"), SummaryProfile.Create(one, "new prompt"));
    }

    [Fact]
    public async Task Schema_five_migrates_without_losing_summaries_or_FTS()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        var first = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        using (var connection = new SqliteConnection($"Data Source={repo.Paths.GetDatabasePath(repo.Root)};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE indexed_files DROP COLUMN summary_cache_hash; PRAGMA user_version = 5;";
            command.ExecuteNonQuery();
        }

        var migrated = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        Assert.Equal(1, migrated.ReusedFiles);
        Assert.Equal(2, repo.Generator.Calls);
        Assert.Equal(2, repo.Store.SearchCode(first.Snapshot.Scope, "nebula", null, 10, repo.Token).Symbols.Count);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/outside")]
    public async Task Cache_paths_cannot_escape_the_repository(string path)
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => repo.Cache.ReadAsync(repo.Root, path, repo.Token));
    }

    [Fact]
    public async Task Malformed_metadata_is_a_cache_miss_and_never_controls_the_source_path()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        var markdown = File.ReadAllText(repo.SummaryPath);
        foreach (var invalid in new[]
        {
            markdown.Replace("schemaVersion: 1", "schemaVersion: 999"),
            markdown.Replace("source: Sample.cs", "source: ../secret.cs"),
            markdown.Replace("source: Sample.cs", "source: Wrong.cs"),
            markdown.Replace("schemaVersion: 1", "schemaVersion: 1\nschemaVersion: 1"),
            "<<<<<<< HEAD\n" + markdown,
            markdown.Replace("Calculates a nebula invoice.", "<<<<<<< HEAD\nCalculates a nebula invoice.\n=======\nOther text.\n>>>>>>> topic")
        })
        {
            File.WriteAllText(repo.SummaryPath, invalid);
            Assert.Null((await repo.Cache.ReadAsync(repo.Root, "Sample.cs", repo.Token)).File);
        }
    }

    [Fact]
    public async Task Symbolic_link_cache_directories_are_never_followed()
    {
        SymbolicLinks.SkipWhenUnsupported();
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        var outside = Path.Combine(repo.Workspace, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(repo.Root, ".codesemantic4llm"), outside);
        await Assert.ThrowsAsync<IOException>(() => repo.Cache.ReadAsync(repo.Root, "Sample.cs", repo.Token));
        await Assert.ThrowsAsync<IOException>(() => repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }
}
