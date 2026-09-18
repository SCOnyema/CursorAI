using CursorAI.App.Models;

namespace CursorAI.App.State;

public sealed class BuddyStateManager
{
    public BuddyState CurrentState { get; private set; } = BuddyState.Idle;

    public event EventHandler<BuddyStateChangedEventArgs>? StateChanged;

    public void SetState(BuddyState state)
    {
        if (state == CurrentState)
        {
            return;
        }

        BuddyState previousState = CurrentState;
        CurrentState = state;
        StateChanged?.Invoke(this, new BuddyStateChangedEventArgs(previousState, CurrentState));
    }
}
