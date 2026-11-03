using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Ensures CLI options do not mask repository-local debounce settings.</summary>
public sealed class WatchRepositorySettingsTests
{
    [Theory]
    [InlineData(null, 6, 6)]
    [InlineData(2, 6, 2)]
    [InlineData(null, 15, 15)]
    public void Debounce_respects_yaml_unless_the_cli_overrides_it(
        int? cliValue, int yamlValue, int expectedSeconds)
    {
        var settings = new WatchRepositorySettings
        {
            RepositoryRoot = ".",
            DebounceSeconds = cliValue
        };

        Assert.True(settings.Validate().Successful);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds),
            settings.ResolveDebounceInterval(new RepositoryWatchConfiguration(yamlValue)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Invalid_explicit_cli_values_are_rejected(int seconds)
    {
        var settings = new WatchRepositorySettings
        {
            RepositoryRoot = ".",
            DebounceSeconds = seconds
        };

        Assert.False(settings.Validate().Successful);
    }
}
