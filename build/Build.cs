using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed class Build : NukeBuild
{
    public static int Main() => Execute<Build>(build => build.Pack);

    [GitRepository] readonly GitRepository Repository = null!;

    AbsolutePath Solution => RootDirectory / "CodeSemantic4Llm.slnx";
    AbsolutePath Server => RootDirectory / "src/CodeSemantic4Llm.Mcp.Server/CodeSemantic4Llm.Mcp.Server.csproj";
    AbsolutePath Artifacts => RootDirectory / "artifacts";

    string PackageVersion
    {
        get
        {
            var version = XDocument.Load(Server).Descendants("Version").Single().Value;
            var branch = Environment.GetEnvironmentVariable("GITHUB_REF") switch
            {
                "refs/heads/main" => "main",
                "refs/heads/develop" => "develop",
                null => Repository.Branch,
                _ => "preview"
            };
            return branch switch
            {
                "main" => version,
                "develop" => $"{version}-beta-{Repository.Commit[..8]}",
                _ => $"{version}-alpha-{Repository.Commit[..8]}"
            };
        }
    }

    Target Restore => target => target.Executes(() =>
        DotNetRestore(settings => settings.SetProjectFile(Solution)));

    Target Compile => target => target.DependsOn(Restore).Executes(() =>
        DotNetBuild(settings => settings.SetProjectFile(Solution).SetConfiguration("Release")
            .SetProperty("Version", PackageVersion).EnableNoRestore()));

    Target Test => target => target.DependsOn(Compile).Executes(() =>
        DotNet($"test --solution \"{Solution}\" --configuration Release --no-build"));

    Target Pack => target => target.DependsOn(Test).Executes(() =>
    {
        DotNetPack(settings => settings.SetProject(Server).SetConfiguration("Release").EnableNoBuild()
            .SetVersion(PackageVersion).SetProperty("RepositoryCommit", Repository.Commit)
            .SetOutputDirectory(Artifacts));

        var githubOutput = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (githubOutput is not null)
            File.AppendAllText(githubOutput, $"version={PackageVersion}{Environment.NewLine}");
    });
}
