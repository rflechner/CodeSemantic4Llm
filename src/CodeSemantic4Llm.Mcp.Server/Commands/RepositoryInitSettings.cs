using System.ComponentModel;
using CodeSemantic4Llm.Domain.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Declarative options for creating repository configuration.</summary>
public sealed class RepositoryInitSettings : RepositoryCommandSettings
{
    /// <summary>Captures occurrences so duplicate --lang options fail instead of silently selecting the last value.</summary>
    [CommandOption("--lang <language-tag>")]
    [Description("Language for future semantic descriptions (default: en; specify once).")]
    public string[] LanguageTags { get; init; } = [];

    /// <summary>Human language for future semantic descriptions, independent of source language.</summary>
    public string Language => LanguageTags.Length == 0 ? SemanticIndexingConfiguration.DefaultLanguage : LanguageTags[0];

    /// <summary>Repeatable analyzer selection; omission keeps the application's C# default.</summary>
    [CommandOption("--analyzer <analyzer-id>")]
    [Description("Select an installed analyzer; repeat for multiple analyzers (default: dotnet-csharp).")]
    public string[] AnalyzerIds { get; init; } = [];

    /// <inheritdoc />
    public override ValidationResult Validate()
    {
        var repositoryValidation = base.Validate();

        if (!repositoryValidation.Successful)
        {
            return repositoryValidation;
        }

        return LanguageTags.Length > 1
            ? ValidationResult.Error("--lang may only be specified once.")
            : ValidationResult.Success();
    }
}
