// using System.ComponentModel;
// using CodeSemantic4Llm.Application.UseCases;
// using CodeSemantic4Llm.Domain.Source;
// using CodeSemantic4Llm.Domain.Symbols;
// using ModelContextProtocol.Server;
//
// namespace CodeSemantic4Llm.Mcp.Server.Tools;
//
// /// <summary>Exposes the currently supported non-persistent analysis operation.</summary>
// [McpServerToolType]
// public sealed class InspectSourceTools(AnalyzeSource analyzeSource)
// {
//     [McpServerTool, Description("Extracts C# declarations from a provided source snippet without saving or executing the code.")]
//     public Task<IReadOnlyList<SymbolDefinition>> InspectSourceAsync(
//         [Description("Relative filename, for example src/Customer.cs.")] string relativePath,
//         [Description("C# source code to inspect.")] string source,
//         CancellationToken cancellationToken = default)
//     {
//         return analyzeSource.ExecuteAsync(
//             new SourceDocument(relativePath, source),
//             cancellationToken);
//     }
// }
