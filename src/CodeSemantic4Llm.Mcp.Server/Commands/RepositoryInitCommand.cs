using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Configuration;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Creates repository YAML from a terminal or IDE without starting an MCP session.</summary>
public sealed class RepositoryInitCommand(InitializeRepositoryConfiguration initializeRepositoryConfiguration)
{
    /// <summary>
    /// Accepts a repository path, an optional --lang tag, and repeatable --analyzer IDs.
    /// Options may appear in any order after the path. Returns 0 for success/help,
    /// 1 for invalid input or filesystem errors, and 130 when canceled.
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
            var configurationPath = await initializeRepositoryConfiguration.ExecuteAsync(
                options.RepositoryRoot, options.Language, options.AnalyzerIds, cancellationToken);

            await output.WriteLineAsync($"Created repository configuration: {configurationPath}");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Repository initialization canceled.");
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException
            or RepositoryConfigurationException or IOException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"Repository initialization failed: {exception.Message}");
            await WriteUsageAsync(error);
            return 1;
        }
    }

    private static InitOptions ParseArguments(string[] arguments)
    {
        if (arguments.Length == 0 || string.IsNullOrWhiteSpace(arguments[0]) || arguments[0].StartsWith('-'))
        {
            throw new ArgumentException("Expected a repository path.");
        }

        var language = SemanticIndexingConfiguration.DefaultLanguage;
        var languageSpecified = false;
        var analyzerIds = new List<string>();

        for (var index = 1; index < arguments.Length; index += 2)
        {
            var option = arguments[index];

            if (option is not ("--lang" or "--analyzer"))
            {
                throw new ArgumentException($"Unknown option '{option}'. Expected --lang or --analyzer.");
            }

            var value = ReadOptionValue(arguments, index);

            if (option == "--analyzer")
            {
                analyzerIds.Add(value);
                continue;
            }

            if (languageSpecified)
            {
                throw new ArgumentException("--lang may only be specified once.");
            }

            language = value;
            languageSpecified = true;
        }

        return new InitOptions(arguments[0], language, analyzerIds.Count == 0 ? null : analyzerIds.AsReadOnly());
    }

    private static string ReadOptionValue(string[] arguments, int optionIndex)
    {
        if (optionIndex + 1 == arguments.Length || arguments[optionIndex + 1].StartsWith('-'))
        {
            throw new ArgumentException($"Expected a value for {arguments[optionIndex]}.");
        }

        return arguments[optionIndex + 1];
    }

    private static Task WriteUsageAsync(TextWriter writer) => writer.WriteLineAsync(
        "Usage: init <repository-path> [--lang <language-tag>] [--analyzer <analyzer-id>]... " +
        $"(language default: {SemanticIndexingConfiguration.DefaultLanguage}; analyzer default: dotnet-csharp). " +
        "Repeat --analyzer to select multiple installed analyzers. " +
        "Creates codesemantic4llm.yaml in an existing directory without overwriting it.");

    private sealed record InitOptions(string RepositoryRoot, string Language, IReadOnlyList<string>? AnalyzerIds);
}
