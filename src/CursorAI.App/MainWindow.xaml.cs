using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CursorAI.App.Automation;
using CursorAI.App.Capture;
using CursorAI.App.Configuration;
using CursorAI.App.Input;
using CursorAI.App.Models;
using CursorAI.App.Overlay;
using CursorAI.App.Speech;
using CursorAI.App.Ui;
using CursorAI.App.State;
using CursorAI.App.Vision;

namespace CursorAI.App;

public partial class MainWindow : Window
{
    private const double CursorOffsetX = 16;
    private const double CursorOffsetY = 16;
    private const string DevelopmentVisionQuestion = "What am I looking at, and what is under my cursor?";
    private static readonly TimeSpan TrackingInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Longest a single utterance may run. With push-to-talk this is a ceiling on holding the
    /// key rather than a silence timeout, so it is longer than the legacy engine needed.
    /// </summary>
    private static readonly TimeSpan VoiceListenTimeout = TimeSpan.FromSeconds(20);

    private readonly BuddyStateManager _buddyStateManager;
    private readonly DispatcherTimer _cursorTrackingTimer;
    private readonly ScreenCaptureService _screenCaptureService = new();
    private readonly VisionService _visionService;
    private readonly LocalSpeechServices _speechServices;
    private readonly GuidanceOverlayWindow _guidanceOverlay = new();
    private readonly IUiElementSource _uiElements = new UiAutomationElementSource();
    private readonly ResponsePanelWindow _responsePanel = new();
    private HotkeyService? _hotkeyService;
    private CancellationTokenSource? _visionCancellationSource;
    private Storyboard? _activeStateStoryboard;
    private bool _cleanupComplete;
    private volatile bool _isShuttingDown;
    private bool _isSettingsOpen;

    public MainWindow()
    {
        InitializeComponent();

        _buddyStateManager = ((App)Application.Current).BuddyStateManager;
        _visionService = ((App)Application.Current).VisionService;
        _speechServices = ((App)Application.Current).SpeechServices;

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

            _hotkeyService = new HotkeyService(source, HotkeyConfiguration.FromEnvironment());
            _hotkeyService.ActivationHotkeyPressed += HotkeyService_ActivationHotkeyPressed;
            _hotkeyService.CaptureHotkeyPressed += HotkeyService_CaptureHotkeyPressed;
            _hotkeyService.VisionHotkeyPressed += HotkeyService_VisionHotkeyPressed;
            _hotkeyService.QuitHotkeyPressed += HotkeyService_QuitHotkeyPressed;
            _hotkeyService.TypeHotkeyPressed += HotkeyService_TypeHotkeyPressed;
            _hotkeyService.SettingsHotkeyPressed += HotkeyService_SettingsHotkeyPressed;
            _responsePanel.QuestionSubmitted += ResponsePanel_QuestionSubmitted;

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

            PromptForCredentialsIfMissing();
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    private async void HotkeyService_ActivationHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_speechServices.CanListen)
        {
            await RunVoiceTurnAsync();
            return;
        }

        // Without a recogniser the shortcut keeps its old cosmetic behaviour, so the buddy
        // still has a visible Listening state to look at during development.
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
        IBuddyActivity? activity = _buddyStateManager.TryBeginActivity(BuddyState.Thinking);
        if (activity is null)
        {
            Console.WriteLine("Vision analysis is already running; duplicate activation ignored.");
            return;
        }

        _guidanceOverlay.HideGuidance();

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
                requestTargets: true,
                cancellationSource.Token);

            if (!_isShuttingDown)
            {
                Console.WriteLine($"Vision analysis completed in {elapsed.Elapsed.TotalSeconds:F1} seconds.");
                Console.WriteLine(DescribeResult(result));
                Console.WriteLine("Vision response:");
                Console.WriteLine(result.ResponseText);
                _responsePanel.ShowAnswer(result.ResponseText, DescribeResult(result));
                ShowGuidance(activity, frame, result);
                await SpeakResponseAsync(activity, result, cancellationSource.Token);
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            Console.Error.WriteLine($"Vision analysis canceled after {elapsed.Elapsed.TotalSeconds:F1} seconds.");
            Console.Error.WriteLine("Cancellation source: application cancellation token");
            Console.Error.WriteLine("Application cancellation requested: true");
            Debug.WriteLine("Vision analysis was canceled.");
        }
        catch (Exception exception)
        {
            ReportFailure("Vision analysis failed", exception, elapsed.Elapsed);
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
    /// <summary>
    /// Draws the located elements and moves the buddy to Guiding. Does nothing when the
    /// provider reported no targets, which is the normal case for a plain description.
    /// </summary>
    private void ShowGuidance(IBuddyActivity activity, CapturedFrame frame, VisionResult result)
    {
        if (_isShuttingDown || result.Targets is not { Count: > 0 } targets)
        {
            return;
        }

        // Refine the model's estimates with real element bounds where the application
        // exposes them. Costs ~150ms against a ~66s request, and silently changes nothing on
        // windows that report no usable elements.
        IReadOnlyList<VisionTarget> refined = SnapToUiElements(frame, targets);

        Console.WriteLine($"Guidance targets: {string.Join(", ", refined.Select(t => t.Label))}");
        _guidanceOverlay.ShowGuidance(frame.Monitor, refined, frame.PixelWidth, frame.PixelHeight);
        activity.SetState(BuddyState.Guiding);
    }

    /// <summary>
    /// Snaps targets to UI Automation element bounds, reporting what it managed to refine.
    /// Any failure here falls back to the model's own coordinates rather than losing guidance.
    /// </summary>
    private IReadOnlyList<VisionTarget> SnapToUiElements(
        CapturedFrame frame,
        IReadOnlyList<VisionTarget> targets)
    {
        try
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            IReadOnlyList<UiElement> elements = _uiElements.GetElements(frame.ForegroundWindow.WindowHandle);
            IReadOnlyList<VisionTarget> refined = TargetSnapper.Snap(targets, elements, frame.Monitor.Bounds);

            int changed = 0;
            for (int i = 0; i < targets.Count && i < refined.Count; i++)
            {
                if (targets[i].Bounds != refined[i].Bounds)
                {
                    changed++;
                }
            }

            Console.WriteLine(
                $"UI Automation: {elements.Count} elements in {elapsed.ElapsedMilliseconds} ms, "
                + $"{changed} of {targets.Count} targets snapped to exact bounds."
                + (elements.Count == 0
                    ? " This window exposes no usable elements, so model coordinates are used."
                    : string.Empty));

            return refined;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"UI Automation refinement skipped ({exception.GetType().Name}); "
                + "using the model's coordinates.");
            Debug.WriteLine($"UI Automation refinement failed: {exception}");
            return targets;
        }
    }

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

    /// <summary>
    /// The only way to stop CursorAI from the keyboard. The buddy window is click-through with
    /// no taskbar entry or tray icon, so before this a running instance could only be ended
    /// through Task Manager - and a forgotten one locks the build output.
    /// </summary>
    private void HotkeyService_SettingsHotkeyPressed(object? sender, EventArgs e)
    {
        if (!_isShuttingDown)
        {
            ShowSettings();
        }
    }

    /// <summary>
    /// Opens settings modally. One at a time, because two windows editing the same stored file
    /// would have the last save win silently.
    /// </summary>
    private void ShowSettings()
    {
        if (_isSettingsOpen)
        {
            return;
        }

        _isSettingsOpen = true;
        try
        {
            SettingsWindow settings = new() { Owner = null };
            settings.ShowDialog();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not open settings: {exception.GetType().Name}: {exception.Message}");
            Debug.WriteLine($"Settings window failed: {exception}");
        }
        finally
        {
            _isSettingsOpen = false;
        }
    }

    /// <summary>
    /// On a first run with no credential anywhere, says so and opens settings. Otherwise the
    /// first thing a new user would see is an authentication failure they cannot act on.
    /// </summary>
    private void PromptForCredentialsIfMissing()
    {
        bool hasEnvironmentKey = AppConfiguration.IsOverriddenByEnvironment("GEMINI_API_KEY")
            || AppConfiguration.IsOverriddenByEnvironment("OPENAI_API_KEY");

        if (hasEnvironmentKey || AppConfiguration.Settings.HasAnyCredential)
        {
            return;
        }

        Console.WriteLine("No API key is configured yet; opening settings.");
        _responsePanel.ShowFailure(
            "No API key is configured yet." + Environment.NewLine + Environment.NewLine
            + "Paste one into the settings window that just opened. A free Gemini key can be "
            + "created at aistudio.google.com/apikey.");
        ShowSettings();
    }

    private void HotkeyService_TypeHotkeyPressed(object? sender, EventArgs e)
    {
        if (!_isShuttingDown)
        {
            _responsePanel.ShowForTyping();
        }
    }

    /// <summary>
    /// Runs a typed question through exactly the same pipeline as a spoken one, so typing is a
    /// different input rather than a different feature.
    /// </summary>
    private async void ResponsePanel_QuestionSubmitted(object? sender, string question)
    {
        if (_isShuttingDown || string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        IBuddyActivity? activity = _buddyStateManager.TryBeginActivity(BuddyState.Thinking);
        if (activity is null)
        {
            Console.WriteLine("Typed question ignored: a request is already in flight.");
            return;
        }

        _guidanceOverlay.HideGuidance();
        CancellationTokenSource cancellationSource = new();
        _visionCancellationSource = cancellationSource;
        Stopwatch elapsed = Stopwatch.StartNew();

        try
        {
            Console.WriteLine($"Typed: {question}");
            CapturedFrame frame = _screenCaptureService.CaptureMonitorContainingCursor();

            VisionResult result = await _visionService.AnalyzeAsync(
                question,
                frame,
                requestTargets: true,
                cancellationSource.Token);

            if (_isShuttingDown)
            {
                return;
            }

            Console.WriteLine($"Typed question completed in {elapsed.Elapsed.TotalSeconds:F1} seconds.");
            Console.WriteLine(DescribeResult(result));
            Console.WriteLine("Vision response:");
            Console.WriteLine(result.ResponseText);
            _responsePanel.ShowAnswer(result.ResponseText, DescribeResult(result));
            ShowGuidance(activity, frame, result);
            await SpeakResponseAsync(activity, result, cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Typed question canceled.");
        }
        catch (Exception exception)
        {
            ReportFailure("Typed question failed", exception, elapsed.Elapsed);
        }
        finally
        {
            if (ReferenceEquals(_visionCancellationSource, cancellationSource))
            {
                _visionCancellationSource = null;
            }

            cancellationSource.Dispose();
            activity.Dispose();
        }
    }

    /// <summary>
    /// Reports a failure to the console and to the panel. Console-only reporting is invisible
    /// unless CursorAI was launched from a terminal, which is how failures used to go unnoticed.
    /// </summary>
    private void ReportFailure(string what, Exception exception, TimeSpan elapsed)
    {
        string detail = exception is InvalidOperationException
            ? exception.Message
            : $"{exception.GetType().Name}. Check the microphone, network connectivity, API "
              + "access, and rate limits or quota.";

        Console.Error.WriteLine($"{what} after {elapsed.TotalSeconds:F1} seconds.");
        Console.Error.WriteLine(detail);
        Debug.WriteLine($"{what}: {exception}");

        if (!_isShuttingDown)
        {
            _responsePanel.ShowFailure($"{what}.{Environment.NewLine}{Environment.NewLine}{detail}");
        }
    }

    private void HotkeyService_QuitHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        Console.WriteLine("Quit requested; shutting down.");
        _visionCancellationSource?.Cancel();
        _speechServices.Synthesizer?.Cancel();
        Application.Current.Shutdown();
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
        _responsePanel.SetStatus(state);

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

    /// <summary>
    /// One spoken turn: listen, capture the screen, analyse it, then speak the answer.
    /// Listening, Thinking and Responding all happen inside a single activity so the buddy's
    /// visual advances without ambient writers being able to interrupt it.
    /// </summary>
    private async Task RunVoiceTurnAsync()
    {
        IBuddyActivity? activity = _buddyStateManager.TryBeginActivity(BuddyState.Listening);
        if (activity is null)
        {
            Console.WriteLine("Voice turn ignored: a request is already in flight.");
            return;
        }

        // Guidance from the previous turn is stale the moment a new one starts.
        _guidanceOverlay.HideGuidance();

        CancellationTokenSource cancellationSource = new();
        _visionCancellationSource = cancellationSource;
        Stopwatch elapsed = Stopwatch.StartNew();

        try
        {
            // Push-to-talk: record while the Ask shortcut's key is still physically held.
            // RegisterHotKey reports the press but never the release, so the release has to be
            // polled. Whisper cannot stream, so it needs an explicit end to the utterance.
            uint talkKey = HotkeyConfiguration.FromEnvironment().Activation.VirtualKey;
            Console.WriteLine("Listening... (hold the Ask shortcut while speaking)");

            string? question = await _speechServices.Recognizer!.RecognizeOnceAsync(
                VoiceListenTimeout,
                () => NativeMethods.IsKeyDown(talkKey),
                cancellationSource.Token);

            if (string.IsNullOrWhiteSpace(question))
            {
                Console.WriteLine("Nothing was recognised; voice turn abandoned.");
                _responsePanel.ShowFailure(
                    "I did not catch that." + Environment.NewLine + Environment.NewLine
                    + "Try again, or type your question instead.");
                return;
            }

            Console.WriteLine($"Heard: {question}");

            // Capture only after the question is known, so the screen reflects what is being
            // asked about rather than whatever was showing when the hotkey was pressed.
            CapturedFrame frame = _screenCaptureService.CaptureMonitorContainingCursor();
            activity.SetState(BuddyState.Thinking);

            VisionResult result = await _visionService.AnalyzeAsync(
                question,
                frame,
                requestTargets: true,
                cancellationSource.Token);

            if (_isShuttingDown)
            {
                return;
            }

            Console.WriteLine($"Voice turn completed in {elapsed.Elapsed.TotalSeconds:F1} seconds.");
            Console.WriteLine(DescribeResult(result));
            Console.WriteLine("Vision response:");
            Console.WriteLine(result.ResponseText);
            _responsePanel.ShowAnswer(result.ResponseText, DescribeResult(result));

            // Guidance first, so the highlight is on screen while the answer is read out.
            ShowGuidance(activity, frame, result);
            await SpeakResponseAsync(activity, result, cancellationSource.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"Voice turn canceled after {elapsed.Elapsed.TotalSeconds:F1} seconds.");
        }
        catch (Exception exception)
        {
            ReportFailure("Voice turn failed", exception, elapsed.Elapsed);
        }
        finally
        {
            if (ReferenceEquals(_visionCancellationSource, cancellationSource))
            {
                _visionCancellationSource = null;
            }

            cancellationSource.Dispose();
            activity.Dispose();
        }
    }

    /// <summary>
    /// Speaks the answer, if this machine can. A missing synthesiser is not an error: the text
    /// has already been written to the console.
    /// </summary>
    private async Task SpeakResponseAsync(
        IBuddyActivity activity,
        VisionResult result,
        CancellationToken cancellationToken)
    {
        if (_speechServices.Synthesizer is not { } synthesizer)
        {
            return;
        }

        string spoken = SpokenResponseFormatter.Format(result.ResponseText);
        if (spoken.Length == 0)
        {
            return;
        }

        activity.SetState(BuddyState.Responding);
        await synthesizer.SpeakAsync(spoken, cancellationToken);
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
        _speechServices.Synthesizer?.Cancel();
        _guidanceOverlay.HideGuidance();
        _guidanceOverlay.Close();
        _responsePanel.Dismiss();
        _responsePanel.Close();

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
        _hotkeyService.QuitHotkeyPressed -= HotkeyService_QuitHotkeyPressed;
        _hotkeyService.TypeHotkeyPressed -= HotkeyService_TypeHotkeyPressed;
        _hotkeyService.SettingsHotkeyPressed -= HotkeyService_SettingsHotkeyPressed;
        _responsePanel.QuestionSubmitted -= ResponsePanel_QuestionSubmitted;

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
