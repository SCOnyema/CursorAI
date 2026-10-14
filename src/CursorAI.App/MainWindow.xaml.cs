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
    private bool _isConsentOpen;
    private TrayIconHost? _trayIcon;

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

            HotkeyConfiguration configuration = HotkeyConfiguration.FromEnvironment();
            _hotkeyService = new HotkeyService(source, configuration);
            _hotkeyService.ActivationHotkeyPressed += HotkeyService_ActivationHotkeyPressed;
            _hotkeyService.CaptureHotkeyPressed += HotkeyService_CaptureHotkeyPressed;
            _hotkeyService.VisionHotkeyPressed += HotkeyService_VisionHotkeyPressed;
            _hotkeyService.QuitHotkeyPressed += HotkeyService_QuitHotkeyPressed;
            _hotkeyService.TypeHotkeyPressed += HotkeyService_TypeHotkeyPressed;
            _hotkeyService.SettingsHotkeyPressed += HotkeyService_SettingsHotkeyPressed;
            _responsePanel.QuestionSubmitted += ResponsePanel_QuestionSubmitted;
            _responsePanel.CancelRequested += ResponsePanel_CancelRequested;
            _responsePanel.ProviderDisplayName = ((App)Application.Current).ProviderDisplayName;

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

            CreateTrayIcon(configuration);
            EnsureConsent();
            PromptForCredentialsIfMissing();
            ShowShortcuts();
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
        if (!CanSendScreen())
        {
            return;
        }

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
        catch (OperationCanceledException)
        {
            ReportCancelled("Vision analysis", elapsed.Elapsed);
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
    /// <summary>
    /// Creates the notification-area icon. Failing to create it is reported but not fatal: the
    /// shortcuts still work, and losing the tray icon should not lose the application.
    /// </summary>
    private void CreateTrayIcon(HotkeyConfiguration configuration)
    {
        try
        {
            _trayIcon = new TrayIconHost(
                configuration,
                new TrayActions(
                    Ask: () => HotkeyService_ActivationHotkeyPressed(this, EventArgs.Empty),
                    Type: () => HotkeyService_TypeHotkeyPressed(this, EventArgs.Empty),
                    Describe: () => HotkeyService_VisionHotkeyPressed(this, EventArgs.Empty),
                    Capture: () => HotkeyService_CaptureHotkeyPressed(this, EventArgs.Empty),
                    Cancel: CancelActiveRequest,
                    ShowShortcuts: ShowShortcuts,
                    OpenSettings: ShowSettings,
                    Quit: () => HotkeyService_QuitHotkeyPressed(this, EventArgs.Empty)));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Could not create the tray icon: {exception.GetType().Name}: {exception.Message}");
            Debug.WriteLine($"Tray icon failed: {exception}");
        }
    }

    /// <summary>
    /// Lists the shortcuts and the engines in use. Shown once at startup and from the tray,
    /// because nothing else tells a new user that any shortcut exists.
    /// </summary>
    private void ShowShortcuts()
    {
        if (_isShuttingDown)
        {
            return;
        }

        HotkeyConfiguration keys = HotkeyConfiguration.FromEnvironment();
        App app = (App)Application.Current;

        string unavailable = _hotkeyService?.UnavailableHotkeys.Count > 0
            ? Environment.NewLine + Environment.NewLine + "Unavailable (owned by another app): "
              + string.Join("; ", _hotkeyService.UnavailableHotkeys)
            : string.Empty;

        string body =
            $"{keys.Activation.DisplayName}  -  hold and speak, release to ask"
            + Environment.NewLine
            + $"{keys.Type.DisplayName}  -  type a question"
            + Environment.NewLine
            + $"{keys.Vision.DisplayName}  -  describe this screen"
            + Environment.NewLine
            + $"{keys.Capture.DisplayName}  -  save a screenshot, nothing sent"
            + Environment.NewLine
            + $"{keys.Settings.DisplayName}  -  settings"
            + Environment.NewLine
            + $"{keys.Quit.DisplayName}  -  quit"
            + unavailable;

        string engines = $"{app.ProviderDisplayName} - "
            + $"{_speechServices.Recognizer?.Description ?? "no speech input"} - "
            + $"{_speechServices.Synthesizer?.Description ?? "no speech output"}";

        _responsePanel.ShowAnswer(body, engines);
    }

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
    /// Shows the privacy disclosure when it has not been accepted for the current version, and
    /// returns whether sending is permitted. Called at startup and again before each external
    /// request, so a declined session can be reconsidered without restarting.
    /// </summary>
    private bool EnsureConsent()
    {
        if (PrivacyConsent.IsAccepted(AppConfiguration.Settings))
        {
            return true;
        }

        if (_isConsentOpen)
        {
            return false;
        }

        _isConsentOpen = true;
        try
        {
            ConsentWindow consent = new();
            consent.ShowDialog();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Could not show the privacy disclosure: {exception.GetType().Name}: {exception.Message}");
            Debug.WriteLine($"Consent window failed: {exception}");
            // Never fail open: without agreement, nothing is sent.
            return false;
        }
        finally
        {
            _isConsentOpen = false;
        }

        return PrivacyConsent.IsAccepted(AppConfiguration.Settings);
    }

    /// <summary>
    /// Gate for every path that sends the screen off the machine. Declining leaves local-only
    /// capture working, so this reports rather than disables the application.
    /// </summary>
    private bool CanSendScreen()
    {
        if (EnsureConsent())
        {
            return true;
        }

        Console.WriteLine("Request not sent: the privacy disclosure has not been accepted.");
        _responsePanel.ShowFailure(
            "Nothing was sent." + Environment.NewLine + Environment.NewLine
            + "CursorAI is in local-only mode because the privacy disclosure was declined. "
            + "Local screenshots still work. Press the settings shortcut and restart to be "
            + "asked again.");
        return false;
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
    private void ResponsePanel_CancelRequested(object? sender, EventArgs e)
    {
        CancelActiveRequest();
    }

    /// <summary>
    /// Abandons the request in flight, if any.
    /// </summary>
    /// <remarks>
    /// A request can take over a minute on a throttled free tier, and until this existed the
    /// only way to stop one was to quit the application. Cancelling stops CursorAI waiting; it
    /// cannot un-send a request that has already left, so the provider's daily allowance may
    /// still have been spent. The panel says so rather than implying a refund.
    /// </remarks>
    private void CancelActiveRequest()
    {
        CancellationTokenSource? active = _visionCancellationSource;

        if (active is null || active.IsCancellationRequested)
        {
            Console.WriteLine("Nothing to cancel.");
            return;
        }

        Console.WriteLine("Cancelling the request in flight.");

        // Only stop playback if something is actually being spoken: Piper's Cancel restarts the
        // process, which costs a model reload, so doing it mid-vision-call would be wasteful.
        if (_buddyStateManager.CurrentState == BuddyState.Responding)
        {
            _speechServices.Synthesizer?.Cancel();
        }

        try
        {
            active.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It completed between the null check and here, which is not a failure.
        }
    }

    private async void ResponsePanel_QuestionSubmitted(object? sender, string question)
    {
        if (_isShuttingDown || string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        if (!CanSendScreen())
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
            ReportCancelled("Typed question", elapsed.Elapsed);
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
    /// <summary>
    /// Reports an abandoned request. Separate from ReportFailure because cancelling is the user
    /// getting what they asked for, not something going wrong.
    /// </summary>
    private void ReportCancelled(string what, TimeSpan elapsed)
    {
        Console.WriteLine($"{what} cancelled after {elapsed.TotalSeconds:F1} seconds.");

        if (!_isShuttingDown)
        {
            _responsePanel.ShowCancelled();
        }
    }

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
        if (!CanSendScreen())
        {
            return;
        }

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
            ReportCancelled("Voice turn", elapsed.Elapsed);
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
        _trayIcon?.Dispose();

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
        _responsePanel.CancelRequested -= ResponsePanel_CancelRequested;

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
