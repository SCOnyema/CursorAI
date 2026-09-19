using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CursorAI.App.Input;
using CursorAI.App.Models;
using CursorAI.App.State;

namespace CursorAI.App;

public partial class MainWindow : Window
{
    private const double CursorOffsetX = 16;
    private const double CursorOffsetY = 16;
    private static readonly TimeSpan TrackingInterval = TimeSpan.FromMilliseconds(16);

    private readonly BuddyStateManager _buddyStateManager;
    private readonly DispatcherTimer _cursorTrackingTimer;
    private HotkeyService? _hotkeyService;
    private Storyboard? _activeStateStoryboard;

    public MainWindow()
    {
        InitializeComponent();

        _buddyStateManager = ((App)Application.Current).BuddyStateManager;
        _buddyStateManager.StateChanged += BuddyStateManager_StateChanged;

        _cursorTrackingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TrackingInterval
        };
        _cursorTrackingTimer.Tick += CursorTrackingTimer_Tick;

        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        nint windowHandle = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableClickThrough(windowHandle);

        HwndSource source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("Could not access the buddy window's native message source.");
        _hotkeyService = new HotkeyService(source);
        _hotkeyService.ActivationHotkeyPressed += HotkeyService_ActivationHotkeyPressed;

        ApplyBuddyState(_buddyStateManager.CurrentState);
        UpdateBuddyPosition();
        _cursorTrackingTimer.Start();
    }

    private void HotkeyService_ActivationHotkeyPressed(object? sender, EventArgs e)
    {
        Console.WriteLine("CursorAI hotkey received: Ctrl + Alt + Space");

        BuddyState nextState = _buddyStateManager.CurrentState == BuddyState.Idle
            ? BuddyState.Listening
            : BuddyState.Idle;
        _buddyStateManager.SetState(nextState);
    }

    private void BuddyStateManager_StateChanged(object? sender, BuddyStateChangedEventArgs e)
    {
        Console.WriteLine($"State: {e.PreviousState} -> {e.CurrentState}");

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => ApplyBuddyState(e.CurrentState));
            return;
        }

        ApplyBuddyState(e.CurrentState);
    }

    private void ApplyBuddyState(BuddyState state)
    {
        ResetStateVisuals();

        switch (state)
        {
            case BuddyState.Idle:
                break;
            case BuddyState.Listening:
                StartStateVisual(ListeningIndicator, "ListeningStoryboard");
                break;
            case BuddyState.Thinking:
                StartStateVisual(ThinkingIndicator, "ThinkingStoryboard");
                break;
            case BuddyState.Responding:
                StartStateVisual(RespondingIndicator, "RespondingStoryboard");
                break;
            case BuddyState.Guiding:
                StartStateVisual(GuidingIndicator, "GuidingStoryboard");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown buddy state.");
        }
    }

    private void StartStateVisual(FrameworkElement indicator, string storyboardResourceKey)
    {
        indicator.Visibility = Visibility.Visible;
        _activeStateStoryboard = (Storyboard)FindResource(storyboardResourceKey);
        _activeStateStoryboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    private void ResetStateVisuals()
    {
        _activeStateStoryboard?.Remove(this);
        _activeStateStoryboard = null;

        ListeningIndicator.Visibility = Visibility.Collapsed;
        ThinkingIndicator.Visibility = Visibility.Collapsed;
        RespondingIndicator.Visibility = Visibility.Collapsed;
        GuidingIndicator.Visibility = Visibility.Collapsed;
    }

    private void CursorTrackingTimer_Tick(object? sender, EventArgs e)
    {
        UpdateBuddyPosition();
    }

    private void UpdateBuddyPosition()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.NativePoint cursorPosition)
            || !NativeMethods.TryGetCursorWorkArea(cursorPosition, out NativeMethods.NativeRect nativeWorkArea))
        {
            return;
        }

        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
        if (source?.CompositionTarget is not { } compositionTarget)
        {
            return;
        }

        Matrix fromDevice = compositionTarget.TransformFromDevice;
        Point cursorPositionInDips = fromDevice.Transform(new Point(cursorPosition.X, cursorPosition.Y));
        Point workAreaTopLeft = fromDevice.Transform(new Point(nativeWorkArea.Left, nativeWorkArea.Top));
        Point workAreaBottomRight = fromDevice.Transform(new Point(nativeWorkArea.Right, nativeWorkArea.Bottom));
        Rect workAreaInDips = new(workAreaTopLeft, workAreaBottomRight);

        Point buddyPosition = BuddyPositioning.Calculate(
            cursorPositionInDips,
            workAreaInDips,
            new Size(ActualWidth, ActualHeight),
            new Vector(CursorOffsetX, CursorOffsetY));

        Left = buddyPosition.X;
        Top = buddyPosition.Y;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _cursorTrackingTimer.Stop();
        _cursorTrackingTimer.Tick -= CursorTrackingTimer_Tick;
        _buddyStateManager.StateChanged -= BuddyStateManager_StateChanged;
        _activeStateStoryboard?.Remove(this);

        if (_hotkeyService is not null)
        {
            _hotkeyService.ActivationHotkeyPressed -= HotkeyService_ActivationHotkeyPressed;
            _hotkeyService.Dispose();
        }
    }
}
