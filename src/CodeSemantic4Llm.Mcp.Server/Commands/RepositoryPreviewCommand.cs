using CodeSemantic4Llm.Application.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Runs the same bounded discovery preview as MCP and displays it in an IDE or terminal.</summary>
public sealed class RepositoryPreviewCommand(
    PreviewRepositoryIndex previewRepositoryIndex,
    IAnsiConsole console) : AsyncCommand<RepositoryPreviewSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        RepositoryPreviewSettings settings,
        CancellationToken cancellationToken)
    {
        var preview = await previewRepositoryIndex.ExecuteAsync(
            settings.RepositoryRoot, settings.MaxFiles, cancellationToken);

        WritePreview(preview);
        return 0;
    }

    private void WritePreview(RepositoryIndexingPreview preview)
    {
        console.WriteLine($"Repository: {preview.RepositoryRoot}");
        console.WriteLine("Read-only discovery preview; no source content is read or indexed.");
        console.WriteLine($"Semantic description language: {preview.SemanticIndexingLanguage}");

        foreach (var analyzerId in preview.EnabledAnalyzers)
        {
            var extensions = string.Join(", ", preview.EffectiveFileExtensions[analyzerId]);
            console.WriteLine($"Analyzer: {analyzerId} ({extensions})");
        }

        console.WriteLine($"Exclusions: {string.Join(", ", preview.EffectiveExclusions)}");
        console.WriteLine();

        foreach (var file in preview.Files)
        {
            console.WriteLine($"{file.AnalyzerId}  {file.RelativePath}");
        }

        console.WriteLine();
        console.WriteLine($"Files shown: {preview.Files.Count}");
        console.WriteLine(preview.HasMore
            ? "More matching files are available; this preview reached its limit."
            : "All matching files are included.");
    }
}
