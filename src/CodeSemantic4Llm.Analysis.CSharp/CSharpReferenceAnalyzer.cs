using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>
/// Builds an ephemeral Roslyn compilation and resolves method and type occurrences by symbol identity.
/// This first version treats the selected C# files as one compilation, without evaluating csproj files.
/// It intentionally omits unresolved/ambiguous references rather than guessing by identifier text.
/// </summary>
public sealed class CSharpReferenceAnalyzer : ISourceReferenceAnalyzer
{
    private static readonly Lazy<MetadataReference[]> FrameworkReferences = new(CreateFrameworkReferences);

    public string AnalyzerId => "dotnet-csharp";

    public Task<IReadOnlyList<SymbolReference>> AnalyzeAsync(
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken = default,
        IProgress<ReferenceAnalysisProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        cancellationToken.ThrowIfCancellationRequested();

        if (documents.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SymbolReference>>([]);
        }

        progress?.Report(new(ReferenceAnalysisPhase.Parsing, 0, documents.Count));
        var trees = new SyntaxTree[documents.Count];

        for (var index = 0; index < documents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = documents[index];
            trees[index] = CSharpSyntaxTree.ParseText(document.Content, path: document.RelativePath,
                cancellationToken: cancellationToken);
            progress?.Report(new(ReferenceAnalysisPhase.Parsing, index + 1, documents.Count));
        }

        progress?.Report(new(ReferenceAnalysisPhase.PreparingCompilation, 0, 0));
        var compilation = CSharpCompilation.Create(
            "CodeSemantic4Llm.ReferenceIndex",
            trees,
            FrameworkReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var references = new HashSet<SymbolReference>();

        progress?.Report(new(ReferenceAnalysisPhase.Resolving, 0, trees.Length));

        for (var index = 0; index < trees.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tree = trees[index];
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

            foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbol = ResolveSymbol(model, name, cancellationToken);

                if (symbol is null)
                {
                    continue;
                }

                var usage = Location(tree.FilePath, name.GetFirstToken());

                foreach (var declaration in symbol.DeclaringSyntaxReferences)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var token = DeclarationIdentifier(declaration.GetSyntax(cancellationToken));

                    if (token.RawKind == 0)
                    {
                        continue;
                    }

                    var target = Location(declaration.SyntaxTree.FilePath, token);

                    if (target != usage)
                    {
                        references.Add(new SymbolReference(target, usage));
                    }
                }
            }

            progress?.Report(new(ReferenceAnalysisPhase.Resolving, index + 1, trees.Length));
        }

        progress?.Report(new(ReferenceAnalysisPhase.Sorting, 0, 0));
        return Task.FromResult<IReadOnlyList<SymbolReference>>(references
            .OrderBy(reference => reference.Target.RelativePath, StringComparer.Ordinal)
            .ThenBy(reference => reference.Target.Line)
            .ThenBy(reference => reference.Target.Column)
            .ThenBy(reference => reference.Usage.RelativePath, StringComparer.Ordinal)
            .ThenBy(reference => reference.Usage.Line)
            .ThenBy(reference => reference.Usage.Column)
            .ToArray());
    }

    private static ISymbol? ResolveSymbol(SemanticModel model, SimpleNameSyntax name, CancellationToken cancellationToken)
    {
        // Alias usage can resolve to an alias symbol instead of the referenced type.
        var alias = name is IdentifierNameSyntax identifier ? model.GetAliasInfo(identifier, cancellationToken) : null;
        ISymbol? symbol = alias?.Target;

        if (symbol is null)
        {
            SyntaxNode expression = name;

            if (name.Parent is MemberAccessExpressionSyntax access && access.Name == name)
            {
                expression = access;
            }
            else if (name.Parent is MemberBindingExpressionSyntax binding && binding.Name == name)
            {
                expression = binding;
            }

            // The invocation selects a precise overload; a bare method group may be ambiguous.
            if (expression.Parent is InvocationExpressionSyntax invocation && invocation.Expression == expression)
            {
                expression = invocation;
            }

            symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
        }

        return symbol switch
        {
            IMethodSymbol method when method.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension
                => (method.ReducedFrom ?? method).OriginalDefinition,
            INamedTypeSymbol type => type.OriginalDefinition,
            _ => null
        };
    }

    private static SyntaxToken DeclarationIdentifier(SyntaxNode declaration) => declaration switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier,
        DelegateDeclarationSyntax type => type.Identifier,
        MethodDeclarationSyntax method => method.Identifier,
        _ => default
    };

    private static SourceLocation Location(string path, SyntaxToken identifier)
    {
        var position = identifier.GetLocation().GetLineSpan().StartLinePosition;
        return new SourceLocation(path, position.Line + 1, position.Character + 1);
    }

    private static MetadataReference[] CreateFrameworkReferences()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [typeof(object).Assembly.Location, typeof(Enumerable).Assembly.Location];

        return paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
