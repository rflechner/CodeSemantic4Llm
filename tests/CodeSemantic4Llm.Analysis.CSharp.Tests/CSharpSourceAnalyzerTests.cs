using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpSourceAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_extracts_qualified_types_and_members()
    {
        const string source = """
            namespace Demo.Sample;

            public class Customer
            {
                public string Name { get; set; } = string.Empty;
                public void Rename(string name) { }
            }
            """;

        var analyzer = new CSharpSourceAnalyzer();
        var symbols = await analyzer.AnalyzeAsync(
            new SourceDocument("src/Customer.cs", source),
            TestContext.Current.CancellationToken);

        Assert.Contains(symbols, symbol => symbol.Kind == "class" && symbol.QualifiedName == "Demo.Sample.Customer");
        Assert.Contains(symbols, symbol => symbol.Kind == "property" && symbol.QualifiedName == "Demo.Sample.Customer.Name");
        Assert.Contains(symbols, symbol => symbol.Kind == "method" && symbol.QualifiedName == "Demo.Sample.Customer.Rename");
        Assert.All(symbols, symbol => Assert.Equal("src/Customer.cs", symbol.Location.RelativePath));
    }

    [Fact]
    public void Supports_only_csharp_files()
    {
        var analyzer = new CSharpSourceAnalyzer();

        Assert.True(analyzer.Supports("src/Program.CS"));
        Assert.False(analyzer.Supports("src/app.ts"));
    }
}
