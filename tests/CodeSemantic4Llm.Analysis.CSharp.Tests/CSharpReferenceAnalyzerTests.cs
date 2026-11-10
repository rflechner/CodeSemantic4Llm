using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpReferenceAnalyzerTests
{
    [Fact]
    public async Task Resolve_calls_by_method_symbol_instead_of_matching_the_simple_name()
    {
        var worker = new SourceDocument("Worker.cs", """
            class Worker
            {
                public void Run() {}
                public void Run(int value) {}
            }
            class Other { public void Run() {} }
            """);
        var caller = new SourceDocument("Caller.cs", """
            class Caller
            {
                void Use(Worker worker, Other other)
                {
                    worker.Run();
                    worker.Run(1);
                    other.Run();
                }
            }
            """);
        var definitions = await new CSharpSourceAnalyzer().AnalyzeAsync(worker, TestContext.Current.CancellationToken);
        var methods = definitions.Where(symbol => symbol.Name == "Run").ToArray();
        Assert.Equal(3, methods.Length);

        var references = await new CSharpReferenceAnalyzer().AnalyzeAsync([worker, caller], TestContext.Current.CancellationToken);
        var first = Assert.Single(references, reference => reference.Target == methods[0].Location);
        var second = Assert.Single(references, reference => reference.Target == methods[1].Location);
        var third = Assert.Single(references, reference => reference.Target == methods[2].Location);

        Assert.Equal("Caller.cs", first.Usage.RelativePath);
        Assert.Equal("Caller.cs", second.Usage.RelativePath);
        Assert.Equal("Caller.cs", third.Usage.RelativePath);
        Assert.Equal(5, first.Usage.Line);
        Assert.Equal(6, second.Usage.Line);
        Assert.Equal(7, third.Usage.Line);
        Assert.Equal(3, references.Count(reference => methods.Any(method => method.Location == reference.Target)));
    }

    [Fact]
    public async Task Partial_type_declarations_both_receive_type_references()
    {
        var first = new SourceDocument("First.cs", "partial class Worker {}");
        var second = new SourceDocument("Second.cs", "partial class Worker {}");
        var caller = new SourceDocument("Caller.cs", "class Caller { Worker Value = new Worker(); }");
        var analyzer = new CSharpSourceAnalyzer();
        var firstDeclaration = Assert.Single(await analyzer.AnalyzeAsync(first, TestContext.Current.CancellationToken));
        var secondDeclaration = Assert.Single(await analyzer.AnalyzeAsync(second, TestContext.Current.CancellationToken));

        var references = await new CSharpReferenceAnalyzer().AnalyzeAsync(
            [first, second, caller], TestContext.Current.CancellationToken);

        Assert.Equal(2, references.Count(reference => reference.Target == firstDeclaration.Location));
        Assert.Equal(2, references.Count(reference => reference.Target == secondDeclaration.Location));
        Assert.All(references, reference => Assert.Equal("Caller.cs", reference.Usage.RelativePath));
    }

    [Fact]
    public async Task Reduced_extension_methods_point_to_the_declared_method()
    {
        var source = new SourceDocument("Extensions.cs", """
            class Worker {}
            static class Extensions
            {
                public static void Ping(this Worker worker) {}
            }
            class Caller { void Use(Worker worker) { worker.Ping(); } }
            """);
        var method = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            source, TestContext.Current.CancellationToken), symbol => symbol.Name == "Ping");

        var references = await new CSharpReferenceAnalyzer().AnalyzeAsync(
            [source], TestContext.Current.CancellationToken);

        var reference = Assert.Single(references, usage => usage.Target == method.Location);
        Assert.Equal(6, reference.Usage.Line);
    }

    [Fact]
    public async Task Reports_parsing_and_semantic_resolution_progress_for_each_file()
    {
        var documents = new[]
        {
            new SourceDocument("Worker.cs", "class Worker { public void Run() {} }"),
            new SourceDocument("Caller.cs", "class Caller { void Go() { new Worker().Run(); } }")
        };
        var updates = new List<ReferenceAnalysisProgress>();
        var references = await new CSharpReferenceAnalyzer().AnalyzeAsync(
            documents, TestContext.Current.CancellationToken, new InlineProgress(updates.Add));

        Assert.NotEmpty(references);
        Assert.Equal(new[] { 0, 1, 2 }, updates.Where(item => item.Phase == ReferenceAnalysisPhase.Parsing)
            .Select(item => item.CompletedFiles));
        Assert.Equal(new[] { 0, 1, 2 }, updates.Where(item => item.Phase == ReferenceAnalysisPhase.Resolving)
            .Select(item => item.CompletedFiles));
        Assert.Contains(updates, item => item.Phase == ReferenceAnalysisPhase.PreparingCompilation);
        Assert.Contains(updates, item => item.Phase == ReferenceAnalysisPhase.Sorting);
        Assert.All(updates, item =>
        {
            if (item.Phase is ReferenceAnalysisPhase.Parsing or ReferenceAnalysisPhase.Resolving)
            {
                Assert.Equal(2, item.TotalFiles);
            }
            else
            {
                Assert.Equal(0, item.TotalFiles);
            }
        });
    }

    private sealed class InlineProgress(Action<ReferenceAnalysisProgress> report) : IProgress<ReferenceAnalysisProgress>
    {
        public void Report(ReferenceAnalysisProgress value) => report(value);
    }

    [Fact]
    public async Task Unresolved_calls_are_never_mapped_by_name_alone()
    {
        var source = new SourceDocument("Worker.cs", """
            class Worker { public void Run() {} }
            class Caller { void Use() { Unknown.Run(); } }
            """);
        var method = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            source, TestContext.Current.CancellationToken), symbol => symbol.Name == "Run");

        var references = await new CSharpReferenceAnalyzer().AnalyzeAsync(
            [source], TestContext.Current.CancellationToken);

        Assert.DoesNotContain(references, usage => usage.Target == method.Location);
    }
}
