using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpSymbolSourceExtractorTests
{
    [Fact]
    public async Task Extracts_whole_class_and_method_including_xml_documentation()
    {
        const string source = """
            namespace Demo;
            /// <summary>Performs billing operations.</summary>
            public class Billing
            {
                /// <summary>Computes a total.</summary>
                /// <param name="amount">Base amount.</param>
                public int Calculate(int amount) => amount * 2;

                public int Calculate(int amount, int multiplier) => amount * multiplier;
            }
            """;

        var document = new SourceDocument("Billing.cs", source);
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(document, TestContext.Current.CancellationToken);
        var snippets = new CSharpSymbolSourceExtractor().Extract(document, symbols, TestContext.Current.CancellationToken);

        var type = Assert.Single(symbols, symbol => symbol.Kind == "class");
        Assert.Contains("Performs billing operations", snippets[type.Location]);
        Assert.Contains("amount * multiplier", snippets[type.Location]);

        var overloads = symbols.Where(symbol => symbol.Kind == "method").OrderBy(s => s.Location.Line).ToArray();
        Assert.Equal(2, overloads.Length);
        Assert.Contains("Computes a total", snippets[overloads[0].Location]);
        Assert.Contains("<param name=", snippets[overloads[0].Location]);
        Assert.DoesNotContain("multiplier", snippets[overloads[0].Location]);
        Assert.Contains("amount * multiplier", snippets[overloads[1].Location]);
        Assert.DoesNotContain("Computes a total", snippets[overloads[1].Location]);
    }
}
