using System.Diagnostics;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Resolves a checkout root, named branch and optional HEAD revision without invoking hooks or builds.</summary>
public sealed class GitRepositoryStateProvider : IRepositoryStateProvider
{
    public async Task<RepositoryState> GetStateAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var prefix = await RunAsync(root, ["rev-parse", "--show-prefix"], cancellationToken);

        if (!prefix.Succeeded || prefix.Output.Length != 0)
        {
            throw new InvalidOperationException("Indexing requires the root of a Git working tree.");
        }

        var topLevel = await RunAsync(root, ["rev-parse", "--show-toplevel"], cancellationToken);
        var branch = await RunAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);

        if (!topLevel.Succeeded || !branch.Succeeded || string.IsNullOrWhiteSpace(branch.Output))
        {
            throw new InvalidOperationException("Indexing requires a named Git branch; detached HEAD is not supported yet.");
        }

        var revision = await RunAsync(root, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken);
        return new RepositoryState(new RepositoryScope(topLevel.Output, branch.Output),
            revision.Succeeded ? revision.Output : null);
    }

    private static async Task<GitResult> RunAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };

        foreach (var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY" })
        {
            startInfo.Environment.Remove(variable);
        }

        foreach (var argument in new[] { "--no-optional-locks", "-C", root }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new IOException("Unable to start Git.");

        // Git must not inherit the MCP transport's stdin. On Windows, probing that
        // pipe can block behind the server's pending read before Git even starts.
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await error;
            return new GitResult(process.ExitCode == 0, (await output).TrimEnd('\r', '\n'));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }

    private sealed record GitResult(bool Succeeded, string Output);
}
