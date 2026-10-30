using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>
/// Reads, validates and edits current methods by source address, independently of the derived index.
/// Read observations retain only metadata and protect later writes against source and branch changes.
/// </summary>
public sealed class CurrentMethodOperations(
    IRepositoryConfigurationLoader configurationLoader,
    IEnumerable<ISourceAnalyzer> analyzers,
    IRepositoryStateProvider stateProvider,
    IRepositorySourceReader sourceReader,
    IRepositorySourceWriter sourceWriter,
    IMethodBodyEditor editor,
    IMethodSyntaxDiagnostics syntaxDiagnostics,
    MethodReadHistory reads,
    MethodBodyPages pages,
    RepositoryWriteAccess writeAccess)
{
    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers = analyzers.ToArray();

    /// <summary>Reads a stable body page and remembers its source version for a subsequent edit.</summary>
    public async Task<CurrentMethodBodyResult> ReadAsync(
        string root, DeclarationSelector selector, int maxCharacters, long offset, CancellationToken cancellationToken)
    {
        ValidatePage(maxCharacters, 0, 20000);
        var state = await stateProvider.GetStateAsync(root, cancellationToken);
        var page = pages.Select(state.Scope.RootPath, selector, offset);
        var selected = await SelectAsync(state.Scope.RootPath, selector, cancellationToken);
        var body = editor.Read(selected.Source.Document, selected.Method.Location);
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

        reads.Remember(observation);
        var start = Math.Min(page.Offset, body.Text.Length);
        var length = Math.Min(maxCharacters, body.Text.Length - start);
        return new CurrentMethodBodyResult(selected.Method,
            new MethodBody(body.Text.Substring(start, length), body.IsExpressionBodied), body.Text.Length,
            pages.Continue(page, observation, start + length < body.Text.Length ? start + length : null));
    }

    /// <summary>Checks proposed syntax against current source without creating an editing observation.</summary>
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

    /// <summary>Replaces one body only when the checkout and source still match the last body read.</summary>
    public async Task<SourceLocation> ReplaceAsync(
        string root, DeclarationSelector selector, string newBody, CancellationToken cancellationToken)
    {
        if (!writeAccess.Enabled)
        {
            throw new CodeRequestException("Source editing is disabled for this server.");
        }

        var state = await stateProvider.GetStateAsync(root, cancellationToken);
        var selected = await SelectAsync(state.Scope.RootPath, selector, cancellationToken);
        var observation = reads.Get(state.Scope.RootPath, selected.Method.Location);

        if (observation.State != state || observation.Method != selected.Method ||
            observation.ContentHash != selected.Source.ContentHash)
        {
            throw new CodeRequestException("The source changed since the last read. Read this method again before editing.");
        }

        var replacement = editor.Replace(selected.Source.Document, selected.Method.Location, newBody);

        if (replacement == selected.Source.Document.Content)
        {
            throw new CodeRequestException("The replacement is identical to the current method body.");
        }

        if (await stateProvider.GetStateAsync(root, cancellationToken) != state)
        {
            throw new CodeRequestException("The checkout changed during editing. Read the method again.");
        }

        await sourceWriter.ReplaceAsync(state.Scope.RootPath, selector.RelativePath,
            observation.ContentHash, replacement, cancellationToken);
        return selected.Method.Location;
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
}

/// <summary>A current method body page with no caller-managed version or identity tokens.</summary>
public sealed record CurrentMethodBodyResult(SymbolDefinition Method, MethodBody Body, int TotalCharacters, long? NextOffset);
