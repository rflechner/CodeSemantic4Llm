using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpMethodSyntaxDiagnosticsTests
{
    [Fact]
    public async Task Proposed_missing_semicolon_returns_roslyn_id_and_file_position()
    {
        var document = new SourceDocument("src/Worker.cs", """
            class Worker
            {
                public int Run()
                {
                    return 1;
                }
            }
            """);
        var location = await GetMethodAsync(document, "Run");
        var analyzer = new CSharpMethodSyntaxDiagnostics();

        var diagnostics = analyzer.ValidateReplacement(document, location, "{\n    return 2\n}");

        var error = Assert.Single(diagnostics, item => item.Id == "CS1002");
        Assert.Equal("error", error.Severity);
        Assert.Equal("src/Worker.cs", error.Location.RelativePath);
        // Roslyn anchors the missing semicolon at the end of the return expression.
        Assert.Equal(5, error.Location.Line);
        Assert.True(error.Location.Column > 0);
        // Preflight must never mutate the original source document.
        Assert.Contains("return 1;", document.Content);
    }

    [Fact]
    public async Task Invalid_shape_reports_clear_diagnostic_instead_of_throwing()
    {
        var document = new SourceDocument("Worker.cs", "class Worker { int Run() { return 1; } }");
        var location = await GetMethodAsync(document, "Run");

        var diagnostics = new CSharpMethodSyntaxDiagnostics()
            .ValidateReplacement(document, location, "{ return 42;");

        var error = Assert.Single(diagnostics);
        Assert.Equal("CS4L0001", error.Id);
        Assert.Equal("error", error.Severity);
        Assert.Equal(location, error.Location);
    }

    [Fact]
    public async Task Inner_statements_report_positions_in_the_normalized_proposed_file()
    {
        var document = new SourceDocument("Worker.cs", "class Worker { int Run() { return 1; } }");
        var location = await GetMethodAsync(document, "Run");

        var diagnostics = new CSharpMethodSyntaxDiagnostics().ValidateReplacement(document, location, "return 42");

        var error = Assert.Single(diagnostics, item => item.Id == "CS1002");
        Assert.Equal(2, error.Location.Line);
        Assert.Equal(10, error.Location.Column);
        Assert.Throws<ArgumentException>(() => new CSharpMethodBodyEditor().Replace(document, location, "return 42"));
    }

    [Fact]
    public async Task Async_statements_and_finally_blocks_are_validated_with_added_braces()
    {
        var document = new SourceDocument("Worker.cs", "class Worker { async Task<int> Run() { return 1; } }");
        var location = await GetMethodAsync(document, "Run");
        const string body = """
            // Preserve the previous context.
            var previous = GetContext();
            using var scope = OpenScope();
            try
            {
                return await RunLocalizedAsync();
            }
            finally
            {
                RestoreContext(previous);
            }
            """;

        Assert.Empty(new CSharpMethodSyntaxDiagnostics().ValidateReplacement(document, location, body));
        var result = new CSharpMethodBodyEditor().Replace(document, location, body);
        Assert.Contains("{\n" + body + "\n}", result);
    }

    [Fact]
    public async Task Proposed_valid_body_has_no_syntax_errors_even_if_it_has_unknown_types()
    {
        var document = new SourceDocument("Worker.cs", "class Worker { int Run() { return 1; } }");
        var location = await GetMethodAsync(document, "Run");

        var diagnostics = new CSharpMethodSyntaxDiagnostics()
            .ValidateReplacement(document, location, "{ return UnknownObject.MissingMember; }");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Current_diagnostics_only_include_errors_in_the_selected_method()
    {
        var document = new SourceDocument("Worker.cs", """
            class Worker
            {
                public int Good() { return 1; }
                public int Bad()
                {
                    return 2
                }
            }
            """);
        var good = await GetMethodAsync(document, "Good");
        var bad = await GetMethodAsync(document, "Bad");
        var analyzer = new CSharpMethodSyntaxDiagnostics();

        Assert.Empty(analyzer.GetCurrent(document, good, "Good"));
        var error = Assert.Single(analyzer.GetCurrent(document, bad, "Bad"),
            item => item.Id == "CS1002");
        Assert.Equal(6, error.Location.Line);
    }

    [Fact]
    public async Task A_mismatched_name_or_location_is_not_silently_accepted()
    {
        var document = new SourceDocument("Worker.cs", """
            class Worker
            {
                public void Run() {}
                public void Other() {}
            }
            """);
        var location = await GetMethodAsync(document, "Run");
        var analyzer = new CSharpMethodSyntaxDiagnostics();

        Assert.Throws<InvalidOperationException>(() => analyzer.GetCurrent(
            document, location, "Other"));
        Assert.Throws<InvalidOperationException>(() => analyzer.GetCurrent(
            document, new SourceLocation("Worker.cs", 300, 1), "Run"));
    }

    [Fact]
    public async Task Expression_bodied_methods_can_be_checked_after_an_in_memory_conversion()
    {
        var document = new SourceDocument("Worker.cs", """
            class Worker
            {
                public int Run(int value) => value + 1;
            }
            """);
        var location = await GetMethodAsync(document, "Run");
        var analyzer = new CSharpMethodSyntaxDiagnostics();

        Assert.Empty(analyzer.ValidateReplacement(document, location, "{ return value * 2; }"));
        Assert.Empty(analyzer.GetCurrent(document, location, "Run"));
        Assert.Contains("=> value + 1;", document.Content);
    }

    [Fact]
    public async Task Diagnostics_preserve_the_original_relative_file_name()
    {
        var document = new SourceDocument("folder/Worker.cs", """
            class Worker
            {
                int Run()
                {
                    return ;
                }
            }
            """);
        var location = await GetMethodAsync(document, "Run");
        // 'return ;' is syntactically valid, although invalid for the method's return type.
        Assert.Empty(new CSharpMethodSyntaxDiagnostics().GetCurrent(document, location, "Run"));
    }

    private static async Task<SourceLocation> GetMethodAsync(SourceDocument document, string name)
    {
        var symbols = await new CSharpSourceAnalyzer()
            .AnalyzeAsync(document, TestContext.Current.CancellationToken);
        return Assert.Single(symbols, symbol => symbol.Name == name && symbol.Kind == "method").Location;
    }
}
