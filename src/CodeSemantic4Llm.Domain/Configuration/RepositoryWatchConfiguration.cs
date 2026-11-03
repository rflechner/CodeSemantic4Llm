namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>Validated per-repository settings for the continuous index watcher.</summary>
public sealed class RepositoryWatchConfiguration
{
    public const int DefaultDebounceSeconds = 15;
    public const int MinDebounceSeconds = 1;
    public const int MaxDebounceSeconds = 3600;

    /// <summary>Creates a quiet period applied after the most recent filesystem change.</summary>
    public RepositoryWatchConfiguration(int debounceSeconds = DefaultDebounceSeconds)
    {
        if (debounceSeconds is < MinDebounceSeconds or > MaxDebounceSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceSeconds), debounceSeconds,
                $"watch.debounceSeconds must be between {MinDebounceSeconds} and {MaxDebounceSeconds}.");
        }

        DebounceSeconds = debounceSeconds;
    }

    public int DebounceSeconds { get; }
}
