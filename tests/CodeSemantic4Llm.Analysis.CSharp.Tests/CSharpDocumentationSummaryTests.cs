using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpDocumentationSummaryTests
{
    [Theory]
    [InlineData("class Catalog {}")]
    [InlineData("interface Catalog {}")]
    [InlineData("struct Catalog {}")]
    [InlineData("record Catalog;")]
    [InlineData("enum Catalog { First }")]
    [InlineData("delegate void Catalog();")]
    public async Task Every_type_kind_preserves_its_own_summary(string declaration)
    {
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("Catalog.cs", "/// <summary>Catalog documentation.</summary>\npublic " + declaration),
            TestContext.Current.CancellationToken);
        Assert.Equal("Catalog documentation.", Assert.Single(symbols, symbol => symbol.Name == "Catalog").Summary);
    }

    [Fact]
    public async Task Summaries_belong_to_the_exact_declaration_including_overloads_and_nested_types()
    {
        const string source = """
            /// <summary>Outer type.</summary>
            public class Worker
            {
                /// <summary>Creates a worker.</summary>
                public Worker() {}
                /// <summary>Runs once.</summary>
                /// <returns>Not part of the summary.</returns>
                [System.Obsolete]
                public int Run() => 1;
                public int Run(int count) => count;
                /// <summary>Nested type.</summary>
                public class Nested { public void Run() {} }
            }
            public class Undocumented
            {
                /// <summary>Only the method.</summary>
                public void Run() {}
            }
            """;
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(new SourceDocument("Worker.cs", source),
            TestContext.Current.CancellationToken);

        Assert.Equal("Outer type.", Assert.Single(symbols, symbol => symbol.Kind == "class" && symbol.Name == "Worker").Summary);
        Assert.Equal("Creates a worker.", Assert.Single(symbols, symbol => symbol.Kind == "constructor").Summary);
        var overloads = symbols.Where(symbol => symbol.QualifiedName == "Worker.Run").ToArray();
        Assert.Equal("Runs once.", overloads[0].Summary);
        Assert.Null(overloads[1].Summary);
        Assert.Equal("Nested type.", Assert.Single(symbols, symbol => symbol.Name == "Nested").Summary);
        Assert.Null(Assert.Single(symbols, symbol => symbol.QualifiedName == "Worker.Nested.Run").Summary);
        Assert.Null(Assert.Single(symbols, symbol => symbol.Name == "Undocumented").Summary);
        Assert.Equal("Only the method.", Assert.Single(symbols, symbol => symbol.QualifiedName == "Undocumented.Run").Summary);
    }

    [Theory]
    [InlineData("/// <summary>Reads &lt;items&gt; &amp; values.</summary>", "Reads <items> & values.")]
    [InlineData("/** <summary>Block documentation.</summary> */", "Block documentation.")]
    [InlineData("/**\n * <summary>First line.\n * Second line.</summary>\n */", "First line.\nSecond line.")]
    [InlineData("/// <summary>Uses <see cref=\"Worker\"/>, <paramref name=\"value\"/> and <see langword=\"null\"/>.</summary>", "Uses Worker, value and null.")]
    [InlineData("/// <summary>Uses <see cref=\"Worker\"></see> and <paramref name=\"value\"> </paramref>.</summary>", "Uses Worker and value.")]
    [InlineData("/// <summary>Uses <see cref=\"T:System.String\"/>.</summary>", "Uses T:System.String.")]
    [InlineData("/// <summary>Maps <typeparamref name=\"T\"/> to <c>output</c>.</summary>", "Maps T to output.")]
    [InlineData("/// <summary><para>First paragraph.</para><para>Second paragraph.</para></summary>", "First paragraph.\nSecond paragraph.")]
    [InlineData("/// <summary><![CDATA[Reads <raw> & values.]]></summary>", "Reads <raw> & values.")]
    public async Task Documentation_is_readable_without_xml_or_comment_markers(string documentation, string expected)
    {
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("Worker.cs", documentation + "\npublic class Worker {}"), TestContext.Current.CancellationToken);
        Assert.Equal(expected, Assert.Single(symbols).Summary);
    }

    [Theory]
    [InlineData("// <summary>Ordinary comment.</summary>")]
    [InlineData("/// <remarks>Remarks only.</remarks>")]
    [InlineData("/// <remarks><summary>Nested summary is not the declaration summary.</summary></remarks>")]
    [InlineData("/// <summary/>")]
    [InlineData("/// <summary>   </summary>")]
    [InlineData("/// <summary>Unclosed documentation.")]
    [InlineData("/// <summary>Invalid <c>nesting</summary>")]
    [InlineData("/// <summary>Invalid &entity;.</summary>")]
    [InlineData("/// <inheritdoc/>")]
    [InlineData("/// <summary><inheritdoc/></summary>")]
    [InlineData("/// <include file=\"outside.xml\" path=\"/summary\"/>")]
    public async Task Missing_empty_or_malformed_summaries_remain_absent(string documentation)
    {
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("Worker.cs", documentation + "\npublic class Worker {}"), TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(symbols).Summary);
    }

    [Fact]
    public async Task Documentation_keeps_more_than_ten_lines()
    {
        var lines = Enumerable.Range(1, 12).Select(index => $"Documentation line {index}.").ToArray();
        var source = "/// <summary>\n" + string.Join("\n", lines.Select(line => "/// " + line)) +
            "\n/// </summary>\npublic record Worker;";
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(new SourceDocument("Worker.cs", source),
            TestContext.Current.CancellationToken);
        Assert.Equal(string.Join("\n", lines), Assert.Single(symbols).Summary);
    }
}
