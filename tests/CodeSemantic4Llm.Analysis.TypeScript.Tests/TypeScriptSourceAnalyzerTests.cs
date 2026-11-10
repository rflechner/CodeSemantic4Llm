using CodeSemantic4Llm.Analysis.TypeScript;
using CodeSemantic4Llm.Domain.Source;
using Xunit;

namespace CodeSemantic4Llm.Analysis.TypeScript.Tests;

public sealed class TypeScriptSourceAnalyzerTests
{
    [Fact]
    public void Supports_typescript_extensions_without_claiming_tsx_or_javascript()
    {
        var analyzer = new TypeScriptSourceAnalyzer();

        Assert.Equal("typescript", analyzer.AnalyzerId);
        Assert.Equal("typescript", analyzer.LanguageId);
        Assert.Equal([".ts", ".mts", ".cts"], analyzer.DefaultFileExtensions);
        Assert.True(analyzer.Supports("src/widget.TS"));
        Assert.True(analyzer.Supports("src/index.mts"));
        Assert.True(analyzer.Supports("src/runtime.cts"));
        Assert.False(analyzer.Supports("src/widget.tsx"));
        Assert.False(analyzer.Supports("src/app.js"));
    }

    [Fact]
    public async Task AnalyzeAsync_extracts_namespaced_types_methods_and_angular_decorated_classes()
    {
        const string source = """
            export namespace Demo {
              /** A data source for widgets. */
              export interface IRepository {
                find(id: string): string;
                label: string;
              }

              @Component({ selector: "demo-widget", template: "<span>ok</span>" })
              export class Widget {
                name: string = "";
                constructor(name: string) { this.name = name; }
                rename(next: string): void {
                  function hiddenLocal() {}
                }
              }

              export enum Status { Ready, Done = 2 }
              export type WidgetId = string;
              export const timeout = 5, retries = 3;
              export function greet(name: string): string { return name; }
            }
            """;

        var symbols = await new TypeScriptSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("src/widget.ts", source), TestContext.Current.CancellationToken);

        var widget = Assert.Single(symbols, symbol => symbol.Kind == "class"
            && symbol.QualifiedName == "Demo.Widget");
        var contract = Assert.Single(symbols, symbol => symbol.Kind == "interface"
            && symbol.QualifiedName == "Demo.IRepository");
        Assert.Equal("typescript", widget.Language);
        Assert.Equal("src/widget.ts", widget.Location.RelativePath);
        Assert.Equal(widget.Location, Assert.Single(symbols, symbol =>
            symbol.QualifiedName == "Demo.Widget.rename").ContainingType);
        Assert.Equal(widget.Location, Assert.Single(symbols, symbol =>
            symbol.QualifiedName == "Demo.Widget.constructor").ContainingType);
        Assert.Equal(contract.Location, Assert.Single(symbols, symbol =>
            symbol.QualifiedName == "Demo.IRepository.find").ContainingType);
        Assert.Equal(contract.Location, Assert.Single(symbols, symbol =>
            symbol.QualifiedName == "Demo.IRepository.label").ContainingType);
        Assert.Equal("A data source for widgets.", contract.Summary);
        Assert.Contains(symbols, symbol => symbol.Kind == "enum" && symbol.QualifiedName == "Demo.Status");
        Assert.Contains(symbols, symbol => symbol.Kind == "enum-member" && symbol.QualifiedName == "Demo.Status.Ready");
        Assert.Contains(symbols, symbol => symbol.Kind == "enum-member" && symbol.QualifiedName == "Demo.Status.Done");
        Assert.Contains(symbols, symbol => symbol.Kind == "type-alias" && symbol.QualifiedName == "Demo.WidgetId");
        Assert.Contains(symbols, symbol => symbol.Kind == "variable" && symbol.QualifiedName == "Demo.timeout");
        Assert.Contains(symbols, symbol => symbol.Kind == "variable" && symbol.QualifiedName == "Demo.retries");
        Assert.Contains(symbols, symbol => symbol.Kind == "function" && symbol.QualifiedName == "Demo.greet");
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "hiddenLocal");
        Assert.All(symbols, symbol => Assert.True(symbol.Location.Line > 0 && symbol.Location.Column > 0));
    }

    [Fact]
    public async Task AnalyzeAsync_preserves_declaring_types_without_guessing_from_names()
    {
        const string source = """
            class First { run() {} }
            class Second {
              run() {}
              nested = () => { const localValue = 3; };
            }
            """;

        var symbols = await new TypeScriptSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("src/owners.ts", source), TestContext.Current.CancellationToken);

        var types = symbols.Where(symbol => symbol.Kind == "class").ToArray();
        Assert.Equal(2, types.Length);
        Assert.Equal(types[0].Location, Assert.Single(symbols, symbol =>
            symbol.Kind == "method" && symbol.ContainingType == types[0].Location).ContainingType);
        Assert.Equal(types[1].Location, Assert.Single(symbols, symbol =>
            symbol.Kind == "method" && symbol.ContainingType == types[1].Location).ContainingType);
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "localValue");
    }

    [Fact]
    public async Task AnalyzeAsync_rejects_unavailable_tsx_grammar()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            new TypeScriptSourceAnalyzer().AnalyzeAsync(
                new SourceDocument("components/Widget.tsx", "export const Widget = () => <h1>Hello</h1>;")));

        Assert.Contains("TSX", exception.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_honors_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TypeScriptSourceAnalyzer().AnalyzeAsync(
                new SourceDocument("widget.ts", "class Widget {}"), cancellation.Token));
    }
}
