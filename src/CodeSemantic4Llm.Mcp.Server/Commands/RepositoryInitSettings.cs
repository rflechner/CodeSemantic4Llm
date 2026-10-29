using System.ComponentModel;
using CodeSemantic4Llm.Domain.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Declarative options for creating repository configuration.</summary>
public sealed class RepositoryInitSettings : RepositoryCommandSettings
{
    /// <summary>Captures occurrences so duplicate project names are rejected.</summary>
    [CommandOption("--name <project-name>")]
    [Description("Optional project display name (specify once).")]
    public string[] ProjectNames { get; init; } = [];

    /// <summary>Optional name written as projectName in the repository YAML.</summary>
    public string? ProjectName => ProjectNames.Length == 0 ? null : ProjectNames[0];

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

        if (LanguageTags.Length > 1)
        {
            return ValidationResult.Error("--lang may only be specified once.");
        }

        return ProjectNames.Length > 1
            ? ValidationResult.Error("--name may only be specified once.")
            : ValidationResult.Success();
    }
}
