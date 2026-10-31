using CodeSemantic4Llm.Mcp.Server.Dashboard;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Specifies Markdown formatting and the HTML boundary for untrusted saved summaries.</summary>
public sealed class DashboardSummaryMarkdownTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void Absent_summaries_have_no_rendered_fragment(string? summary) =>
        Assert.Null(DashboardSummaryMarkdown.Render(summary));

    [Fact]
    public void Formats_headings_lists_emphasis_quotes_and_code()
    {
        var html = DashboardSummaryMarkdown.Render("""
            ## Repository index
            **Publishes** a *branch* using `IndexRepository`.

            - Reads declarations
            - Saves references

            > Keeps the previous snapshot on failure.

            ```csharp
            return count < 10 && isReady;
            ```
            """);

        Assert.Contains("<h2>Repository index</h2>", html);
        Assert.Contains("<strong>Publishes</strong>", html);
        Assert.Contains("<em>branch</em>", html);
        Assert.Contains("<code>IndexRepository</code>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>Reads declarations</li>", html);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("<pre><code class=\"language-csharp\">", html);
        Assert.Contains("count &lt; 10 &amp;&amp; isReady;", html);
    }

    [Fact]
    public void Formats_tables_and_preserves_plain_summary_line_breaks()
    {
        var html = DashboardSummaryMarkdown.Render("""
            First line
            Second line

            | Input | Result |
            | --- | --- |
            | Branch | Snapshot |
            """);

        Assert.Contains("First line<br />", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<th>Input</th>", html);
        Assert.Contains("<td>Snapshot</td>", html);
    }

    [Fact]
    public void Raw_html_and_generic_attributes_cannot_create_active_elements()
    {
        var html = DashboardSummaryMarkdown.Render("""
            <script>alert('unsafe')</script>
            <img src="https://example.invalid/tracker" onerror="alert(1)">
            <iframe src="https://example.invalid"></iframe>

            **Description**{onclick="alert(1)"}
            """);

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<strong>Description</strong>", html);
    }

    [Theory]
    [InlineData("[Run](javascript:alert%281%29)")]
    [InlineData("[Run](JaVaScRiPt:alert%281%29)")]
    [InlineData("[Run](jav&#x61;script:alert%281%29)")]
    [InlineData("[Run](data:text/html;base64,PHNjcmlwdD4=)")]
    [InlineData("[Run](file:///tmp/source.cs)")]
    [InlineData("[Run](//example.invalid/path)")]
    [InlineData("[Run](relative/path)")]
    [InlineData("<javascript:alert(1)>")]
    public void Unsupported_urls_are_not_clickable(string markdown)
    {
        var html = DashboardSummaryMarkdown.Render(markdown);

        Assert.DoesNotContain("<a ", html);
        Assert.NotEmpty(html!);
    }

    [Theory]
    [InlineData("[Documentation](https://example.invalid/docs)", "https://example.invalid/docs")]
    [InlineData("<http://example.invalid/docs>", "http://example.invalid/docs")]
    [InlineData("<reader@example.invalid>", "mailto:reader@example.invalid")]
    public void Supported_links_remain_clickable(string markdown, string url)
    {
        var html = DashboardSummaryMarkdown.Render(markdown);

        Assert.Contains("href=\"" + url + "\"", html);
    }

    [Fact]
    public void Images_keep_their_labels_without_loading_remote_resources()
    {
        var html = DashboardSummaryMarkdown.Render("![Diagram](https://example.invalid/tracker.svg)");

        Assert.Contains("Diagram", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("src=", html);
        Assert.DoesNotContain("https://example.invalid", html);
    }
}
