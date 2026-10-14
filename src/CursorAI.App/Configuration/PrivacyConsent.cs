namespace CursorAI.App.Configuration;

/// <summary>
/// Tracks whether the user has been told, and has agreed, that their screen leaves the machine.
/// </summary>
/// <remarks>
/// CursorAI's trust boundary has always been real in the architecture and described in its
/// documentation, but neither of those reaches someone who double-clicks an executable. Before
/// another person's screen is sent to a third party, they have to be shown what is sent, when
/// it is sent, and to whom, and be able to decline.
///
/// Declining is a supported state rather than an exit: local-only capture still works, so the
/// application remains useful without agreeing to anything leaving the machine.
///
/// The version exists so that a later change to what is sent requires fresh agreement rather
/// than silently inheriting the old one.
/// </remarks>
public static class PrivacyConsent
{
    /// <summary>
    /// Current disclosure version. Increment when what leaves the machine changes, which
    /// invalidates previously stored agreement and asks again.
    /// </summary>
    public const int CurrentVersion = 1;

    public static bool IsAccepted(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ConsentVersion == CurrentVersion;
    }

    /// <summary>
    /// What is sent, when, and to whom. Kept here rather than in the window so it can be
    /// asserted in tests and reused wherever the user needs reminding.
    /// </summary>
    public static string DisclosureText =>
        "CursorAI answers questions about your screen by sending a screenshot to a third-party "
        + "AI provider."
        + Environment.NewLine + Environment.NewLine
        + "What is sent, and only when you press Ask, Describe, or submit a typed question:"
        + Environment.NewLine
        + "  - a screenshot of the monitor your cursor is on"
        + Environment.NewLine
        + "  - your cursor position and the monitor's dimensions"
        + Environment.NewLine
        + "  - the name and window title of the application in front"
        + Environment.NewLine
        + "  - your question, as text"
        + Environment.NewLine + Environment.NewLine
        + "That screenshot may contain anything visible at the time, including messages, "
        + "documents, credentials and other people's information."
        + Environment.NewLine + Environment.NewLine
        + "What is never sent:"
        + Environment.NewLine
        + "  - audio; speech is recognised and spoken entirely on this machine"
        + Environment.NewLine
        + "  - anything at all unless you press a shortcut; there is no background or periodic "
        + "capture, and no screen recording"
        + Environment.NewLine + Environment.NewLine
        + "Requests are marked as not-for-retention, but this is a statement about what "
        + "CursorAI asks for, not a guarantee about the provider's own handling. Review your "
        + "provider's data-use terms before sending anything sensitive."
        + Environment.NewLine + Environment.NewLine
        + "If you decline, CursorAI keeps working for local-only screenshots and nothing is "
        + "sent anywhere.";
}
