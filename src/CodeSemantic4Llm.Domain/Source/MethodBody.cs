namespace CodeSemantic4Llm.Domain.Source;

/// <summary>Transient source text for one method body. Never written to the index database.</summary>
public sealed record MethodBody(string Text, bool IsExpressionBodied);
