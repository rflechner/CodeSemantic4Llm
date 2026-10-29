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

    [Fact]
    public async Task Members_reference_their_immediate_type_declaration_without_name_matching()
    {
        const string source = """
            namespace Demo;
            public class Worker { public void Plain() {} }
            public class Worker<T>
            {
                public Worker() {}
                public int Value;
                public int Count { get; set; }
                public event System.Action Changed;
                public void Run() {}
                public void Run(int count) { void Local() {} }
                public class Nested { public void Inner() {} }
                public enum State { Ready }
            }
            """;
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("src/Worker.cs", source), TestContext.Current.CancellationToken);
        var types = symbols.Where(symbol => symbol.Kind == "class" && symbol.Name == "Worker").ToArray();
        Assert.Equal(2, types.Length);
        Assert.Equal(types[0].Location, Assert.Single(symbols, symbol => symbol.Name == "Plain").ContainingType);
        var genericMembers = symbols.Where(symbol => symbol.ContainingType == types[1].Location).ToArray();
        Assert.Equal(new[] { "Worker", "Value", "Count", "Changed", "Run", "Run", "Nested", "State" },
            genericMembers.Select(symbol => symbol.Name));
        Assert.DoesNotContain(symbols, symbol => symbol.Name == "Local");
        var nested = Assert.Single(symbols, symbol => symbol.Name == "Nested");
        Assert.Equal(nested.Location, Assert.Single(symbols, symbol => symbol.Name == "Inner").ContainingType);
        var enumeration = Assert.Single(symbols, symbol => symbol.Kind == "enum");
        Assert.Equal(enumeration.Location, Assert.Single(symbols, symbol => symbol.Name == "Ready").ContainingType);
        Assert.All(types, type => Assert.Null(type.ContainingType));
    }

    [Fact]
    public async Task Explicit_interface_methods_and_record_members_retain_their_declaring_types()
    {
        const string source = """
            namespace Demo
            {
                interface IWorker { void Run(); }
                record Worker(int Value) : IWorker
                {
                    void IWorker.Run() {}
                    public void Run<T>(T input) {}
                }
            }
            """;
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("src/Worker.cs", source), TestContext.Current.CancellationToken);
        var contract = Assert.Single(symbols, symbol => symbol.Kind == "interface");
        var record = Assert.Single(symbols, symbol => symbol.Kind == "record");
        Assert.Single(symbols, symbol => symbol.ContainingType == contract.Location);
        var implementations = symbols.Where(symbol => symbol.ContainingType == record.Location).ToArray();
        Assert.Equal(2, implementations.Length);
        Assert.All(implementations, symbol => Assert.Equal("method", symbol.Kind));
        Assert.NotEqual(implementations[0].Location, implementations[1].Location);
    }
}
