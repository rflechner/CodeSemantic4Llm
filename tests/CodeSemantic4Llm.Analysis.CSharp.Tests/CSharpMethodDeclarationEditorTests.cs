using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpMethodDeclarationEditorTests
{
    private const string Source = """
        class Worker
        {
            /// <summary>Keep the documentation.</summary>
            [Old]
            public int Run(int value)
            {
                return value;
            } // Keep the trailing comment.

            public int Run() => 7;
        }
        """;

    [Fact]
    public void Reads_the_complete_declaration_with_indentation_and_round_trips_it()
    {
        var document = new SourceDocument("Worker.cs", Source);
        var editor = new CSharpMethodDeclarationEditor();
        var location = Location(Source);

        var text = editor.Read(document, location);

        Assert.StartsWith("    [Old]\n    public int Run(int value)", text);
        Assert.EndsWith("return value;\n    }", text);
        Assert.DoesNotContain("Keep the", text);
        Assert.DoesNotContain("=> 7;", text);
        Assert.Equal(Source, editor.Replace(document, location, text).Content);
    }

    [Fact]
    public void Replaces_attributes_signature_and_body_and_returns_the_new_identifier_position()
    {
        const string replacement = """
            [New]
            [Other]
            public async Task<string> ExecuteAsync(
                string value, CancellationToken cancellationToken = default)
            {
                return await ProcessAsync(value, cancellationToken);
            }
            """;
        var document = new SourceDocument("Worker.cs", Source);

        var result = new CSharpMethodDeclarationEditor().Replace(document, Location(Source), replacement);

        Assert.Equal("""
            class Worker
            {
                /// <summary>Keep the documentation.</summary>
                [New]
                [Other]
                public async Task<string> ExecuteAsync(
                    string value, CancellationToken cancellationToken = default)
                {
                    return await ProcessAsync(value, cancellationToken);
                } // Keep the trailing comment.

                public int Run() => 7;
            }
            """, result.Content);
        Assert.Equal(Location(result.Content), result.MethodLocation);
    }

    [Theory]
    [InlineData("int Run() => 42;")]
    [InlineData("void Run();")]
    [InlineData("T IFoo.Run<T>(T value) where T : class => value;")]
    public void Accepts_expression_bodies_and_bodyless_or_generic_declarations(string replacement)
    {
        var document = new SourceDocument("Worker.cs", "interface Worker { int Run(); }");

        var result = new CSharpMethodDeclarationEditor().Replace(document, Location(document.Content), replacement);

        Assert.Equal("interface Worker { " + replacement + " }", result.Content);
        Assert.Equal(Location(result.Content), result.MethodLocation);
    }

    [Fact]
    public void Preserves_crlf_and_multiline_literal_tokens()
    {
        var document = new SourceDocument("Worker.cs", Source.Replace("\n", "\r\n", StringComparison.Ordinal));
        const string replacement = "string Run()\n{\n    return @\"first\n  second\";\n}";

        var result = new CSharpMethodDeclarationEditor().Replace(document, Location(document.Content), replacement);

        Assert.Contains("    string Run()\r\n    {\r\n        return @\"first\n  second\";\r\n    }", result.Content);
        Assert.Contains("} // Keep the trailing comment.\r\n\r\n    public int Run() => 7;", result.Content);
    }

    [Fact]
    public void Reading_a_multiline_expression_declaration_keeps_continuation_indentation_on_replacement()
    {
        const string source = "class Worker\n{\n\tint Run(\n\t\tint value) =>\n\t\tvalue;\n}";
        var document = new SourceDocument("Worker.cs", source);
        var editor = new CSharpMethodDeclarationEditor();

        Assert.Equal(source, editor.Replace(document, Location(source), editor.Read(document, Location(source))).Content);
    }

    [Theory]
    [InlineData("return 42;")]
    [InlineData("{ return 42; }")]
    [InlineData("class Other { int Run() => 42; }")]
    [InlineData("int Run() => 42; int Inject() => 7;")]
    [InlineData("int Run() { return 42;")]
    [InlineData("int Run() { return 42 }")]
    [InlineData("int Value { get; set; }")]
    [InlineData("Worker() { }")]
    [InlineData("// outside\nint Run() => 42;")]
    [InlineData("int Run() => 42; // outside")]
    [InlineData("#if true\nint Run() => 42;\n#endif")]
    [InlineData("int Run() => 42; } class Inject { int Other() => 7; }")]
    public void Rejects_anything_other_than_one_complete_method(string replacement)
    {
        var document = new SourceDocument("Worker.cs", Source);

        var error = Assert.Throws<MethodEditValidationException>(() =>
            new CSharpMethodDeclarationEditor().Replace(document, Location(Source), replacement));

        Assert.NotEmpty(error.Diagnostics);
        Assert.All(error.Diagnostics, diagnostic =>
        {
            Assert.Equal("error", diagnostic.Severity);
            Assert.Equal("Worker.cs", diagnostic.Location.RelativePath);
        });
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Unterminated_logged_string_reports_roslyn_errors_at_normalized_file_positions(string newLine)
    {
        var document = new SourceDocument("Worker.cs", Source.Replace("\n", newLine, StringComparison.Ordinal));
        const string replacement = """
            public void Run()
            {
                errors.MarkupLine("[red]tinyharness:[/] \\u0022 Markup.Escape(exception.Message));
            }
            """;

        var error = Assert.Throws<MethodEditValidationException>(() =>
            new CSharpMethodDeclarationEditor().Replace(document, Location(document.Content), replacement));

        var unterminated = Assert.Single(error.Diagnostics, diagnostic => diagnostic.Id == "CS1010");
        Assert.Equal("Newline in constant", unterminated.Message);
        Assert.Equal(new SourceLocation("Worker.cs", 6, 27), unterminated.Location);
        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Id == "CS1026");
        Assert.DoesNotContain(error.Diagnostics, diagnostic => diagnostic.Id == "CS4L0002");
        Assert.Contains("return value;", document.Content);
    }

    [Fact]
    public void Invalid_shape_has_a_diagnostic_in_addition_to_any_parser_errors()
    {
        var document = new SourceDocument("Worker.cs", Source);

        var error = Assert.Throws<MethodEditValidationException>(() =>
            new CSharpMethodDeclarationEditor().Replace(document, Location(Source), "class Other { }"));

        var diagnostic = Assert.Single(error.Diagnostics, diagnostic => diagnostic.Id == "CS4L0002");
        Assert.Contains("exactly one C# method declaration", diagnostic.Message);
    }

    [Theory]
    [InlineData("public int Run() { return 42; }")]
    [InlineData("int Run() { return 42; }")]
    [InlineData("async Task<int> Run() { return await Execute(); }")]
    [InlineData("int DifferentName() => 42;")]
    [InlineData("[Marker] public int Run() => 42;")]
    public void Body_tools_redirect_complete_declarations_instead_of_nesting_them(string declaration)
    {
        var document = new SourceDocument("Worker.cs", Source);
        var location = Location(Source);

        var error = Assert.Throws<ArgumentException>(() =>
            new CSharpMethodBodyEditor().Replace(document, location, declaration));
        Assert.Contains("replace_method", error.Message);

        var diagnostic = Assert.Single(new CSharpMethodSyntaxDiagnostics().ValidateReplacement(document, location, declaration));
        Assert.Contains("replace_method", diagnostic.Message);
    }

    [Fact]
    public void A_single_intentional_local_function_is_accepted_in_an_explicit_body_block()
    {
        var document = new SourceDocument("Worker.cs", Source);
        var result = new CSharpMethodBodyEditor().Replace(document, Location(Source), "{ void Local() { } }");

        Assert.Contains("public int Run(int value)\n    { void Local() { } }", result);
    }

    private static SourceLocation Location(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken);
        var method = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First();
        var position = method.Identifier.GetLocation().GetLineSpan().StartLinePosition;
        return new SourceLocation("Worker.cs", position.Line + 1, position.Character + 1);
    }
}
