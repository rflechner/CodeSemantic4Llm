using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Rejects an edit before writing, with syntax errors located in the proposed file.</summary>
public sealed class MethodEditValidationException : Exception
{
    public MethodEditValidationException(IReadOnlyList<SyntaxDiagnostic> diagnostics)
        : base("The proposed method edit contains syntax errors. No edit was applied.")
    {
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    /// <summary>Errors from the same source version and normalization used to prepare the edit.</summary>
    public IReadOnlyList<SyntaxDiagnostic> Diagnostics { get; }
}
