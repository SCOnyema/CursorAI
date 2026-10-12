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
using CursorAI.App.Vision;

namespace CursorAI.App;

public partial class MainWindow : Window
{
    private const double CursorOffsetX = 16;
    private const double CursorOffsetY = 16;
    private const string DevelopmentVisionQuestion = "What am I looking at, and what is under my cursor?";
    private static readonly TimeSpan TrackingInterval = TimeSpan.FromMilliseconds(16);

    private readonly BuddyStateManager _buddyStateManager;
    private readonly DispatcherTimer _cursorTrackingTimer;
    private readonly ScreenCaptureService _screenCaptureService = new();
    private readonly VisionService _visionService;
    private HotkeyService? _hotkeyService;
    private CancellationTokenSource? _visionCancellationSource;
    private Storyboard? _activeStateStoryboard;
    private bool _cleanupComplete;
    private volatile bool _isShuttingDown;

    public MainWindow()
    {
        InitializeComponent();

        _buddyStateManager = ((App)Application.Current).BuddyStateManager;
        _visionService = ((App)Application.Current).VisionService;

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

            if (!NativeMethods.TryExcludeFromCapture(windowHandle))
            {
                Console.Error.WriteLine(
                    "Warning: the buddy window could not be excluded from screen capture, so it "
                    + "may appear in captures and occlude what the cursor is pointing at.");
            }

            HwndSource source = HwndSource.FromHwnd(windowHandle)
                ?? throw new InvalidOperationException("Could not access the buddy window's native message source.");

            if (_hotkeyService is not null)
            {
                throw new InvalidOperationException("The CursorAI hotkeys are already initialized.");
            }

            _hotkeyService = new HotkeyService(source);
            _hotkeyService.ActivationHotkeyPressed += HotkeyService_ActivationHotkeyPressed;
            _hotkeyService.CaptureHotkeyPressed += HotkeyService_CaptureHotkeyPressed;
            _hotkeyService.VisionHotkeyPressed += HotkeyService_VisionHotkeyPressed;

            if (_hotkeyService.UnavailableHotkeys.Count > 0)
            {
                Console.Error.WriteLine(
                    "Warning: these CursorAI shortcuts are already owned by another application "
                    + "and will not respond:");
                foreach (string hotkey in _hotkeyService.UnavailableHotkeys)
                {
                    Console.Error.WriteLine($"  - {hotkey}");
                }
            }

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

        if (!_buddyStateManager.SetState(nextState))
        {
            Console.WriteLine(
                "Idle/listening toggle ignored: a request is in flight and owns the buddy state.");
        }
    }

    private async void HotkeyService_CaptureHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        try
        {
            // The capture itself stays on the UI thread on purpose. ScreenCaptureService reads
            // GetCursorPos internally, so running it after a thread hop would sample the cursor
            // later than the keypress and drift the one coordinate this product is grounded on.
            // BitBlt costs a few milliseconds; PNG encoding a 1080p frame costs far more, so
            // only encoding and file I/O move off-thread. The frame is already Freeze()d, which
            // is what makes handing it across threads safe.
            CapturedFrame frame = _screenCaptureService.CaptureMonitorContainingCursor();
            string baseDirectory = Environment.CurrentDirectory;
            DevelopmentCaptureArtifacts artifacts = await Task.Run(
                () => DevelopmentCaptureStorage.Save(frame, baseDirectory));

            if (_isShuttingDown)
            {
                return;
            }

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

    private async void HotkeyService_VisionHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        // Ownership is taken before the capture so a second press during capture is rejected
        // as well, and so the scope restores whatever state was current beforehand.
        IDisposable? activity = _buddyStateManager.TryBeginActivity(BuddyState.Thinking);
        if (activity is null)
        {
            Console.WriteLine("Vision analysis is already running; duplicate activation ignored.");
            return;
        }

        CancellationTokenSource cancellationSource = new();
        _visionCancellationSource = cancellationSource;
        Stopwatch elapsed = Stopwatch.StartNew();

        try
        {
            CapturedFrame frame = _screenCaptureService.CaptureMonitorContainingCursor();

            Console.WriteLine("Vision analysis started.");
            VisionResult result = await _visionService.AnalyzeAsync(
                DevelopmentVisionQuestion,
                frame,
                cancellationSource.Token);

            if (!_isShuttingDown)
            {
                Console.WriteLine($"Vision analysis completed in {elapsed.Elapsed.TotalSeconds:F1} seconds.");
                Console.WriteLine(DescribeResult(result));
                Console.WriteLine("Vision response:");
                Console.WriteLine(result.ResponseText);
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            Console.Error.WriteLine($"Vision analysis canceled after {elapsed.Elapsed.TotalSeconds:F1} seconds.");
            Console.Error.WriteLine("Cancellation source: application cancellation token");
            Console.Error.WriteLine("Application cancellation requested: true");
            Debug.WriteLine("Vision analysis was canceled.");
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"Vision analysis failed after {elapsed.Elapsed.TotalSeconds:F1} seconds.");
            Console.Error.WriteLine(exception.Message);
            Debug.WriteLine($"Vision analysis failed: {exception.GetType().Name}: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Vision analysis failed after {elapsed.Elapsed.TotalSeconds:F1} seconds.");
            Console.Error.WriteLine(
                $"Failure type: {exception.GetType().Name}. Check network connectivity, API access, rate limits/quota, and configuration.");
            Debug.WriteLine($"Vision analysis failed: {exception.GetType().Name}.");
        }
        finally
        {
            if (ReferenceEquals(_visionCancellationSource, cancellationSource))
            {
                _visionCancellationSource = null;
            }

            cancellationSource.Dispose();

            // Restores the state that was current before this request, rather than assuming Idle.
            activity.Dispose();
        }
    }

    /// <summary>
    /// Renders the metadata a provider reported. Absent values are omitted rather than shown as
    /// zero, because providers differ in what they report and zero would be a different claim.
    /// </summary>
    private static string DescribeResult(VisionResult result)
    {
        List<string> parts = [];

        if (!string.IsNullOrWhiteSpace(result.Model))
        {
            parts.Add($"model={result.Model}");
        }

        if (result.Latency is { } latency)
        {
            parts.Add($"latency={latency.TotalSeconds:F1}s");
        }

        if (result.Usage is { } usage)
        {
            if (usage.TotalTokens is { } total)
            {
                parts.Add($"tokens={total}");
            }

            if (usage.InputTokens is { } input)
            {
                parts.Add($"in={input}");
            }

            if (usage.OutputTokens is { } output)
            {
                parts.Add($"out={output}");
            }

            if (usage.ThoughtTokens is { } thoughts)
            {
                parts.Add($"thinking={thoughts}");
            }
        }

        return parts.Count == 0
            ? "No provider metadata reported."
            : string.Join("  ", parts);
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
        _visionCancellationSource?.Cancel();

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
        _hotkeyService.VisionHotkeyPressed -= HotkeyService_VisionHotkeyPressed;

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
