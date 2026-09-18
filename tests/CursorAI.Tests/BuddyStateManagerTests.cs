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
}
