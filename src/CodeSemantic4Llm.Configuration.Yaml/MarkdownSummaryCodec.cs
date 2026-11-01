using System.Text;
using System.Text.RegularExpressions;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeSemantic4Llm.Configuration.Yaml;

/// <summary>Version-one Markdown: YAML provenance and identities, followed by numbered summary sections.</summary>
public sealed class MarkdownSummaryCodec : ISharedSummaryCodec
{
    public const int MaximumCharacters = 4 * 1024 * 1024;
    private const string MarkerPrefix = "<!-- codesemantic4llm:summary:";
    private static readonly Regex HashPattern = new("^[A-F0-9]{64}$", RegexOptions.NonBacktracking);

    public SharedSummaryFile Decode(string markdown)
    {
        if (markdown.Length > MaximumCharacters)
        {
            throw new FormatException("Summary document exceeds the 4 MiB character limit.");
        }

        markdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (markdown.Split('\n').Any(line => line.StartsWith("<<<<<<<", StringComparison.Ordinal) ||
            line.StartsWith(">>>>>>>", StringComparison.Ordinal) || line.StartsWith("|||||||", StringComparison.Ordinal)))
        {
            throw new FormatException("Unresolved Git conflict markers in summary document.");
        }

        if (markdown.Length < 4)
        {
            throw new FormatException("Missing YAML front matter.");
        }

        var end = markdown.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (!markdown.StartsWith("---\n", StringComparison.Ordinal) || end < 0 || end > 512 * 1024)
        {
            throw new FormatException("Missing or oversized YAML front matter.");
        }

        Metadata metadata;
        try
        {
            metadata = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithDuplicateKeyChecking().Build().Deserialize<Metadata>(markdown[4..end]);
        }
        catch (YamlException exception)
        {
            throw new FormatException("Invalid summary front matter.", exception);
        }

        Validate(metadata);
        var body = markdown[(end + 5)..];
        var pieces = body.Split(MarkerPrefix, StringSplitOptions.None);
        if (!string.IsNullOrWhiteSpace(pieces[0]) || pieces.Length != metadata.Symbols.Count + 1)
        {
            throw new FormatException("Summary sections do not match the YAML symbol list.");
        }

        var summaries = new List<SharedSymbolSummary>();
        for (var index = 0; index < metadata.Symbols.Count; index++)
        {
            var symbol = metadata.Symbols[index];
            var prefix = index + " -->\n";
            if (!pieces[index + 1].StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new FormatException("Summary sections must have consecutive identifiers.");
            }

            var summary = pieces[index + 1][prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(summary) || summary.Length > 32768)
            {
                throw new FormatException("A summary must contain between 1 and 32768 characters.");
            }

            summaries.Add(new(symbol.QualifiedName, symbol.Kind, symbol.Line, symbol.Column, summary));
        }

        return new(new(metadata.Source, metadata.SourceHash, metadata.Analyzer, metadata.AnalyzerVersion),
            new(metadata.ModelHash, metadata.PromptHash, metadata.LanguageHash), summaries);
    }

    public string Encode(SharedSummaryFile file)
    {
        var metadata = new Metadata
        {
            SchemaVersion = 1, Source = file.Source.RelativePath, SourceHash = file.Source.ContentHash,
            Analyzer = file.Source.AnalyzerId, AnalyzerVersion = file.Source.AnalyzerVersion,
            ModelHash = file.Profile.ModelHash, PromptHash = file.Profile.PromptHash,
            LanguageHash = file.Profile.LanguageHash,
            Symbols = file.Symbols.Select(symbol => new SymbolMetadata
            {
                QualifiedName = symbol.QualifiedName, Kind = symbol.Kind, Line = symbol.Line, Column = symbol.Column
            }).ToList()
        };
        Validate(metadata);
        var yaml = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithIndentedSequences().Build().Serialize(metadata).Replace("\r\n", "\n", StringComparison.Ordinal);
        var text = new StringBuilder("---\n").Append(yaml).Append("---\n");
        for (var index = 0; index < file.Symbols.Count; index++)
        {
            var summary = file.Symbols[index].Summary.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            if (summary.Contains(MarkerPrefix, StringComparison.Ordinal))
            {
                throw new FormatException("Summary text contains a reserved section marker.");
            }

            text.Append('\n').Append(MarkerPrefix).Append(index).Append(" -->\n").Append(summary).Append('\n');
        }

        var result = text.ToString();
        Decode(result); // Enforce the same bounds for locally generated and imported documents.
        return result;
    }

    private static void Validate(Metadata? metadata)
    {
        if (metadata is null || metadata.SchemaVersion != 1 || string.IsNullOrWhiteSpace(metadata.Source) ||
            metadata.Source.Contains('\\') || metadata.Source.Contains(':') ||
            metadata.Source.Any(char.IsControl) ||
            metadata.Source.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or "..") ||
            string.IsNullOrWhiteSpace(metadata.Analyzer) || string.IsNullOrWhiteSpace(metadata.AnalyzerVersion) ||
            new[] { metadata.SourceHash, metadata.ModelHash, metadata.PromptHash, metadata.LanguageHash }
                .Any(hash => hash is null || !HashPattern.IsMatch(hash)) ||
            metadata.Symbols is null || metadata.Symbols.Count > 10000)
        {
            throw new FormatException("Invalid or unsupported summary metadata.");
        }

        var identities = new HashSet<(int, int)>();
        foreach (var symbol in metadata.Symbols)
        {
            if (symbol is null || string.IsNullOrWhiteSpace(symbol.QualifiedName) ||
                string.IsNullOrWhiteSpace(symbol.Kind) || symbol.Line <= 0 || symbol.Column <= 0 ||
                !identities.Add((symbol.Line, symbol.Column)))
            {
                throw new FormatException("Summary symbol identities must be valid and unique.");
            }
        }
    }

    private sealed class Metadata
    {
        public int SchemaVersion { get; set; }
        public string Source { get; set; } = "";
        public string SourceHash { get; set; } = "";
        public string Analyzer { get; set; } = "";
        public string AnalyzerVersion { get; set; } = "";
        public string ModelHash { get; set; } = "";
        public string PromptHash { get; set; } = "";
        public string LanguageHash { get; set; } = "";
        public List<SymbolMetadata> Symbols { get; set; } = [];
    }

    private sealed class SymbolMetadata
    {
        public string QualifiedName { get; set; } = "";
        public string Kind { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
