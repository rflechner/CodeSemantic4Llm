using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpMethodBodyEditorTests
{
    [Fact]
    public async Task Replaces_only_the_requested_overload_and_preserves_surrounding_text()
    {
        const string source = """
            class Worker
            {
                // Keep this method and comment.
                public int Run() { return 1; }
                public int Run(int value)
                {
                    return value + 1;
                }
            }
            """;
        var document = new SourceDocument("Worker.cs", source);
        var symbols = await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken);
        var target = symbols.Where(symbol => symbol.Kind == "method").Last();
        var editor = new CSharpMethodBodyEditor();

        var result = editor.Replace(document, target.Location, "{ return value * 2; }");

        Assert.Contains("public int Run() { return 1; }", result);
        Assert.Contains("// Keep this method and comment.", result);
        Assert.Contains("public int Run(int value)\n    { return value * 2; }", result);
        Assert.DoesNotContain("return value + 1;", result);

        var newBody = editor.Read(new SourceDocument("Worker.cs", result), target.Location);
        Assert.Equal("{ return value * 2; }", newBody.Text);
        Assert.False(newBody.IsExpressionBodied);
    }

    [Fact]
    public async Task Replaces_an_expression_body_without_changing_the_method_signature()
    {
        const string source = "class Worker { public int Run(int value) => value + 1; }";
        var document = new SourceDocument("Worker.cs", source);
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Kind == "method");
        var editor = new CSharpMethodBodyEditor();

        Assert.True(editor.Read(document, target.Location).IsExpressionBodied);

        var result = editor.Replace(document, target.Location, "{ return value * 2; }");
        Assert.Equal("class Worker { public int Run(int value) { return value * 2; } }", result);
    }

    [Fact]
    public async Task Preserves_crlf_and_code_outside_the_replaced_span()
    {
        const string source = "class Worker\r\n{\r\n    public void Run() { return; }\r\n    public void Keep() { }\r\n}\r\n";
        var document = new SourceDocument("Worker.cs", source);
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Name == "Run");

        var result = new CSharpMethodBodyEditor().Replace(document, target.Location, "{\r\n        return;\r\n    }");

        Assert.Equal("class Worker\r\n{\r\n    public void Run() {\r\n        return;\r\n    }\r\n    public void Keep() { }\r\n}\r\n", result);
    }

    [Theory]
    [InlineData("return 42")]
    [InlineData("return 42; }")]
    [InlineData("{ return 42;")]
    [InlineData("public int Inject() { return 42; }")]
    [InlineData("return 42; } public void Inject() {}")]
    [InlineData("using (var scope = Open()) {} finally {}")]
    [InlineData("{ return 42; } public void Inject() {}")]
    [InlineData("{ if (true) {")]
    [InlineData("{ return ; invalid @@@; }")]
    public async Task Rejects_an_invalid_or_escaping_body(string body)
    {
        var document = new SourceDocument("Worker.cs", "class Worker { int Run() { return 1; } }");
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Kind == "method");

        Assert.Throws<ArgumentException>(() => new CSharpMethodBodyEditor().Replace(
            document, target.Location, body));
    }

    [Theory]
    [InlineData("return 42;")]
    [InlineData("var value = 21;\nreturn value * 2;")]
    [InlineData("// Keep the opening comment.\nreturn 42; // Keep the final comment.")]
    [InlineData("var text = \"{}\"; /* } */ return text.Length;")]
    [InlineData("if (ready) { return 42; } else { return 0; }")]
    [InlineData("try { return 42; } finally { Cleanup(); }")]
    [InlineData("int Local() => 42; return Local();")]
    [InlineData("")]
    public async Task Accepts_inner_statements_and_preserves_surrounding_source(string statements)
    {
        const string prefix = "class Worker { int Run() ";
        const string suffix = " int Keep() => 7; }";
        var document = new SourceDocument("Worker.cs", prefix + "{ return 1; }" + suffix);
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Name == "Run");

        var result = new CSharpMethodBodyEditor().Replace(document, target.Location, statements);

        Assert.Equal(prefix + "{\n" + statements + "\n}" + suffix, result);
        Assert.Empty(new CSharpMethodSyntaxDiagnostics().ValidateReplacement(document, target.Location, statements));
    }

    [Fact]
    public async Task Added_braces_use_the_source_line_endings()
    {
        var document = new SourceDocument("Worker.cs", "class Worker\r\n{\r\n    int Run() => 1;\r\n}\r\n");
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Kind == "method");

        var result = new CSharpMethodBodyEditor().Replace(document, target.Location, "return 42; // final comment");

        Assert.Equal("class Worker\r\n{\r\n    int Run() {\r\nreturn 42; // final comment\r\n}\r\n}\r\n", result);
    }

    [Fact]
    public async Task An_explicit_block_with_comments_is_not_wrapped_again()
    {
        var document = new SourceDocument("Worker.cs", "class Worker { int Run() { return 1; } int Keep() => 7; }");
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Name == "Run");
        const string body = "// leading comment\n{ return 42; } // trailing comment";

        var result = new CSharpMethodBodyEditor().Replace(document, target.Location, body);

        Assert.Equal("class Worker { int Run() " + body + "\n int Keep() => 7; }", result);
        Assert.Empty(new CSharpMethodSyntaxDiagnostics().ValidateReplacement(document, target.Location, body));
    }

    [Fact]
    public async Task Rejects_methods_without_an_implementation()
    {
        var document = new SourceDocument("Worker.cs", "abstract class Worker { public abstract void Run(); }");
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Kind == "method");
        var editor = new CSharpMethodBodyEditor();

        Assert.Throws<InvalidOperationException>(() => editor.Read(document, target.Location));
        Assert.Throws<InvalidOperationException>(() => editor.Replace(document, target.Location, "{}"));
    }

    [Fact]
    public async Task Rejects_an_indexed_location_that_no_longer_identifies_a_method()
    {
        var document = new SourceDocument("Worker.cs", "class Worker { public void Run() {} }");
        var target = Assert.Single(await new CSharpSourceAnalyzer().AnalyzeAsync(
            document, TestContext.Current.CancellationToken), symbol => symbol.Kind == "method");

        Assert.Throws<InvalidOperationException>(() => new CSharpMethodBodyEditor().Replace(
            document, new SourceLocation("Worker.cs", target.Location.Line + 1, target.Location.Column), "{}"));
    }
}
