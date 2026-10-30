namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Keeps body and full-declaration pagination separate; edits do not depend on read scope.</summary>
public enum MethodReadScope
{
    Body,
    Declaration
}
