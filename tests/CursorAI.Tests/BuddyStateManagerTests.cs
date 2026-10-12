using CursorAI.App.Models;
using CursorAI.App.State;

namespace CursorAI.Tests;

public class BuddyStateManagerTests
{
    [Fact]
    public void NewManager_StartsIdle()
    {
        BuddyStateManager manager = new();

        Assert.Equal(BuddyState.Idle, manager.CurrentState);
    }

    [Fact]
    public void SetState_UpdatesCurrentState()
    {
        BuddyStateManager manager = new();

        manager.SetState(BuddyState.Thinking);

        Assert.Equal(BuddyState.Thinking, manager.CurrentState);
    }

    [Fact]
    public void SetState_WhenStateChanges_NotifiesSubscribers()
    {
        BuddyStateManager manager = new();
        BuddyStateChangedEventArgs? notification = null;
        object? sender = null;
        manager.StateChanged += (eventSender, eventArgs) =>
        {
            sender = eventSender;
            notification = eventArgs;
        };

        manager.SetState(BuddyState.Listening);

        Assert.Same(manager, sender);
        Assert.NotNull(notification);
        Assert.Equal(BuddyState.Idle, notification.PreviousState);
        Assert.Equal(BuddyState.Listening, notification.CurrentState);
    }

    [Fact]
    public void SetState_WhenStateIsUnchanged_DoesNotNotifySubscribers()
    {
        BuddyStateManager manager = new();
        int notificationCount = 0;
        manager.StateChanged += (_, _) => notificationCount++;

        manager.SetState(BuddyState.Idle);

        Assert.Equal(BuddyState.Idle, manager.CurrentState);
        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void TryBeginActivity_TakesOwnershipAndRestoresPriorStateOnDispose()
    {
        BuddyStateManager manager = new();
        manager.SetState(BuddyState.Listening);

        IDisposable? activity = manager.TryBeginActivity(BuddyState.Thinking);

        Assert.NotNull(activity);
        Assert.True(manager.IsBusy);
        Assert.Equal(BuddyState.Thinking, manager.CurrentState);

        activity!.Dispose();

        // Restores Listening, not Idle: the previous behaviour discarded the user's choice.
        Assert.False(manager.IsBusy);
        Assert.Equal(BuddyState.Listening, manager.CurrentState);
    }

    [Fact]
    public void SetState_WhileAnActivityOwnsTheState_IsRefused()
    {
        // The concrete defect: pressing the activation hotkey during a vision request flipped
        // the state to Idle and killed the Thinking animation mid-flight.
        BuddyStateManager manager = new();
        using IDisposable activity = manager.TryBeginActivity(BuddyState.Thinking)!;

        bool accepted = manager.SetState(BuddyState.Idle);

        Assert.False(accepted);
        Assert.Equal(BuddyState.Thinking, manager.CurrentState);
    }

    [Fact]
    public void TryBeginActivity_WhileAnotherActivityIsRunning_ReturnsNull()
    {
        BuddyStateManager manager = new();
        using IDisposable first = manager.TryBeginActivity(BuddyState.Thinking)!;

        Assert.Null(manager.TryBeginActivity(BuddyState.Responding));
        Assert.Equal(BuddyState.Thinking, manager.CurrentState);
    }

    [Fact]
    public void ActivityScope_IsIdempotentAndDoesNotClobberALaterActivity()
    {
        BuddyStateManager manager = new();
        IDisposable first = manager.TryBeginActivity(BuddyState.Thinking)!;
        first.Dispose();
        first.Dispose();

        using IDisposable second = manager.TryBeginActivity(BuddyState.Guiding)!;
        first.Dispose();

        Assert.True(manager.IsBusy);
        Assert.Equal(BuddyState.Guiding, manager.CurrentState);
    }

    [Fact]
    public void TryBeginActivity_RaisesStateChangedForEntryAndExit()
    {
        BuddyStateManager manager = new();
        List<(BuddyState Previous, BuddyState Current)> changes = [];
        manager.StateChanged += (_, e) => changes.Add((e.PreviousState, e.CurrentState));

        manager.TryBeginActivity(BuddyState.Thinking)!.Dispose();

        Assert.Equal(
            [(BuddyState.Idle, BuddyState.Thinking), (BuddyState.Thinking, BuddyState.Idle)],
            changes);
    }
}
