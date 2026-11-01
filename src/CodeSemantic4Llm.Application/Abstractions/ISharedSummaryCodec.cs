using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Strict portable document format, independent of filesystem and SQLite storage.</summary>
public interface ISharedSummaryCodec
{
    SharedSummaryFile Decode(string markdown);
    string Encode(SharedSummaryFile file);
}
