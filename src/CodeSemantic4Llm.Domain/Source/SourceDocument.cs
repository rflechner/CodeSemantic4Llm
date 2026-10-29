namespace CodeSemantic4Llm.Domain.Source;

/// <summary>
/// A transient source document provided to an analyzer. Source content must not be persisted in the index.
/// </summary>
public sealed class SourceDocument
{
    public SourceDocument(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        RelativePath = relativePath.Replace('\\', '/');
        Content = content;
    }

    public string RelativePath { get; }

    public string Content { get; }
}
