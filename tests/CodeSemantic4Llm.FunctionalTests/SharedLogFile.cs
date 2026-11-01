namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Reads flushed logs without denying the live writer access on Windows.</summary>
internal static class SharedLogFile
{
    public static string ReadAllText(string path)
    {
        using var reader = OpenReader(path);
        return reader.ReadToEnd();
    }

    public static string[] ReadAllLines(string path)
    {
        using var reader = OpenReader(path);
        var lines = new List<string>();

        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    private static StreamReader OpenReader(string path) => new(new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
}
