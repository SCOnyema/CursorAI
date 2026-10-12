using CursorAI.App.Models;

namespace CursorAI.App.State;

/// <summary>
/// Exclusive ownership of the buddy's visual state for the duration of one activity.
/// Disposing restores whatever state was current before the activity began.
/// </summary>
public interface IBuddyActivity : IDisposable
{
    /// <summary>
    /// Advances the visual while retaining ownership. Ignored once disposed, so a late
    /// continuation cannot repaint over a different activity.
    /// </summary>
    void SetState(BuddyState state);
}
