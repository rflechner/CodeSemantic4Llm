using System.Diagnostics;
using CodeSemantic4Llm.Application.Abstractions;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Reads the checked-out Git branch without checking out or modifying files.</summary>
public sealed class GitBranchProvider : IGitBranchProvider
{
    public async ValueTask<string> GetCurrentBranchAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(Path.GetFullPath(repositoryRoot));
        startInfo.ArgumentList.Add("symbolic-ref");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("--short");
        startInfo.ArgumentList.Add("HEAD");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Git process.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var branchName = (await outputTask).Trim();
        var error = (await errorTask).Trim();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(branchName))
        {
            throw new InvalidOperationException(
                $"Cannot resolve the checked-out Git branch (detached HEAD or non-Git directory). {error}");
        }

        return branchName;
    }
}
