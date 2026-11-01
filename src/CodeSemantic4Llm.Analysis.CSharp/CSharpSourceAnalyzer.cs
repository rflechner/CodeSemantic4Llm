using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>
/// Initial Roslyn-based declaration extractor. Syntax extraction is implemented;
/// semantic symbol resolution and reference discovery are future milestones.
/// </summary>
public sealed class CSharpSourceAnalyzer : ISourceAnalyzer
{
    public string AnalyzerId => "dotnet-csharp";

    public string LanguageId => "csharp";

    public SourceFileExtensions DefaultFileExtensions { get; } = new([".cs"]);

    public bool Supports(string relativePath) => DefaultFileExtensions.Matches(relativePath);

    public Task<IReadOnlyList<SymbolDefinition>> AnalyzeAsync(
        SourceDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var syntaxTree = CSharpSyntaxTree.ParseText(document.Content,
            CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose),
            cancellationToken: cancellationToken);
        var root = syntaxTree.GetRoot(cancellationToken);
        var symbols = new List<SymbolDefinition>();

        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var (name, identifier, kind) in GetDeclarations(member))
            {
                var ancestorNames = member.Ancestors()
                    .OfType<MemberDeclarationSyntax>()
                    .Reverse()
                    .Select(GetContainerName)
                    .OfType<string>();

                var qualifiedName = string.Join(".", ancestorNames.Append(name));
                var containingType = member.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();

                symbols.Add(new SymbolDefinition(
                    LanguageId,
                    kind,
                    name,
                    qualifiedName,
                    GetLocation(document.RelativePath, identifier),
                    containingType is null ? null : GetLocation(document.RelativePath, containingType.Identifier),
                    CSharpDocumentationSummary.Read(member)));
            }
        }

        return Task.FromResult<IReadOnlyList<SymbolDefinition>>(symbols);
    }

    private static SourceLocation GetLocation(string path, SyntaxToken identifier)
    {
        var position = identifier.GetLocation().GetLineSpan().StartLinePosition;
        return new SourceLocation(path, position.Line + 1, position.Character + 1);
    }

    private static string? GetContainerName(MemberDeclarationSyntax member) => member switch
    {
        BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Name.ToString(),
        TypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
        EnumDeclarationSyntax enumDeclaration => enumDeclaration.Identifier.ValueText,
        _ => null
    };

    private static IEnumerable<(string Name, SyntaxToken Identifier, string Kind)> GetDeclarations(
        MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                yield return (
                    namespaceDeclaration.Name.ToString(),
                    namespaceDeclaration.Name.GetFirstToken(),
                    "namespace");
                break;

            case TypeDeclarationSyntax typeDeclaration:
                yield return (
                    typeDeclaration.Identifier.ValueText,
                    typeDeclaration.Identifier,
                    typeDeclaration.Keyword.ValueText);
                break;

            case EnumDeclarationSyntax enumDeclaration:
                yield return (enumDeclaration.Identifier.ValueText, enumDeclaration.Identifier, "enum");
                break;

            case EnumMemberDeclarationSyntax enumMember:
                yield return (enumMember.Identifier.ValueText, enumMember.Identifier, "enum-member");
                break;

            case DelegateDeclarationSyntax delegateDeclaration:
                yield return (delegateDeclaration.Identifier.ValueText, delegateDeclaration.Identifier, "delegate");
                break;

            case MethodDeclarationSyntax method:
                yield return (method.Identifier.ValueText, method.Identifier, "method");
                break;

            case ConstructorDeclarationSyntax constructor:
                yield return (constructor.Identifier.ValueText, constructor.Identifier, "constructor");
                break;

            case PropertyDeclarationSyntax property:
                yield return (property.Identifier.ValueText, property.Identifier, "property");
                break;

            case EventDeclarationSyntax eventDeclaration:
                yield return (eventDeclaration.Identifier.ValueText, eventDeclaration.Identifier, "event");
                break;

            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    yield return (variable.Identifier.ValueText, variable.Identifier, "field");
                }
                break;

            case EventFieldDeclarationSyntax eventField:
                foreach (var variable in eventField.Declaration.Variables)
                {
                    yield return (variable.Identifier.ValueText, variable.Identifier, "event");
                }
                break;
        }
    }
}
