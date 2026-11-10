using System.Net;
using System.Text.RegularExpressions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>A Razor declaration and its transient source excerpt for optional LLM summarization.</summary>
internal sealed record RazorDeclaration(SymbolDefinition Symbol, string? Source);

/// <summary>
/// Extracts Razor document metadata and C# members without treating HTML as C#.
/// A temporary Roslyn wrapper gives members a valid containing type; all positions are mapped
/// back to the original file and only derived declarations leave this parser.
/// </summary>
internal static class RazorDocumentParser
{
    private const string HostPrefix = "class __CodeSemanticRazorHost__ {\n";
    private const string HostSuffix = "\n}";

    private static readonly Regex MetadataDirective = new(
        @"(?m)^[ \t]*@(?<name>page|model|layout|inherits)[ \t]+(?<value>[^\r\n]+)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex NamedDirective = new(
        @"(?m)^[ \t]*@(?<kind>section|helper)[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Heading = new(
        @"<h[1-6]\b[^>]*>(?<text>.*?)</h[1-6]\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex Component = new(
        @"<(?<name>[A-Z][A-Za-z0-9_.]*)\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex HtmlTag = new(
        @"<[^>]*>", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Whitespace = new(
        @"\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    public static IReadOnlyList<RazorDeclaration> Parse(SourceDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var source = document.Content;
        var markup = source.ToCharArray();
        var blocks = ReadCodeBlocks(source, markup, cancellationToken);
        var visibleMarkup = new string(markup);
        var isComponent = string.Equals(Path.GetExtension(document.RelativePath), ".razor",
            StringComparison.OrdinalIgnoreCase);
        var kind = isComponent ? "component" : "view";
        var name = Path.GetFileNameWithoutExtension(document.RelativePath);
        var qualifiedName = Path.ChangeExtension(document.RelativePath, null)!
            .Replace('/', '.').Replace('\\', '.');
        var rootLocation = new SourceLocation(document.RelativePath, 1, 1);
        var root = new SymbolDefinition("razor", kind, name, qualifiedName, rootLocation,
            Summary: BuildSummary(visibleMarkup, isComponent));
        var declarations = new List<RazorDeclaration> { new(root, source) };
        var text = SourceText.From(source);

        foreach (Match match in NamedDirective.Matches(visibleMarkup))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identifier = match.Groups["name"];
            var location = Location(document.RelativePath, text, identifier.Index);
            var declared = new SymbolDefinition("razor", match.Groups["kind"].Value,
                identifier.Value, qualifiedName + "." + identifier.Value, location, rootLocation);
            declarations.Add(new RazorDeclaration(declared, null));
        }

        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCSharpDeclarations(document, text, root, block, declarations, cancellationToken);
        }

        return declarations;
    }

    private static void ReadCSharpDeclarations(SourceDocument document, SourceText text, SymbolDefinition root,
        RazorCodeBlock block, List<RazorDeclaration> declarations, CancellationToken cancellationToken)
    {
        var virtualSource = HostPrefix + document.Content.Substring(block.BodyStart, block.BodyLength) + HostSuffix;
        var syntaxRoot = CSharpSyntaxTree.ParseText(virtualSource,
                CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Diagnose),
                cancellationToken: cancellationToken)
            .GetRoot(cancellationToken);
        var host = syntaxRoot.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(type => type.Identifier.ValueText == "__CodeSemanticRazorHost__");

        if (host is null)
        {
            return;
        }

        foreach (var member in host.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var (name, identifier, kind) in CSharpSourceAnalyzer.GetDeclarations(member))
            {
                var location = MapLocation(identifier.SpanStart);

                if (location is null)
                {
                    continue;
                }

                var containers = member.Ancestors().OfType<MemberDeclarationSyntax>()
                    .Where(ancestor => ancestor != host)
                    .Reverse()
                    .Select(CSharpSourceAnalyzer.GetContainerName)
                    .OfType<string>();
                var qualifiedName = string.Join(".", new[] { root.QualifiedName }.Concat(containers).Append(name));
                var containingType = member.Ancestors().OfType<BaseTypeDeclarationSyntax>()
                    .FirstOrDefault(type => type != host);
                var parentLocation = containingType is null
                    ? root.Location
                    : MapLocation(containingType.Identifier.SpanStart) ?? root.Location;
                var symbol = new SymbolDefinition("csharp", kind, name, qualifiedName,
                    location, parentLocation, CSharpDocumentationSummary.Read(member));

                // Keep only the declaration, including its XML documentation, never the wrapper.
                var start = Math.Max(block.BodyStart, block.BodyStart + member.FullSpan.Start - HostPrefix.Length);
                var end = Math.Min(block.BodyStart + block.BodyLength,
                    block.BodyStart + member.Span.End - HostPrefix.Length);
                var snippet = end > start ? document.Content[start..end].Trim() : null;
                declarations.Add(new RazorDeclaration(symbol, snippet));
            }
        }

        SourceLocation? MapLocation(int position)
        {
            var offset = position - HostPrefix.Length;

            if (offset < 0 || offset >= block.BodyLength)
            {
                return null;
            }

            return Location(document.RelativePath, text, block.BodyStart + offset);
        }
    }

    private static SourceLocation Location(string path, SourceText text, int offset)
    {
        var position = text.Lines.GetLinePosition(offset);
        return new SourceLocation(path, position.Line + 1, position.Character + 1);
    }

    private static string BuildSummary(string markup, bool isComponent)
    {
        var parts = new List<string> { isComponent ? "Blazor component" : "Razor view" };

        foreach (Match directive in MetadataDirective.Matches(markup).Cast<Match>().Take(8))
        {
            var value = NormalizeText(directive.Groups["value"].Value).Trim('"', '\'');
            var label = directive.Groups["name"].Value switch
            {
                "page" => "Route",
                "model" => "Model",
                "layout" => "Layout",
                _ => "Base type"
            };

            if (value.Length > 0)
            {
                parts.Add(label + ": " + value[..Math.Min(value.Length, 120)]);
            }
        }

        var headings = Heading.Matches(markup).Cast<Match>()
            .Select(match => NormalizeText(WebUtility.HtmlDecode(HtmlTag.Replace(match.Groups["text"].Value, " "))))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(6).ToArray();

        if (headings.Length > 0)
        {
            parts.Add("Headings: " + string.Join(", ", headings.Select(value => value[..Math.Min(80, value.Length)])));
        }

        var components = Component.Matches(markup).Cast<Match>()
            .Select(match => match.Groups["name"].Value)
            .Where(name => name != "DOCTYPE")
            .Distinct(StringComparer.Ordinal)
            .Take(10).ToArray();

        if (components.Length > 0)
        {
            parts.Add("Uses components: " + string.Join(", ", components));
        }

        return string.Join(". ", parts) + ".";
    }

    private static string NormalizeText(string value) => Whitespace.Replace(value, " ").Trim();

    private static IReadOnlyList<RazorCodeBlock> ReadCodeBlocks(
        string source, char[] markup, CancellationToken cancellationToken)
    {
        var blocks = new List<RazorCodeBlock>();

        for (var index = 0; index < source.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (StartsWith(source, index, "@*"))
            {
                var end = source.IndexOf("*@", index + 2, StringComparison.Ordinal);
                var next = end < 0 ? source.Length : end + 2;
                Mask(markup, index, next);
                index = next;
                continue;
            }

            if (StartsWith(source, index, "<!--"))
            {
                var end = source.IndexOf("-->", index + 4, StringComparison.Ordinal);
                var next = end < 0 ? source.Length : end + 3;
                Mask(markup, index, next);
                index = next;
                continue;
            }

            // @{} is executable view code, not a declaration container. Ignore it for symbol
            // extraction and markup metadata, including any apparent Razor keywords in strings.
            if (StartsWith(source, index, "@{"))
            {
                var closing = FindClosingBrace(source, index + 1);
                var next = closing < 0 ? source.Length : closing + 1;
                Mask(markup, index, next);
                index = next;
                continue;
            }

            if (source[index] == '@' && (index == 0 || source[index - 1] != '@'))
            {
                var keywordLength = StartsWithKeyword(source, index, "@code") ? 5
                    : StartsWithKeyword(source, index, "@functions") ? 10 : 0;

                if (keywordLength > 0)
                {
                    var opening = index + keywordLength;

                    while (opening < source.Length && char.IsWhiteSpace(source[opening]))
                    {
                        opening++;
                    }

                    if (opening < source.Length && source[opening] == '{')
                    {
                        var closing = FindClosingBrace(source, opening);
                        var next = closing < 0 ? source.Length : closing + 1;

                        if (closing >= 0)
                        {
                            blocks.Add(new RazorCodeBlock(opening + 1, closing - opening - 1));
                        }

                        Mask(markup, index, next);
                        index = next;
                        continue;
                    }
                }
            }

            index++;
        }

        return blocks;
    }

    private static int FindClosingBrace(string source, int opening)
    {
        var depth = 0;

        for (var index = opening; index < source.Length; index++)
        {
            if (StartsWith(source, index, "//"))
            {
                var end = source.IndexOf('\n', index + 2);
                index = end < 0 ? source.Length : end;
                continue;
            }

            if (StartsWith(source, index, "/*"))
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 1;
                continue;
            }

            if (source[index] is '"' or '\'')
            {
                index = SkipString(source, index) - 1;
                continue;
            }

            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return index;
            }
            else if (source[index] != '}')
            {
                // No brace was consumed.
            }
        }

        return -1;
    }

    private static int SkipString(string source, int opening)
    {
        var quote = source[opening];
        var verbatim = quote == '"' && opening > 0 && source[opening - 1] == '@';
        var delimiterLength = 1;

        if (quote == '"')
        {
            while (opening + delimiterLength < source.Length && source[opening + delimiterLength] == '"')
            {
                delimiterLength++;
            }

            if (delimiterLength < 3)
            {
                delimiterLength = 1;
            }
        }

        for (var index = opening + delimiterLength; index < source.Length;)
        {
            if (delimiterLength >= 3)
            {
                if (index + delimiterLength <= source.Length
                    && source.AsSpan(index, delimiterLength).IndexOfAnyExcept('"') < 0)
                {
                    return index + delimiterLength;
                }

                index++;
            }
            else if (source[index] == '\\' && !verbatim)
            {
                index = Math.Min(source.Length, index + 2);
            }
            else if (source[index] == quote)
            {
                if (verbatim && index + 1 < source.Length && source[index + 1] == '"')
                {
                    index += 2;
                }
                else
                {
                    return index + 1;
                }
            }
            else
            {
                index++;
            }
        }

        return source.Length;
    }

    private static void Mask(char[] markup, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (markup[index] is not ('\r' or '\n'))
            {
                markup[index] = ' ';
            }
        }
    }

    private static bool StartsWith(string source, int index, string value) =>
        source.AsSpan(index).StartsWith(value, StringComparison.Ordinal);

    private static bool StartsWithKeyword(string source, int index, string value)
    {
        if (!StartsWith(source, index, value))
        {
            return false;
        }

        var after = index + value.Length;
        return after == source.Length || !char.IsLetterOrDigit(source[after]) && source[after] != '_';
    }

    private sealed record RazorCodeBlock(int BodyStart, int BodyLength);
}
