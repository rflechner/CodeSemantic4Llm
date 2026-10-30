using System.Security.Cryptography;
using System.Text;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Reads original files in place, validating each path component before and after the read.</summary>
public sealed class RepositorySourceReader : IRepositorySourceReader
{
    public async Task<SourceFileSnapshot> ReadAsync(
        string repositoryRoot, string relativePath, CancellationToken cancellationToken = default)
    {
        var path = RepositorySourcePath.Validate(repositoryRoot, relativePath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        RepositorySourcePath.Validate(repositoryRoot, relativePath);

        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        return new SourceFileSnapshot(new SourceDocument(relativePath, content), hash);
    }

}
