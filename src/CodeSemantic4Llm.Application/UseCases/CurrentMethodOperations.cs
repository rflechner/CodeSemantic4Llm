using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>
/// Reads, validates and edits current methods by source address, independently of the derived index.
/// Each edit selects current source and checks for source and branch changes during that call.
/// </summary>
public sealed class CurrentMethodOperations(
    IRepositoryConfigurationLoader configurationLoader,
    IEnumerable<ISourceAnalyzer> analyzers,
    IRepositoryStateProvider stateProvider,
    IRepositorySourceReader sourceReader,
    IRepositorySourceWriter sourceWriter,
    IMethodBodyEditor editor,
    IMethodDeclarationEditor declarationEditor,
    IMethodSyntaxDiagnostics syntaxDiagnostics,
    MethodBodyPages pages,
    RepositoryWriteAccess writeAccess)
{
    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers = analyzers.ToArray();

    /// <summary>Reads a body page, retaining its source version only for pagination.</summary>
    public async Task<CurrentMethodBodyResult> ReadAsync(
        string root, DeclarationSelector selector, int maxCharacters, long offset, CancellationToken cancellationToken)
    {
        ValidatePage(maxCharacters, 0, 20000);
        var read = await ReadSelectionAsync(root, selector, offset, MethodReadScope.Body, cancellationToken);
        var body = editor.Read(read.Selected.Source.Document, read.Selected.Method.Location);
        var page = Slice(body.Text, read, maxCharacters);
        return new CurrentMethodBodyResult(read.Selected.Method,
            new MethodBody(page.Text, body.IsExpressionBodied), page.TotalCharacters, page.NextOffset);
    }

    /// <summary>Reads attributes, signature and body with source-consistent pagination.</summary>
    public async Task<CurrentMethodDeclarationResult> ReadDeclarationAsync(
        string root, DeclarationSelector selector, int maxCharacters, long offset, CancellationToken cancellationToken)
    {
        ValidatePage(maxCharacters, 0, 20000);
        var read = await ReadSelectionAsync(root, selector, offset, MethodReadScope.Declaration, cancellationToken);
        var text = declarationEditor.Read(read.Selected.Source.Document, read.Selected.Method.Location);
        var page = Slice(text, read, maxCharacters);
        return new CurrentMethodDeclarationResult(read.Selected.Method, page.Text, page.TotalCharacters, page.NextOffset);
    }

    private async Task<ObservedMethod> ReadSelectionAsync(
        string root, DeclarationSelector selector, long offset, MethodReadScope scope, CancellationToken cancellationToken)
    {
        var state = await stateProvider.GetStateAsync(root, cancellationToken);
        var page = pages.Select(state.Scope.RootPath, selector, offset, scope);
        var selected = await SelectAsync(state.Scope.RootPath, selector, cancellationToken);
        var finalState = await stateProvider.GetStateAsync(root, cancellationToken);

        if (state != finalState)
        {
            throw new CodeRequestException("The checkout changed during the read. Read the method again.");
        }

        var observation = new MethodReadObservation(state, selected.Method, selected.Source.ContentHash);

        if (page.Observation is not null && page.Observation != observation)
        {
            throw new CodeRequestException("The source changed while reading the method body. Start again at offset 0.");
        }

        return new ObservedMethod(selected, page, observation);
    }

    private MethodTextPage Slice(string text, ObservedMethod read, int maxCharacters)
    {
        var start = Math.Min(read.Continuation.Offset, text.Length);
        var length = Math.Min(maxCharacters, text.Length - start);
        return new MethodTextPage(text.Substring(start, length), text.Length,
            pages.Continue(read.Continuation, read.Observation, start + length < text.Length ? start + length : null));
    }

    /// <summary>Checks proposed syntax against current source without writing.</summary>
    public async Task<MethodDiagnosticsResult> ValidateAsync(
        string root, DeclarationSelector selector, string newBody, int maxDiagnostics, int offset,
        CancellationToken cancellationToken)
    {
        ValidatePage(maxDiagnostics, offset, 200);
        var selected = await SelectAsync(root, selector, cancellationToken);
        var diagnostics = syntaxDiagnostics.ValidateReplacement(selected.Source.Document, selected.Method.Location, newBody);
        return new MethodDiagnosticsResult(selected.Method.Location, selected.Source.ContentHash,
            diagnostics.All(item => item.Severity != "error"), diagnostics.Skip(offset).Take(maxDiagnostics).ToArray(),
            diagnostics.Count, diagnostics.Count - offset > maxDiagnostics ? checked(offset + maxDiagnostics) : null);
    }

    /// <summary>Validates and replaces the current body in one call, without requiring a preceding read.</summary>
    public Task<SourceLocation> ReplaceAsync(
        string root, DeclarationSelector selector, string newBody, CancellationToken cancellationToken) =>
        ReplaceSelectedAsync(root, selector,
            (document, location) => ValidateAndReplaceBody(document, location, newBody),
            cancellationToken);

    private MethodDeclarationEdit ValidateAndReplaceBody(SourceDocument document, SourceLocation location, string newBody)
    {
        var errors = syntaxDiagnostics.ValidateReplacement(document, location, newBody)
            .Where(diagnostic => diagnostic.Severity == "error").ToArray();

        if (errors.Length > 0)
        {
            throw new MethodEditValidationException(errors);
        }

        // Validation and replacement use the same source snapshot. The writer still rejects
        // concurrent file changes before publishing the replacement.
        return new MethodDeclarationEdit(editor.Replace(document, location, newBody), location);
    }

    /// <summary>Replaces the current complete declaration without requiring a preceding read.</summary>
    public Task<SourceLocation> ReplaceDeclarationAsync(
        string root, DeclarationSelector selector, string newMethod, CancellationToken cancellationToken) =>
        ReplaceSelectedAsync(root, selector,
            (document, location) => declarationEditor.Replace(document, location, newMethod), cancellationToken);

    private async Task<SourceLocation> ReplaceSelectedAsync(
        string root, DeclarationSelector selector,
        Func<SourceDocument, SourceLocation, MethodDeclarationEdit> replace, CancellationToken cancellationToken)
    {
        if (!writeAccess.Enabled)
        {
            throw new CodeRequestException("Source editing is disabled for this server.");
        }

        var state = await stateProvider.GetStateAsync(root, cancellationToken);
        var selected = await SelectAsync(state.Scope.RootPath, selector, cancellationToken);
        var replacement = replace(selected.Source.Document, selected.Method.Location);

        if (replacement.Content == selected.Source.Document.Content)
        {
            throw new CodeRequestException("The replacement is identical to the current method.");
        }

        if (await stateProvider.GetStateAsync(root, cancellationToken) != state)
        {
            throw new CodeRequestException("The checkout changed during editing. Retry when it is stable.");
        }

        await sourceWriter.ReplaceAsync(state.Scope.RootPath, selector.RelativePath,
            selected.Source.ContentHash, replacement.Content, cancellationToken);
        return replacement.MethodLocation;
    }

    private async Task<CurrentMethod> SelectAsync(
        string root, DeclarationSelector selector, CancellationToken cancellationToken)
    {
        var configuration = await configurationLoader.LoadAsync(root, cancellationToken);
        var analyzer = new ConfiguredSourceAnalyzers(configuration, _analyzers).Select(selector.RelativePath);

        if (analyzer?.Analyzer.LanguageId != "csharp")
        {
            throw new CodeRequestException("Select a configured C# source file.");
        }

        var source = await sourceReader.ReadAsync(root, selector.RelativePath, cancellationToken);
        var declarations = await analyzer.Analyzer.AnalyzeAsync(source.Document, cancellationToken);
        var matches = declarations.Where(symbol => symbol.Kind == "method" && selector.Matches(symbol)).Take(11).ToArray();

        if (matches.Length == 0)
        {
            throw new CodeRequestException("No matching method. Check its name and current source position.");
        }

        if (matches.Length > 1)
        {
            var positions = string.Join(", ", matches.Take(10).Select(item => $"{item.Location.Line}:{item.Location.Column}"));
            throw new CodeRequestException($"Several methods match. Specify line and column: {positions}.");
        }

        return new CurrentMethod(source, matches[0]);
    }

    private static void ValidatePage(int limit, int offset, int maximum)
    {
        if (limit < 1 || limit > maximum || offset < 0)
        {
            throw new ArgumentException($"Use a limit from 1 to {maximum} and a non-negative offset.");
        }
    }

    private sealed record CurrentMethod(SourceFileSnapshot Source, SymbolDefinition Method);

    private sealed record ObservedMethod(
        CurrentMethod Selected, MethodBodyContinuation Continuation, MethodReadObservation Observation);

    private sealed record MethodTextPage(string Text, int TotalCharacters, long? NextOffset);
}

/// <summary>A current method body page with no caller-managed version or identity tokens.</summary>
public sealed record CurrentMethodBodyResult(SymbolDefinition Method, MethodBody Body, int TotalCharacters, long? NextOffset);

/// <summary>A bounded current declaration, including attributes and signature but excluding surrounding comments.</summary>
public sealed record CurrentMethodDeclarationResult(SymbolDefinition Method, string Text, int TotalCharacters, long? NextOffset);
