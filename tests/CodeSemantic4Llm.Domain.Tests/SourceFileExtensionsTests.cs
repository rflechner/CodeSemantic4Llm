using CodeSemantic4Llm.Domain.Source;
using Xunit;

namespace CodeSemantic4Llm.Domain.Tests;

public sealed class SourceFileExtensionsTests
{
    [Fact]
    public void Extensions_are_normalized_deduplicated_and_detached_from_the_input()
    {
        var input = new List<string> { " .CS ", ".csharp", ".cs" };
        var extensions = new SourceFileExtensions(input);

        input.Clear();

        Assert.Equal([".cs", ".csharp"], extensions);
    }

    [Theory]
    [InlineData("Program.cs", true)]
    [InlineData("src/features/customers/Customer.CS", true)]
    [InlineData("src/Customer.g.cs", true)]
    [InlineData("src/Customer.csharp", true)]
    [InlineData("src/Customer.cs.bak", false)]
    [InlineData("src.cs/README", false)]
    [InlineData("src/app.ts", false)]
    public void Matching_uses_only_the_final_extension(string path, bool expected)
    {
        var extensions = new SourceFileExtensions([".cs", ".csharp"]);

        Assert.Equal(expected, extensions.Matches(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("cs")]
    [InlineData(".")]
    [InlineData("*.cs")]
    [InlineData(".g.cs")]
    [InlineData("../cs")]
    [InlineData(".cs/other")]
    [InlineData(".cs\\other")]
    [InlineData(".c s")]
    [InlineData(".cs?")]
    public void Invalid_extensions_are_rejected(string? extension)
    {
        Assert.Throws<ArgumentException>(() => new SourceFileExtensions([extension!]));
    }

    [Fact]
    public void An_empty_selection_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SourceFileExtensions([]));
    }
}
