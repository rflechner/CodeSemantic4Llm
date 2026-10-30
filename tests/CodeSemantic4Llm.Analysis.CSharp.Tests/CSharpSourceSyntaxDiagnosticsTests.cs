using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpSourceSyntaxDiagnosticsTests
{
    private readonly CSharpSourceSyntaxDiagnostics _analyzer = new();

    [Fact]
    public async Task File_diagnostics_find_errors_after_a_valid_method_and_outside_all_types()
    {
        var document = new SourceDocument("src/Worker.cs", """
            class Worker
            {
                public int Run() => 1;
                public int Later() => 2
            }
            #error Broken file
            """);
        var method = await FindDeclarationAsync(document, "Run");

        Assert.Empty(new CSharpMethodSyntaxDiagnostics().GetCurrent(document, method, "Run"));
        var diagnostics = _analyzer.GetFile(document, TestContext.Current.CancellationToken);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS1002" && diagnostic.Location.Line == 4);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS1029" && diagnostic.Location.Line == 6);
        Assert.All(diagnostics, diagnostic => Assert.Equal("src/Worker.cs", diagnostic.Location.RelativePath));
    }

    [Theory]
    [InlineData("class Worker {", "CS1513")]
    [InlineData("class Worker { int Run() => 42; } }", "CS1022")]
    [InlineData("class", "CS1001")]
    public void File_diagnostics_do_not_require_a_recoverable_symbol(string content, string diagnosticId)
    {
        var diagnostics = _analyzer.GetFile(new SourceDocument("Worker.cs", content), TestContext.Current.CancellationToken);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    }

    [Fact]
    public async Task Type_scope_includes_nested_errors_but_excludes_siblings_and_other_partial_parts()
    {
        var document = new SourceDocument("Worker.cs", """
            partial class Worker
            {
                class Nested { int Broken() => ; }
            }
            partial class Worker { int Other() => ; }
            class Sibling { int Broken() => ; }
            """);
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(document, TestContext.Current.CancellationToken);
        var declaration = symbols.First(symbol => symbol.Name == "Worker").Location;

        var diagnostics = _analyzer.GetType(document, declaration, "Worker", TestContext.Current.CancellationToken);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic => Assert.Equal(3, diagnostic.Location.Line));
        Assert.True(_analyzer.GetFile(document, TestContext.Current.CancellationToken).Count > diagnostics.Count);
    }

    [Fact]
    public async Task Missing_closing_brace_is_included_at_the_type_boundary()
    {
        var document = new SourceDocument("Worker.cs", "class Worker { int Run() => 1;");
        var declaration = await FindDeclarationAsync(document, "Worker");

        var error = Assert.Single(_analyzer.GetType(document, declaration, "Worker", TestContext.Current.CancellationToken));

        Assert.Equal("CS1513", error.Id);
        Assert.Equal(document.Content.Length + 1, error.Location.Column);
    }

    [Theory]
    [InlineData("class Worker { int Value = ; }")]
    [InlineData("record Worker { int Value = ; }")]
    [InlineData("struct Worker { int Value = ; }")]
    [InlineData("record struct Worker { int Value = ; }")]
    [InlineData("interface Worker { int Run() => ; }")]
    [InlineData("enum Worker { Value = }")]
    [InlineData("delegate void Worker(int value = );")]
    public async Task All_indexed_type_forms_support_diagnostics(string content)
    {
        var document = new SourceDocument("Worker.cs", content);
        var declaration = await FindDeclarationAsync(document, "Worker");
        Assert.NotEmpty(_analyzer.GetType(document, declaration, "Worker", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Type_selection_requires_the_exact_file_position_and_name()
    {
        var document = new SourceDocument("Worker.cs", "class Worker<T> { }");
        var declaration = await FindDeclarationAsync(document, "Worker");

        Assert.Throws<InvalidOperationException>(() => _analyzer.GetType(document, declaration, "Other", TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() =>
            _analyzer.GetType(document, new SourceLocation("Worker.cs", 2, 1), "Worker", TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() =>
            _analyzer.GetType(document, new SourceLocation("Other.cs", 1, 7), "Worker", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Diagnostics_use_physical_positions_despite_line_directives()
    {
        var document = new SourceDocument("Worker.cs", """
            #line 900 "elsewhere.cs"
            class Worker { int Run() => ; }
            """);

        var diagnostics = _analyzer.GetFile(document, TestContext.Current.CancellationToken);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("Worker.cs", diagnostic.Location.RelativePath);
            Assert.Equal(2, diagnostic.Location.Line);
        });
    }

    [Fact]
    public void Syntax_warnings_are_preserved_and_unknown_names_are_not_type_checked()
    {
        var document = new SourceDocument("Worker.cs", """
            #warning Check this
            class Worker { UnknownType Run() => Unknown.Value; }
            """);

        var warning = Assert.Single(_analyzer.GetFile(document, TestContext.Current.CancellationToken));

        Assert.Equal("CS1030", warning.Id);
        Assert.Equal("warning", warning.Severity);
    }

    [Fact]
    public void Parsing_honors_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _analyzer.GetFile(new SourceDocument("Worker.cs", "class Worker {}"), cancellation.Token));
    }

    private static async Task<SourceLocation> FindDeclarationAsync(SourceDocument document, string name)
    {
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(document, TestContext.Current.CancellationToken);
        return Assert.Single(symbols, symbol => symbol.Name == name).Location;
    }
}
