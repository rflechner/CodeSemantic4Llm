using System.Globalization;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Application.UseCases;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>
/// Runs the same bounded discovery preview as MCP and displays it in an IDE or terminal.
/// The repository path is supplied by the local operator; no MCP session is started.
/// </summary>
public sealed class RepositoryPreviewCommand(PreviewRepositoryIndex previewRepositoryIndex)
{
    /// <summary>
    /// Accepts a repository path and optional --max-files limit. Returns 0 for success/help,
    /// 1 for invalid input or discovery errors, and 130 when canceled.
    /// </summary>
    public async Task<int> ExecuteAsync(
        string[] arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (arguments is ["--help"] or ["-h"])
        {
            await WriteUsageAsync(output);
            return 0;
        }

        try
        {
            var options = ParseArguments(arguments);
            var preview = await previewRepositoryIndex.ExecuteAsync(
                options.RepositoryRoot, options.MaxFiles, cancellationToken);

            await WritePreviewAsync(preview, output);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Repository preview canceled.");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException
            or RepositoryConfigurationException or IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"Repository preview failed: {exception.Message}");
            await WriteUsageAsync(error);
            return 1;
        }
    }

    private static PreviewOptions ParseArguments(string[] arguments)
    {
        if (arguments.Length == 0 || string.IsNullOrWhiteSpace(arguments[0]) || arguments[0].StartsWith('-'))
        {
            throw new ArgumentException("Expected a repository path.");
        }

        if (arguments.Length == 1)
        {
            return new PreviewOptions(arguments[0], PreviewRepositoryIndex.DefaultMaxFiles);
        }

        if (arguments.Length != 3 || arguments[1] != "--max-files")
        {
            throw new ArgumentException("Expected a repository path followed by an optional --max-files limit.");
        }

        if (!int.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out var maxFiles))
        {
            throw new ArgumentException("--max-files must be a positive integer.");
        }

        return new PreviewOptions(arguments[0], maxFiles);
    }

    private static async Task WritePreviewAsync(RepositoryIndexingPreview preview, TextWriter output)
    {
        await output.WriteLineAsync($"Repository: {preview.RepositoryRoot}");
        await output.WriteLineAsync("Read-only discovery preview; no source content is read or indexed.");
        await output.WriteLineAsync($"Semantic description language: {preview.SemanticIndexingLanguage}");

        foreach (var analyzerId in preview.EnabledAnalyzers)
        {
            var extensions = string.Join(", ", preview.EffectiveFileExtensions[analyzerId]);
            await output.WriteLineAsync($"Analyzer: {analyzerId} ({extensions})");
        }

        await output.WriteLineAsync($"Exclusions: {string.Join(", ", preview.EffectiveExclusions)}");
        await output.WriteLineAsync();

        foreach (var file in preview.Files)
        {
            await output.WriteLineAsync($"{file.AnalyzerId}  {file.RelativePath}");
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Files shown: {preview.Files.Count}");
        await output.WriteLineAsync(preview.HasMore
            ? "More matching files are available; this preview reached its limit."
            : "All matching files are included.");
    }

    private static Task WriteUsageAsync(TextWriter writer) => writer.WriteLineAsync(
        $"Usage: preview <repository-path> [--max-files <1-{PreviewRepositoryIndex.MaximumMaxFiles}>] " +
        $"(default: {PreviewRepositoryIndex.DefaultMaxFiles}). The repository must contain codesemantic4llm.yaml.");

    private sealed record PreviewOptions(string RepositoryRoot, int MaxFiles);
}
