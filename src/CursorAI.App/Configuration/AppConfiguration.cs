namespace CursorAI.App.Configuration;

/// <summary>
/// Single place that resolves configuration, checking the environment first and stored user
/// settings second.
/// </summary>
/// <remarks>
/// The ordering is deliberate. Environment variables stay authoritative so an existing
/// development workflow keeps behaving exactly as before and a one-off override needs no UI,
/// while stored settings make the application usable by someone who never opens a terminal.
/// A value set in both places is not ambiguous: the environment wins, and the settings window
/// says so rather than appearing to have no effect.
/// </remarks>
public static class AppConfiguration
{
    private static readonly object Gate = new();
    private static UserSettings? _settings;

    /// <summary>Stored settings, loaded once and cached.</summary>
    public static UserSettings Settings
    {
        get
        {
            lock (Gate)
            {
                return _settings ??= SettingsStore.Default.Load();
            }
        }
    }

    /// <summary>Replaces the cached settings after a save, so changes apply without a restart.</summary>
    public static void Update(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (Gate)
        {
            _settings = settings;
        }
    }

    /// <summary>True when the named environment variable is set, so the UI can say it is in charge.</summary>
    public static bool IsOverriddenByEnvironment(string variable)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
    }

    /// <summary>Environment value if present, otherwise the stored value, otherwise null.</summary>
    public static string? Resolve(string variable, Func<UserSettings, string?> fromSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variable);
        ArgumentNullException.ThrowIfNull(fromSettings);

        string? fromEnvironment = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        string? stored = fromSettings(Settings);
        return string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
    }
}
