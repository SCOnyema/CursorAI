using CursorAI.App.Models;

namespace CursorAI.App.State;

public sealed class BuddyStateChangedEventArgs : EventArgs
{
    public BuddyStateChangedEventArgs(BuddyState previousState, BuddyState currentState)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }

    public BuddyState PreviousState { get; }

    public BuddyState CurrentState { get; }
}
