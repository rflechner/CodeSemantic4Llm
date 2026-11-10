using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class RazorSourceAnalyzerTests
{
    [Fact]
    public void Supports_blazor_components_and_classic_razor_views()
    {
        var analyzer = new RazorSourceAnalyzer();

        Assert.Equal("dotnet-razor", analyzer.AnalyzerId);
        Assert.Equal([".razor", ".cshtml"], analyzer.DefaultFileExtensions);
        Assert.True(analyzer.Supports("Components/Counter.RAZOR"));
        Assert.True(analyzer.Supports("Views/Home/Index.cshtml"));
        Assert.False(analyzer.Supports("Components/Counter.razor.cs"));
        Assert.False(analyzer.Supports("Program.cs"));
    }

    [Fact]
    public async Task Blazor_markup_and_code_members_are_indexed_at_original_positions()
    {
        const string source = """
            @page "/counter"
            @using Sample.Components
            <h1>Counter</h1>
            <ChildCounter Value="@count" />
            @* @code { void FakeFromComment() {} } *@
            @code {
                private int count;
                /// <summary>Increases the counter.</summary>
                private void Increment()
                {
                    var brace = "}";
                    count++;
                }
                private sealed class Nested
                {
                    public void Inner() {}
                }
            }
            """;
        var document = new SourceDocument("Components/Counter.razor", source);
        var symbols = await new RazorSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken);

        var component = Assert.Single(symbols, symbol => symbol.Kind == "component");
        Assert.Equal("Counter", component.Name);
        Assert.Equal("Components.Counter", component.QualifiedName);
        Assert.Equal(1, component.Location.Line);
        Assert.Contains("Route: /counter", component.Summary);
        Assert.Contains("Headings: Counter", component.Summary);
        Assert.Contains("Uses components: ChildCounter", component.Summary);

        var count = Assert.Single(symbols, symbol => symbol.Name == "count");
        Assert.Equal("field", count.Kind);
        Assert.Equal(component.Location, count.ContainingType);
        Assert.Equal(7, count.Location.Line);

        var increment = Assert.Single(symbols, symbol => symbol.Name == "Increment");
        Assert.Equal("method", increment.Kind);
        Assert.Equal("Components.Counter.Increment", increment.QualifiedName);
        Assert.Equal(component.Location, increment.ContainingType);
        Assert.Equal("Increases the counter.", increment.Summary);
        Assert.Equal(9, increment.Location.Line);
        Assert.Equal(18, increment.Location.Column);

        var nested = Assert.Single(symbols, symbol => symbol.Name == "Nested");
        var inner = Assert.Single(symbols, symbol => symbol.Name == "Inner");
        Assert.Equal(nested.Location, inner.ContainingType);
        Assert.Equal("Components.Counter.Nested.Inner", inner.QualifiedName);
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "FakeFromComment");
    }

    [Fact]
    public async Task Classic_cshtml_functions_helpers_sections_and_model_are_searchable()
    {
        const string source = """
            @model Sample.Web.HomeViewModel
            @* @section Fake { } *@
            <h1>Welcome</h1>
            @section Scripts {
                <script>console.log("ready")</script>
            }
            @helper Badge(string label) {
                <strong>@label</strong>
            }
            @functions {
                public string Title { get; set; } = "Home";
                public string Format() { return "}"; }
            }
            """;
        var symbols = await new RazorSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("Views/Home/Index.cshtml", source),
            TestContext.Current.CancellationToken);

        var view = Assert.Single(symbols, symbol => symbol.Kind == "view");
        Assert.Equal("Views.Home.Index", view.QualifiedName);
        Assert.Contains("Model: Sample.Web.HomeViewModel", view.Summary);
        Assert.Contains("Headings: Welcome", view.Summary);

        var section = Assert.Single(symbols, symbol => symbol.Kind == "section");
        Assert.Equal("Scripts", section.Name);
        Assert.Equal(4, section.Location.Line);
        Assert.Equal(view.Location, section.ContainingType);

        var helper = Assert.Single(symbols, symbol => symbol.Kind == "helper");
        Assert.Equal("Badge", helper.Name);
        Assert.Equal(7, helper.Location.Line);

        var title = Assert.Single(symbols, symbol => symbol.Kind == "property");
        Assert.Equal("Title", title.Name);
        Assert.Equal(11, title.Location.Line);

        var method = Assert.Single(symbols, symbol => symbol.Name == "Format");
        Assert.Equal("Views.Home.Index.Format", method.QualifiedName);
        Assert.Equal(12, method.Location.Line);
        Assert.Equal(view.Location, method.ContainingType);
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "Fake");
    }

    [Fact]
    public async Task Multiple_code_blocks_with_comments_and_literals_keep_independent_positions()
    {
        const string source = """
            <p>Example</p>
            @code {
                private string A() => @"{ brace }";
                /* } inside a comment */
            }
            @code {
                private string B() => """{ raw }""";
            }
            """;
        var symbols = await new RazorSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("Example.razor", source),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, symbols.Count);
        Assert.Equal(3, Assert.Single(symbols, symbol => symbol.Name == "A").Location.Line);
        Assert.Equal(7, Assert.Single(symbols, symbol => symbol.Name == "B").Location.Line);
    }

    [Fact]
    public async Task An_unterminated_code_block_does_not_produce_fictitious_declarations()
    {
        const string source = """
            @page "/broken"
            @code {
                private void Unfinished() {
            """;
        var symbols = await new RazorSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("Broken.razor", source),
            TestContext.Current.CancellationToken);

        Assert.Single(symbols);
        Assert.Equal("component", symbols[0].Kind);
    }

    [Fact]
    public async Task Summary_extraction_uses_the_original_razor_and_member_source()
    {
        const string source = """
            <h2>Greeting</h2>
            @code {
                /// <summary>Formats a name.</summary>
                public string Format(string name) => $"Hi {name}";
            }
            """;
        var document = new SourceDocument("Greeting.razor", source);
        var analyzer = new RazorSourceAnalyzer();
        var symbols = await analyzer.AnalyzeAsync(document, TestContext.Current.CancellationToken);
        var snippets = new RazorSymbolSourceExtractor().Extract(
            document, symbols, TestContext.Current.CancellationToken);
        var component = Assert.Single(symbols, symbol => symbol.Kind == "component");
        var method = Assert.Single(symbols, symbol => symbol.Kind == "method");

        Assert.Equal(source, snippets[component.Location]);
        Assert.Contains("/// <summary>Formats a name.</summary>", snippets[method.Location]);
        Assert.Contains("public string Format", snippets[method.Location]);
        Assert.DoesNotContain("<h2>", snippets[method.Location]);
        Assert.DoesNotContain("__CodeSemanticRazorHost__", snippets[method.Location]);
    }
}
