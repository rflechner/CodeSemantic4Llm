using System.Globalization;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace CodeSemantic4Llm.Mcp.Server.Dashboard;

/// <summary>Renders saved LLM Markdown for the dashboard without raw HTML or automatic resource loading.</summary>
public static class DashboardSummaryMarkdown
{
    // Enable only formatting extensions: generic attributes, embedded media and diagrams
    // can emit additional HTML and must not bypass the summary's rendering constraints.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    /// <summary>
    /// Returns an HTML fragment, or null when no summary exists. Images and unsupported links
    /// retain their labels; only HTTP(S) and mailto links remain clickable.
    /// </summary>
    public static string? Render(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Insert(0, new SummaryLinkRenderer { Rel = "noopener noreferrer" });
        renderer.ObjectRenderers.Insert(0, new SummaryAutolinkRenderer { Rel = "noopener noreferrer" });
        renderer.Render(Markdown.Parse(summary, Pipeline));
        return writer.ToString();
    }

    private static bool IsAllowedUrl(string? url) =>
        url is not null && !url.Any(char.IsControl)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "mailto";

    private sealed class SummaryLinkRenderer : LinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            if (link.IsImage || !IsAllowedUrl(link.Url))
            {
                renderer.WriteChildren(link);
                return;
            }

            base.Write(renderer, link);
        }
    }

    private sealed class SummaryAutolinkRenderer : AutolinkInlineRenderer
    {
        protected override void Write(HtmlRenderer renderer, AutolinkInline link)
        {
            if (!IsAllowedUrl(link.IsEmail ? "mailto:" + link.Url : link.Url))
            {
                renderer.WriteEscape(link.Url);
                return;
            }

            base.Write(renderer, link);
        }
    }
}
