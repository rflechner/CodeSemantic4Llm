namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>
/// Removes temporary test trees, including the Git checkouts the indexing scenarios create.
/// Git stores loose objects and pack files as read-only, which Windows refuses to delete, and a
/// server process that has just been asked to stop may still hold a handle for a moment. Plain
/// <see cref="Directory.Delete(string, bool)"/> therefore fails on Windows for most scenarios.
/// </summary>
internal static class TemporaryDirectory
{
    private const int Attempts = 5;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Deletes a directory a scenario relies on having removed, for example to simulate a
    /// checkout that disappeared. Failures are reported because the arrangement did not happen.
    /// </summary>
    public static void Remove(string path)
    {
        if (!TryDelete(path, out var failure))
        {
            throw failure;
        }
    }

    /// <summary>
    /// Best-effort cleanup for teardown. A leaked directory under the system temporary folder is
    /// preferable to failing a scenario whose behavior was already verified.
    /// </summary>
    public static void TryRemove(string path) => TryDelete(path, out _);

    private static bool TryDelete(string path, out Exception failure)
    {
        failure = new DirectoryNotFoundException($"Temporary directory '{path}' does not exist.");

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            try
            {
                ClearReadOnlyAttributes(new DirectoryInfo(path));
                Directory.Delete(path, recursive: true);

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failure = exception;

                if (attempt < Attempts)
                {
                    Thread.Sleep(RetryDelay);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Clears the read-only flag Git sets on its object and pack files. The walk is explicit
    /// because recursive deletion removes a link itself, so descending into one would change
    /// attributes of files the scenario deliberately placed outside the temporary tree.
    /// </summary>
    private static void ClearReadOnlyAttributes(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            if (file.LinkTarget is null && file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if (child.LinkTarget is null)
            {
                ClearReadOnlyAttributes(child);
            }
        }
    }
}
