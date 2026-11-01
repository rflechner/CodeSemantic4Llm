using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>
/// Reports whether this machine lets the test process create symbolic links. Windows grants that
/// privilege only to administrators or to accounts with Developer Mode enabled, so the scenarios
/// that verify link boundaries are skipped with an explicit reason instead of failing there.
/// </summary>
internal static class SymbolicLinks
{
    private static readonly Lazy<bool> Supported = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    public const string UnsupportedReason =
        "Creating symbolic links requires elevation or Developer Mode on this machine.";

    public static bool AreSupported => Supported.Value;

    /// <summary>Skips the current test when links cannot be created, keeping the reason visible.</summary>
    public static void SkipWhenUnsupported() => Assert.SkipUnless(AreSupported, UnsupportedReason);

    private static bool Probe()
    {
        var probeRoot = Path.Combine(
            Path.GetTempPath(), "CodeSemantic4Llm.LinkProbe", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(probeRoot, "target"));
            Directory.CreateSymbolicLink(Path.Combine(probeRoot, "link"), Path.Combine(probeRoot, "target"));

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            TemporaryDirectory.TryRemove(probeRoot);
        }
    }
}
