using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CodeSemantic4Llm.Analysis.CSharp.Tests;

public sealed class CSharpMethodBodyIndentationTests
{
    private const string Source = """
        class Worker
        {
            public int Run()
            {
                return 1;
            }

            public int Keep() => 7;
        }
        """;

    [Fact]
    public void Indents_a_relative_block_including_comments_before_the_opening_brace()
    {
        const string body = """
            // Start execution.
            // Preserve the current context.
            {
                var previous = GetContext();

                try {
                    return Execute();
                }
                finally {
                    RestoreContext(previous);
                }
            }
            """;

        var result = Replace(Source, body);

        Assert.Equal("""
            class Worker
            {
                public int Run()
                // Start execution.
                // Preserve the current context.
                {
                    var previous = GetContext();

                    try {
                        return Execute();
                    }
                    finally {
                        RestoreContext(previous);
                    }
                }

                public int Keep() => 7;
            }
            """, result);
    }

    [Theory]
    [InlineData("{\n    return 42;\n}")]
    [InlineData("    {\n        return 42;\n    }")]
    [InlineData("            {\n                return 42;\n            }")]
    [InlineData("return 42;")]
    [InlineData("        return 42;")]
    public void Accepts_relative_absolute_and_excess_indentation_without_accumulating_it(string body)
    {
        var result = Replace(Source, body);

        Assert.Equal(Source.Replace("return 1;", "return 42;", StringComparison.Ordinal), result);
        var parsedBody = ParseRoot(result).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First().Body!.ToString();
        Assert.Equal(result, Replace(result, parsedBody));
    }

    [Fact]
    public void Reindents_inner_statements_without_losing_the_first_lines_original_indent()
    {
        const string statements = "    var value = 21;\n    if (ready)\n    {\n        return value * 2;\n    }\n    return 0;";

        var result = Replace(Source, statements);

        Assert.Contains("""
                {
                    var value = 21;
                    if (ready)
                    {
                        return value * 2;
                    }
                    return 0;
                }
            """, result);
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("\t")]
    public void Uses_the_existing_indentation_inside_a_nested_type(string indent)
    {
        var source = "namespace Example\n{\n" + indent + "class Worker\n" + indent + "{\n"
            + indent + indent + "int Run()\n" + indent + indent + "{\n"
            + indent + indent + indent + "return 1;\n" + indent + indent + "}\n"
            + indent + "}\n}";

        Assert.Equal(source.Replace("return 1;", "return 42;", StringComparison.Ordinal),
            Replace(source, "{\n    return 42;\n}"));
    }

    [Fact]
    public void Infers_indentation_for_an_expression_body_with_a_multiline_signature()
    {
        const string source = "class Worker\n{\n  int Run(\n    int value) => value;\n}";

        var result = Replace(source, "return value * 2;");

        Assert.Equal("class Worker\n{\n  int Run(\n    int value) {\n    return value * 2;\n  }\n}", result);
    }

    [Fact]
    public void Uses_source_line_endings_outside_multiline_literals()
    {
        var source = Source.Replace("\n", "\r\n", StringComparison.Ordinal);

        var result = Replace(source, "{\n    return 42;\n}");

        Assert.Equal(source.Replace("return 1;", "return 42;", StringComparison.Ordinal), result);
    }

    [Fact]
    public void Preserves_multiline_literal_tokens_and_comment_contents_exactly()
    {
        const string body = """"
            {
                var verbatim = @"first
              second
            last";
                var raw = """
                    first
                      second
                    """;
                var interpolated = $@"first {1}
              second {2}
            last";
                var interpolatedRaw = $"""
                    first {1}
                      second {2}
                    """;
                /* Keep this text.
             * And its spacing.
             */
                return raw.Length;
            }
            """";
        var source = Source.Replace("\n", "\r\n", StringComparison.Ordinal);

        var result = Replace(source, body);
        var originalBlock = SyntaxFactory.ParseStatement(body);
        var resultBlock = ParseRoot(result).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First().Body!;

        Assert.Equal(originalBlock.DescendantTokens().Select(token => token.Text),
            resultBlock.DescendantTokens().Select(token => token.Text));
        Assert.Equal(originalBlock.DescendantTrivia().Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            .Select(trivia => trivia.ToString()),
            resultBlock.DescendantTrivia().Where(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                .Select(trivia => trivia.ToString()));
        Assert.Contains("\r\n        var verbatim", result);
        Assert.Contains("\r\n        return raw.Length;\r\n    }", result);
    }

    [Fact]
    public void Preserves_disabled_preprocessor_text()
    {
        const string body = "{\n#if NEVER_DEFINED\n  not parsed {\n    arbitrary text\n#endif\n    return 42;\n}";

        var result = Replace(Source, body);

        Assert.Contains("  not parsed {\n    arbitrary text\n", result);
        Assert.Contains("\n        return 42;\n    }", result);
    }

    [Fact]
    public void Validation_reports_columns_after_the_same_indentation_adjustment_as_writing()
    {
        var document = new SourceDocument("Worker.cs", Source);
        var location = FindRun(document);

        var diagnostics = new CSharpMethodSyntaxDiagnostics().ValidateReplacement(document, location, "return 42");

        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "CS1002");
        Assert.Equal(5, error.Location.Line);
        Assert.Equal(18, error.Location.Column);
    }

    private static string Replace(string source, string body)
    {
        var document = new SourceDocument("Worker.cs", source);
        return new CSharpMethodBodyEditor().Replace(document, FindRun(document), body);
    }

    private static SourceLocation FindRun(SourceDocument document)
    {
        var method = ParseRoot(document.Content).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single(candidate => candidate.Identifier.ValueText == "Run");
        var position = method.Identifier.GetLocation().GetLineSpan().StartLinePosition;
        return new SourceLocation(document.RelativePath, position.Line + 1, position.Character + 1);
    }

    private static SyntaxNode ParseRoot(string source) =>
        CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken);
}
