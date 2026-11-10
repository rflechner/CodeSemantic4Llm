using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using TreeSitter;

namespace CodeSemantic4Llm.Analysis.TypeScript;

/// <summary>A declaration and its transient syntax node, valid only while its parsed tree is alive.</summary>
internal sealed record TypeScriptDeclaration(Node Syntax, SymbolDefinition Symbol);

/// <summary>
/// Walks declaration-bearing scopes rather than all descendants, so local functions and object literals
/// are not accidentally attributed to the containing class or exported as top-level declarations.
/// </summary>
internal static class TypeScriptDeclarations
{
    public static IReadOnlyList<TypeScriptDeclaration> Read(
        SourceDocument document, Node root, CancellationToken cancellationToken)
    {
        var declarations = new List<TypeScriptDeclaration>();
        VisitChildren(root, string.Empty, null);
        return declarations;

        void VisitChildren(Node container, string scope, SourceLocation? containingType)
        {
            foreach (var child in container.NamedChildren)
            {
                Visit(child, scope, containingType);
            }
        }

        void Visit(Node node, string scope, SourceLocation? containingType)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (node.Type)
            {
                case "export_statement":
                case "ambient_declaration":
                    VisitChildren(node, scope, containingType);
                    break;

                case "class_declaration":
                case "abstract_class_declaration":
                case "interface_declaration":
                case "enum_declaration":
                {
                    var kind = node.Type switch
                    {
                        "interface_declaration" => "interface",
                        "enum_declaration" => "enum",
                        _ => "class"
                    };
                    var type = Add(node, kind, scope, containingType);
                    var body = node.GetChildForField("body");

                    if (type is null || body is null)
                    {
                        break;
                    }

                    if (kind == "enum")
                    {
                        VisitEnum(body, type.Symbol.QualifiedName, type.Symbol.Location);
                    }
                    else
                    {
                        VisitChildren(body, type.Symbol.QualifiedName, type.Symbol.Location);
                    }

                    break;
                }

                case "internal_module":
                case "module":
                {
                    var module = Add(node, "namespace", scope, null);
                    var body = node.GetChildForField("body");

                    if (module is not null && body is not null)
                    {
                        VisitChildren(body, module.Symbol.QualifiedName, null);
                    }

                    break;
                }

                case "type_alias_declaration":
                    Add(node, "type-alias", scope, containingType);
                    break;

                case "function_declaration":
                case "generator_function_declaration":
                case "function_signature":
                    Add(node, "function", scope, containingType);
                    break;

                case "lexical_declaration":
                case "variable_declaration":
                    foreach (var declarator in node.NamedChildren.Where(child => child.Type == "variable_declarator"))
                    {
                        Add(declarator, "variable", scope, containingType);
                    }
                    break;

                case "method_definition":
                case "method_signature":
                case "abstract_method_signature":
                    if (containingType is not null)
                    {
                        var name = node.GetChildForField("name");
                        Add(node, name?.Text == "constructor" ? "constructor" : "method", scope, containingType);
                    }

                    break;

                case "public_field_definition":
                case "property_signature":
                    if (containingType is not null)
                    {
                        Add(node, "property", scope, containingType);
                    }

                    break;
            }
        }

        void VisitEnum(Node body, string scope, SourceLocation containingType)
        {
            foreach (var member in body.NamedChildren)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (member.Type == "enum_assignment")
                {
                    Add(member, "enum-member", scope, containingType);
                }
                else if (member.Type is "property_identifier" or "string" or "number")
                {
                    Add(member, member, "enum-member", scope, containingType);
                }
            }
        }

        TypeScriptDeclaration? Add(
            Node syntax, string kind, string scope, SourceLocation? containingType) =>
            syntax.GetChildForField("name") is { } name
                ? Add(syntax, name, kind, scope, containingType)
                : null;

        TypeScriptDeclaration? Add(
            Node syntax, Node identifier, string kind, string scope, SourceLocation? containingType)
        {
            // A computed name is an expression, not a reliable stable declaration identity.
            if (identifier.Type is "computed_property_name" or "array_pattern" or "object_pattern")
            {
                return null;
            }

            var name = identifier.Type == "string"
                ? identifier.Text.Trim('"', '\'')
                : identifier.Text;

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var location = TypeScriptSyntaxTree.Location(document.RelativePath, identifier);
            var qualifiedName = string.IsNullOrEmpty(scope) ? name : $"{scope}.{name}";
            var definition = new SymbolDefinition(
                "typescript", kind, name, qualifiedName, location, containingType, ReadSummary(syntax));

            var result = new TypeScriptDeclaration(syntax, definition);
            declarations.Add(result);
            return result;
        }
    }

    /// <summary>Finds JSDoc attached to an exported declaration or a direct class/interface member.</summary>
    public static Node? LeadingDocumentation(Node declaration)
    {
        var outer = declaration;

        while (outer.Parent is { Type: "export_statement" or "ambient_declaration" } parent)
        {
            outer = parent;
        }

        var previous = outer.PreviousNamedSibling;
        return previous is { Type: "comment" }
               && previous.Text.StartsWith("/**", StringComparison.Ordinal)
               && outer.StartPosition.Row - previous.EndPosition.Row <= 2
            ? previous
            : null;
    }

    private static string? ReadSummary(Node declaration)
    {
        var comment = LeadingDocumentation(declaration)?.Text;

        if (comment is null)
        {
            return null;
        }

        var lines = comment[3..^2]
            .Replace("\r", "", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim().TrimStart('*').Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('@'))
            .Take(10)
            .ToArray();

        return lines.Length == 0 ? null : string.Join(" ", lines);
    }
}
