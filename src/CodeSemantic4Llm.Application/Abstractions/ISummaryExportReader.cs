using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Reads descriptions from one published generation with matching indexing configuration.</summary>
public interface ISummaryExportReader
{
    IReadOnlyList<SharedSummaryFile> Read(
        RepositoryState state, string configurationFingerprint, SummaryProfile profile,
        CancellationToken cancellationToken = default);
}
