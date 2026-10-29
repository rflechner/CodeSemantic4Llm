using CodeSemantic4Llm.Domain.Configuration;
using Xunit;

namespace CodeSemantic4Llm.Domain.Tests;

public sealed class SemanticIndexingConfigurationTests
{
    [Fact]
    public void Language_defaults_to_english()
    {
        var options = new SemanticIndexingConfiguration();

        Assert.Equal("en", options.Language);
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("FR-ch", "fr-CH")]
    [InlineData("EN-us", "en-US")]
    [InlineData("ZH-hANT-tw", "zh-Hant-TW")]
    public void Language_tags_are_normalized(string configuredLanguage, string expected)
    {
        var options = new SemanticIndexingConfiguration(configuredLanguage);

        Assert.Equal(expected, options.Language);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("english")]
    [InlineData("fr_CH")]
    [InlineData("fr-")]
    [InlineData("a")]
    [InlineData("en-US-extra")]
    public void Invalid_language_tags_are_rejected(string language)
    {
        Assert.Throws<ArgumentException>(() => new SemanticIndexingConfiguration(language));
    }

    [Fact]
    public void Repository_configuration_defaults_to_english()
    {
        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"]);

        Assert.Equal("en", configuration.SemanticIndexing.Language);
    }

    [Fact]
    public void Repository_configuration_uses_requested_language()
    {
        var configuration = new RepositoryIndexingConfiguration(
            ["dotnet-csharp"],
            semanticIndexing: new SemanticIndexingConfiguration("fr-CH"));

        Assert.Equal("fr-CH", configuration.SemanticIndexing.Language);
    }
}
