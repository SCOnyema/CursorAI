using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CursorAI.App.Capture;
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
    private readonly ScreenCaptureService _screenCaptureService = new();
    private HotkeyService? _hotkeyService;
    private Storyboard? _activeStateStoryboard;
    private bool _cleanupComplete;
    private volatile bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();

        _buddyStateManager = ((App)Application.Current).BuddyStateManager;

        _cursorTrackingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TrackingInterval
        };
        _cursorTrackingTimer.Tick += CursorTrackingTimer_Tick;

        _buddyStateManager.StateChanged += BuddyStateManager_StateChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= MainWindow_SourceInitialized;

        try
        {
            nint windowHandle = new WindowInteropHelper(this).Handle;
            NativeMethods.EnableClickThrough(windowHandle);

            HwndSource source = HwndSource.FromHwnd(windowHandle)
                ?? throw new InvalidOperationException("Could not access the buddy window's native message source.");

            if (_hotkeyService is not null)
            {
                throw new InvalidOperationException("The CursorAI hotkeys are already initialized.");
            }

            _hotkeyService = new HotkeyService(source);
            _hotkeyService.ActivationHotkeyPressed += HotkeyService_ActivationHotkeyPressed;
            _hotkeyService.CaptureHotkeyPressed += HotkeyService_CaptureHotkeyPressed;

            ApplyBuddyState(_buddyStateManager.CurrentState);
            UpdateBuddyPosition();
            _cursorTrackingTimer.Start();
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    private void HotkeyService_ActivationHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        BuddyState nextState = _buddyStateManager.CurrentState == BuddyState.Idle
            ? BuddyState.Listening
            : BuddyState.Idle;
        _buddyStateManager.SetState(nextState);
    }

    private void HotkeyService_CaptureHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        try
        {
            CapturedFrame frame = _screenCaptureService.CaptureMonitorContainingCursor();
            DevelopmentCaptureArtifacts artifacts = DevelopmentCaptureStorage.Save(frame, Environment.CurrentDirectory);
            CapturedMonitorInfo monitor = frame.Monitor;

            Console.WriteLine("Screen captured");
            Console.WriteLine(
                $"Monitor bounds: X={monitor.Bounds.X} Y={monitor.Bounds.Y} "
                + $"Width={monitor.Bounds.Width} Height={monitor.Bounds.Height}");
            Console.WriteLine(
                $"Work area: X={monitor.WorkArea.X} Y={monitor.WorkArea.Y} "
                + $"Width={monitor.WorkArea.Width} Height={monitor.WorkArea.Height}");
            Console.WriteLine($"Primary monitor: {monitor.IsPrimary}");
            Console.WriteLine(
                $"Cursor: screen=({frame.CursorScreenPosition.X},{frame.CursorScreenPosition.Y}) "
                + $"capture=({frame.CursorPositionInImage.X},{frame.CursorPositionInImage.Y})");
            Console.WriteLine(
                $"Foreground: title={frame.ForegroundWindow.Title ?? "<unavailable>"} "
                + $"process={frame.ForegroundWindow.ProcessName ?? "<unavailable>"}");
            Console.WriteLine($"Image: {frame.PixelWidth}x{frame.PixelHeight}");
            Console.WriteLine($"Saved PNG: {artifacts.PngPath}");
            Console.WriteLine($"Saved JSON: {artifacts.JsonPath}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Screen capture failed: {exception.Message}");
            Debug.WriteLine($"Screen capture failed: {exception}");
        }
    }

    private void BuddyStateManager_StateChanged(object? sender, BuddyStateChangedEventArgs e)
    {
        if (_isShuttingDown || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            BuddyState state = e.CurrentState;
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!_isShuttingDown)
                {
                    ApplyBuddyState(state);
                }
            });
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
        if (!_isShuttingDown)
        {
            UpdateBuddyPosition();
        }
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
        Cleanup();
    }

    private void Cleanup()
    {
        if (_cleanupComplete)
        {
            return;
        }

        _cleanupComplete = true;
        _isShuttingDown = true;

        Closed -= MainWindow_Closed;
        SourceInitialized -= MainWindow_SourceInitialized;
        _cursorTrackingTimer.Stop();
        _cursorTrackingTimer.Tick -= CursorTrackingTimer_Tick;
        _buddyStateManager.StateChanged -= BuddyStateManager_StateChanged;
        ResetStateVisuals();

        if (_hotkeyService is null)
        {
            return;
        }

        _hotkeyService.ActivationHotkeyPressed -= HotkeyService_ActivationHotkeyPressed;
        _hotkeyService.CaptureHotkeyPressed -= HotkeyService_CaptureHotkeyPressed;

        try
        {
            _hotkeyService.Dispose();
        }
        catch (Win32Exception exception)
        {
            Debug.WriteLine($"CursorAI hotkey cleanup failed: {exception}");
        }
        finally
        {
            _hotkeyService = null;
        }
    }
}
