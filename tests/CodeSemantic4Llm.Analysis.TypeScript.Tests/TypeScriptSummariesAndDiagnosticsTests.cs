using CodeSemantic4Llm.Analysis.TypeScript;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Xunit;

namespace CodeSemantic4Llm.Analysis.TypeScript.Tests;

public sealed class TypeScriptSummariesAndDiagnosticsTests
{
    [Fact]
    public async Task Extract_includes_full_type_and_method_with_leading_jsdoc()
    {
        const string source = """
            /** A widget that can run jobs. */
            export class Widget {
              /** Runs the selected job. */
              run(job: string): void { console.log(job); }
            }
            """;

        var document = new SourceDocument("widget.ts", source);
        var symbols = await new TypeScriptSourceAnalyzer().AnalyzeAsync(document);
        var snippets = new TypeScriptSymbolSourceExtractor().Extract(document, symbols);

        var widget = Assert.Single(symbols, symbol => symbol.Kind == "class");
        var run = Assert.Single(symbols, symbol => symbol.Kind == "method");
        Assert.Equal("A widget that can run jobs.", widget.Summary);
        Assert.Equal("Runs the selected job.", run.Summary);
        Assert.Contains("run(job: string)", snippets[run.Location]);
        Assert.Contains("/** Runs the selected job. */", snippets[run.Location]);
        Assert.Contains("/** A widget that can run jobs. */", snippets[widget.Location]);
    }

    [Fact]
    public void GetFile_identifies_invalid_syntax_without_executing_the_source()
    {
        var diagnostics = new TypeScriptSourceSyntaxDiagnostics();
        var valid = new SourceDocument("valid.ts", "export class Good { value = 1; }");
        var invalid = new SourceDocument("invalid.ts", "export class Broken { value = ; }");

        Assert.Empty(diagnostics.GetFile(valid));
        Assert.Contains(diagnostics.GetFile(invalid),
            diagnostic => diagnostic.Severity == "error"
                && diagnostic.Location.RelativePath == "invalid.ts"
                && diagnostic.Location.Line >= 1);
    }

    [Fact]
    public async Task GetType_scopes_to_the_exact_declared_type_and_rejects_stale_selections()
    {
        const string source = """
            class Good { value = 1; }
            class Broken { value = ; }
            """;

        var document = new SourceDocument("src/models.ts", source);
        var symbols = await new TypeScriptSourceAnalyzer().AnalyzeAsync(document);
        var valid = Assert.Single(symbols, symbol => symbol.Name == "Good");
        var broken = Assert.Single(symbols, symbol => symbol.Name == "Broken");
        var diagnostics = new TypeScriptSourceSyntaxDiagnostics();

        Assert.Empty(diagnostics.GetType(document, valid.Location, valid.Name));
        Assert.NotEmpty(diagnostics.GetType(document, broken.Location, broken.Name));
        Assert.Throws<InvalidOperationException>(() =>
            diagnostics.GetType(document, broken.Location, valid.Name));
        Assert.Throws<ArgumentException>(() =>
            diagnostics.GetType(document, new SourceLocation("other.ts", 2, 7), broken.Name));
    }
}
