using System.Diagnostics;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Local Git setup and operation checks. No shell, hooks, staging, commits or global configuration.</summary>
public sealed class GitSummaryIntegration
{
    public const string AttributeRule = ".codesemantic4llm/summaries/** merge=codesemantic-summary text eol=lf";
    public const string OtherDriver = "codesemantic4llm git-merge-summary other \"%O\" \"%A\" \"%B\"";
    public const string CurrentDriver = "codesemantic4llm git-merge-summary current \"%O\" \"%A\" \"%B\"";

    public async Task SetupAsync(string root, CancellationToken cancellationToken = default)
    {
        await new GitRepositoryStateProvider().GetStateAsync(root, cancellationToken);
        var attributes = RepositorySourcePath.Validate(root, ".gitattributes", allowMissing: true);
        var text = File.Exists(attributes) ? await File.ReadAllTextAsync(attributes, cancellationToken) : "";
        if (!text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Contains(AttributeRule))
        {
            await File.AppendAllTextAsync(attributes, (text.EndsWith('\n') || text.Length == 0 ? "" : "\n") +
                AttributeRule + "\n", cancellationToken);
        }

        await RunAsync(root, ["config", "--local", "merge.codesemantic-summary.name", "Shared summary: select complete other side"], cancellationToken);
        await RunAsync(root, ["config", "--local", "merge.codesemantic-summary.driver", OtherDriver], cancellationToken);
        await RunAsync(root, ["config", "--local", "merge.codesemantic-summary.recursive", "binary"], cancellationToken);
    }

    public async Task<bool> IsOperationInProgressAsync(string root, CancellationToken cancellationToken = default)
    {
        foreach (var name in new[] { "index.lock", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply" })
        {
            var gitPath = await RunAsync(root, ["rev-parse", "--git-path", name], cancellationToken);
            if (Path.Exists(Path.GetFullPath(gitPath, root)))
            {
                return true;
            }
        }

        return (await RunAsync(root, ["diff", "--name-only", "--diff-filter=U"], cancellationToken)).Length > 0;
    }

    private static async Task<string> RunAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path.GetFullPath(root), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY" })
        {
            start.Environment.Remove(variable);
        }

        start.ArgumentList.Add("--no-optional-locks");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("Unable to start Git.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                throw new IOException("Git failed: " + await error);
            }

            await error;
            return (await output).TrimEnd('\r', '\n');
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}
