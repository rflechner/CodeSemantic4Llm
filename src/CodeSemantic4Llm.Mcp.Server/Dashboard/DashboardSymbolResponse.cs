using CodeSemantic4Llm.Application.Abstractions;

namespace CodeSemantic4Llm.Mcp.Server.Dashboard;

/// <summary>A dashboard declaration with transient rendered Markdown; HTML never enters the index model.</summary>
internal sealed record DashboardSymbolResponse(
    string Language, string Kind, string Name, string QualifiedName,
    string Path, int Line, int Column, int? ContainingTypeLine, int? ContainingTypeColumn,
    string? Summary, string? SummaryHtml)
{
    public static DashboardSymbolResponse From(DashboardSymbol symbol) => new(
        symbol.Language, symbol.Kind, symbol.Name, symbol.QualifiedName,
        symbol.Path, symbol.Line, symbol.Column, symbol.ContainingTypeLine, symbol.ContainingTypeColumn,
        symbol.Summary, DashboardSummaryMarkdown.Render(symbol.Summary));
}
