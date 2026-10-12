using CursorAI.App.Models;

namespace CursorAI.App.State;

/// <summary>
/// Owns the buddy's visual state.
/// </summary>
/// <remarks>
/// State has two kinds of writer, and conflating them was a real defect. Ambient changes
/// (the idle/listening toggle) are cosmetic and may be refused. Activities (a vision request,
/// and later a voice turn) take exclusive ownership for their duration.
///
/// Before this distinction existed, pressing the activation hotkey during a vision request
/// saw CurrentState != Idle and flipped to Idle, killing the Thinking animation while the HTTP
/// request was still in flight; the request's own cleanup then forced Idle unconditionally,
/// discarding whatever the user had selected. V0.4 voice adds a second concurrent activity,
/// which is why ownership is modelled now rather than patched at each call site.
/// </remarks>
public sealed class BuddyStateManager
{
    private readonly object _gate = new();
    private ActivityScope? _activity;

    public BuddyState CurrentState { get; private set; } = BuddyState.Idle;

    /// <summary>True while an activity owns the state and ambient changes are refused.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _activity is not null;
            }
        }
    }

    public event EventHandler<BuddyStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Requests an ambient state change. Returns false when an activity owns the state, so a
    /// cosmetic toggle cannot cancel the visual for work that is still running.
    /// </summary>
    public bool SetState(BuddyState state)
    {
        BuddyStateChangedEventArgs? change;

        lock (_gate)
        {
            if (_activity is not null)
            {
                return false;
            }

            change = Apply(state);
        }

        Raise(change);
        return true;
    }

    /// <summary>
    /// Takes exclusive ownership of the state for one activity, returning a scope whose disposal
    /// restores whatever state was current beforehand. Returns null when another activity
    /// already owns it, which is also how duplicate activation is rejected.
    /// </summary>
    public IDisposable? TryBeginActivity(BuddyState state)
    {
        BuddyStateChangedEventArgs? change;
        ActivityScope scope;

        lock (_gate)
        {
            if (_activity is not null)
            {
                return null;
            }

            scope = new ActivityScope(this, CurrentState);
            _activity = scope;
            change = Apply(state);
        }

        Raise(change);
        return scope;
    }

    private void EndActivity(ActivityScope scope)
    {
        BuddyStateChangedEventArgs? change;

        lock (_gate)
        {
            if (!ReferenceEquals(_activity, scope))
            {
                return;
            }

            _activity = null;
            change = Apply(scope.StateBeforeActivity);
        }

        Raise(change);
    }

    /// <summary>Applies a state change under the lock, returning the event to raise outside it.</summary>
    private BuddyStateChangedEventArgs? Apply(BuddyState state)
    {
        if (state == CurrentState)
        {
            return null;
        }

        BuddyState previousState = CurrentState;
        CurrentState = state;
        return new BuddyStateChangedEventArgs(previousState, state);
    }

    /// <summary>Raised outside the lock: subscribers run arbitrary code, including re-entry.</summary>
    private void Raise(BuddyStateChangedEventArgs? change)
    {
        if (change is not null)
        {
            StateChanged?.Invoke(this, change);
        }
    }

    private sealed class ActivityScope(BuddyStateManager manager, BuddyState stateBeforeActivity)
        : IDisposable
    {
        private bool _isDisposed;

        internal BuddyState StateBeforeActivity { get; } = stateBeforeActivity;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            manager.EndActivity(this);
        }
    }
}
