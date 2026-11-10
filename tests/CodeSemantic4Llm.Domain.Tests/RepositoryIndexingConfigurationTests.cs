using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Source;
using Xunit;

namespace CodeSemantic4Llm.Domain.Tests;

public sealed class RepositoryIndexingConfigurationTests
{
    [Fact]
    public void Mcp_paths_default_to_both()
    {
        Assert.Equal(McpPathFormat.Both, new RepositoryIndexingConfiguration(["dotnet-csharp"]).McpPaths);
    }

    [Fact]
    public void Undefined_mcp_path_format_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RepositoryIndexingConfiguration(["dotnet-csharp"], mcpPaths: (McpPathFormat)99));
    }

    [Fact]
    public void Watch_debounce_defaults_to_15_seconds()
    {
        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"]);
        Assert.Equal(15, configuration.Watch.DebounceSeconds);
    }

    [Fact]
    public void Repository_watch_debounce_is_configurable()
    {
        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"],
            watch: new RepositoryWatchConfiguration(6));
        Assert.Equal(6, configuration.Watch.DebounceSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Invalid_watch_debounce_is_rejected(int seconds)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RepositoryWatchConfiguration(seconds));
        Assert.Contains("watch.debounceSeconds", error.Message);
    }

    [Fact]
    public void Project_names_are_optional_display_labels()
    {
        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"]);

        Assert.Null(configuration.ProjectName);
    }

    [Fact]
    public void Project_names_are_trimmed_without_changing_case_or_unicode()
    {
        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"], projectName: "  Équipe: Paiements #1 [API]  ");

        Assert.Equal("Équipe: Paiements #1 [API]", configuration.ProjectName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("First\nSecond")]
    [InlineData("\tProject")]
    [InlineData("Project\u001b[31m")]
    [InlineData("First\u2028Second")]
    [InlineData("First\u2029Second")]
    public void Empty_or_multiline_project_names_are_rejected(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new RepositoryIndexingConfiguration(["dotnet-csharp"], projectName: name));
    }

    [Fact]
    public void Omitted_extension_overrides_leave_defaults_to_the_analyzers()
    {
        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"]);

        Assert.Empty(configuration.FileExtensions);
    }

    [Fact]
    public void Extension_overrides_use_canonical_analyzer_ids_and_copy_the_input()
    {
        var input = new Dictionary<string, SourceFileExtensions>
        {
            [" DOTNET-CSHARP "] = new([".csharp"])
        };

        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"], fileExtensions: input);
        input.Clear();

        Assert.Equal("dotnet-csharp", Assert.Single(configuration.FileExtensions).Key);
        Assert.Equal([".csharp"], configuration.FileExtensions["dotnet-csharp"]);
    }

    [Fact]
    public void Overrides_for_disabled_analyzers_are_rejected()
    {
        var overrides = new Dictionary<string, SourceFileExtensions>
        {
            ["typescript"] = new([".ts"])
        };

        Assert.Throws<ArgumentException>(() =>
            new RepositoryIndexingConfiguration(["dotnet-csharp"], fileExtensions: overrides));
    }

    [Fact]
    public void Duplicate_canonical_override_ids_are_rejected()
    {
        var overrides = new Dictionary<string, SourceFileExtensions>
        {
            ["dotnet-csharp"] = new([".cs"]),
            [" DOTNET-CSHARP "] = new([".csharp"])
        };

        Assert.Throws<ArgumentException>(() =>
            new RepositoryIndexingConfiguration(["dotnet-csharp"], fileExtensions: overrides));
    }
}
