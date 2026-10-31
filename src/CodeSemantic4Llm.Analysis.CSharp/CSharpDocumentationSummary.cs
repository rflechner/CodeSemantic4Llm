using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>Reads a declaration's own XML summary as text without binding or expanding external documentation.</summary>
internal static class CSharpDocumentationSummary
{
    public static string? Read(MemberDeclarationSyntax declaration)
    {
        if (declaration is not (TypeDeclarationSyntax or MethodDeclarationSyntax or ConstructorDeclarationSyntax))
        {
            return null;
        }

        var documentation = declaration.GetLeadingTrivia()
            .Select(trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .LastOrDefault();
        var summary = documentation?.Content.OfType<XmlElementSyntax>()
            .FirstOrDefault(element => element.StartTag.Name.ToString() == "summary");

        if (summary is null || summary.ContainsDiagnostics)
        {
            return null;
        }

        var text = new StringBuilder();

        foreach (var node in summary.Content)
        {
            AppendText(text, node);
        }

        // XML exterior trivia (/// or /** */) is excluded by reading token values. Keep the
        // complete documentation, removing indentation and blank lines rather than truncating it.
        var normalized = string.Join("\n", text.ToString().Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void AppendText(StringBuilder text, XmlNodeSyntax node)
    {
        switch (node)
        {
            case XmlTextSyntax value:
                AppendTokens(text, value.TextTokens);
                break;
            case XmlCDataSectionSyntax data:
                AppendTokens(text, data.TextTokens);
                break;
            case XmlElementSyntax element:
                AppendElement(text, element);
                break;
            case XmlEmptyElementSyntax element:
                AppendReference(text, element.Name.ToString(), element.Attributes);
                break;
        }
    }

    private static void AppendElement(StringBuilder text, XmlElementSyntax element)
    {
        var name = element.StartTag.Name.ToString();

        if (name is "see" or "seealso" or "paramref" or "typeparamref"
            && element.Content.All(node => node is XmlTextSyntax value
                && value.TextTokens.All(token => string.IsNullOrWhiteSpace(token.ValueText))))
        {
            AppendReference(text, name, element.StartTag.Attributes);
            return;
        }

        var block = name is "para" or "code" or "list" or "item" or "term" or "description";

        if (block)
        {
            text.Append('\n');
        }

        foreach (var child in element.Content)
        {
            AppendText(text, child);
        }

        if (block)
        {
            text.Append('\n');
        }
    }

    private static void AppendReference(StringBuilder text, string name, SyntaxList<XmlAttributeSyntax> attributes)
    {
        if (name == "br")
        {
            text.Append('\n');
        }
        else if (name is "see" or "seealso" or "paramref" or "typeparamref")
        {
            foreach (var attribute in attributes)
            {
                switch (attribute)
                {
                    case XmlCrefAttributeSyntax reference:
                        text.Append(reference.Cref.ToString());
                        return;
                    case XmlNameAttributeSyntax parameter:
                        text.Append(parameter.Identifier.Identifier.ValueText);
                        return;
                    case XmlTextAttributeSyntax value when value.Name.ToString() is "langword" or "href" or "cref" or "name":
                        AppendTokens(text, value.TextTokens);
                        return;
                }
            }
        }
    }

    private static void AppendTokens(StringBuilder text, SyntaxTokenList tokens)
    {
        foreach (var token in tokens)
        {
            text.Append(token.ValueText);
        }
    }
}
