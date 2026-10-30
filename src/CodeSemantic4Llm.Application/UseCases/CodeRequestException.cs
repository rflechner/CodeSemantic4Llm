namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>A code request that the caller can correct without understanding storage internals.</summary>
public sealed class CodeRequestException(string message) : InvalidOperationException(message);
