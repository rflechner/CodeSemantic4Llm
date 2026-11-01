using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Domain.Source;

/// <summary>Portable generation identity. Authentication and machine-specific paths never participate.</summary>
public sealed record SummaryProfile(string ModelHash, string PromptHash, string LanguageHash)
{
    public static SummaryProfile Create(SemanticIndexingConfiguration settings, string promptVersion) => new(
        Hash(JsonSerializer.Serialize(new
        {
            Url = settings.Llm.BaseUrl?.AbsoluteUri.TrimEnd('/'), settings.Llm.Model,
            settings.Llm.ReasoningEffort, settings.Llm.MaxCompletionTokens, settings.Llm.ForceGeneration
        })), Hash(promptVersion), Hash(settings.Language));

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Derived descriptions only; source declarations are always obtained from the checked-out files.</summary>
public sealed class SharedSummaryFile
{
    private readonly IReadOnlyDictionary<(string Name, string Kind, int Line, int Column), string> _descriptions;

    public SharedSummaryFile(IndexedSourceFile source, SummaryProfile profile, IReadOnlyList<SharedSymbolSummary> symbols)
    {
        Source = source;
        Profile = profile;
        Symbols = Array.AsReadOnly(symbols.ToArray());
        _descriptions = Symbols.ToDictionary(symbol => (symbol.QualifiedName, symbol.Kind, symbol.Line, symbol.Column),
            symbol => symbol.Summary);
    }

    public IndexedSourceFile Source { get; }
    public SummaryProfile Profile { get; }
    public IReadOnlyList<SharedSymbolSummary> Symbols { get; }

    public bool Matches(IndexedSourceFile source, SummaryProfile profile) =>
        Source.RelativePath == source.RelativePath && Source.ContentHash == source.ContentHash &&
        Source.AnalyzerId == source.AnalyzerId && Source.AnalyzerVersion == source.AnalyzerVersion && Profile == profile;

    public string? Find(SymbolDefinition symbol) => _descriptions.GetValueOrDefault(
        (symbol.QualifiedName, symbol.Kind, symbol.Location.Line, symbol.Location.Column));
}

/// <summary>Location disambiguates overloads and partial declarations under an exact source hash.</summary>
public sealed record SharedSymbolSummary(string QualifiedName, string Kind, int Line, int Column, string Summary);

/// <summary>The digest observes the complete Markdown, including rejected documents, for incremental invalidation.</summary>
public sealed record SharedSummaryRead(string Digest, SharedSummaryFile? File);
