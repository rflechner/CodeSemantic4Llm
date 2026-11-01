using CodeSemantic4Llm.Domain.Configuration;
using Xunit;

namespace CodeSemantic4Llm.Domain.Tests;

public sealed class McpToolSelectionTests
{
    [Fact]
    public void Every_catalog_tool_is_selected_by_default()
    {
        Assert.Equal(McpToolCatalog.AllNames, McpToolSelection.All.EnabledToolNames);
    }

    [Fact]
    public void Configuration_without_a_selection_advertises_every_tool()
    {
        var configuration = new RepositoryIndexingConfiguration(["dotnet-csharp"]);

        Assert.Equal(McpToolCatalog.AllNames, configuration.Tools.EnabledToolNames);
    }

    [Fact]
    public void A_selection_narrows_the_advertised_tools()
    {
        var selection = new McpToolSelection(["find_symbols", "get_method"]);

        Assert.True(selection.IsEnabled("find_symbols"));
        Assert.True(selection.IsEnabled("get_method"));
        Assert.False(selection.IsEnabled("replace_method_body"));
        Assert.Equal(["find_symbols", "get_method"], selection.EnabledToolNames);
    }

    [Theory]
    [InlineData("  find_symbols  ")]
    [InlineData("FIND_SYMBOLS")]
    public void Tool_names_are_trimmed_and_lowercased(string configuredName)
    {
        var selection = new McpToolSelection([configuredName]);

        Assert.Equal(["find_symbols"], selection.EnabledToolNames);
    }

    [Fact]
    public void Repeated_tool_names_select_the_tool_once()
    {
        var selection = new McpToolSelection(["get_method", "GET_METHOD", "get_method"]);

        Assert.Equal(["get_method"], selection.EnabledToolNames);
    }

    [Fact]
    public void Selected_tools_keep_catalog_order()
    {
        var reversed = McpToolCatalog.AllNames.Reverse().ToArray();

        var selection = new McpToolSelection(reversed);

        Assert.Equal(McpToolCatalog.AllNames, selection.EnabledToolNames);
    }

    [Theory]
    [InlineData("find_symbol")]
    [InlineData("get_file_diagnostic")]
    [InlineData("dotnet-csharp")]
    public void An_unknown_tool_is_rejected_with_the_available_names(string unknownTool)
    {
        var error = Assert.Throws<ArgumentException>(() => new McpToolSelection([unknownTool]));

        Assert.Contains($"Unknown MCP tool '{unknownTool}'", error.Message);
        Assert.Contains("find_usages", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_tool_name_is_rejected(string toolName)
    {
        Assert.Throws<ArgumentException>(() => new McpToolSelection([toolName]));
    }

    [Fact]
    public void Selecting_no_tool_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => new McpToolSelection([]));

        Assert.Contains("At least one MCP tool must be selected", error.Message);
    }

    [Fact]
    public void Unknown_names_cannot_be_enabled_through_the_catalog()
    {
        Assert.Null(McpToolCatalog.Find("find_symbol"));
        Assert.NotNull(McpToolCatalog.Find("find_symbols"));
    }

    [Fact]
    public void Each_catalog_tool_is_declared_once_with_a_purpose()
    {
        Assert.Equal(McpToolCatalog.All.Count, McpToolCatalog.AllNames.Distinct(StringComparer.Ordinal).Count());
        Assert.All(McpToolCatalog.All, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Purpose)));
        Assert.All(McpToolCatalog.All, tool => Assert.Equal(tool.Name.ToLowerInvariant(), tool.Name));
    }

    [Fact]
    public void Writing_and_indexing_tools_are_declared_with_their_capability()
    {
        Assert.Equal(McpToolCapability.Writing, McpToolCatalog.Find("replace_method_body")!.Capability);
        Assert.Equal(McpToolCapability.Writing, McpToolCatalog.Find("replace_method")!.Capability);
        Assert.Equal(McpToolCapability.Indexing, McpToolCatalog.Find("preview_repository_index")!.Capability);
        Assert.Equal(McpToolCapability.Code, McpToolCatalog.Find("find_usages")!.Capability);
    }
}
