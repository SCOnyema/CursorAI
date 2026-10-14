using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CursorAI.App.Models;
using CursorAI.App.Vision;

namespace CursorAI.App.Ui;

/// <summary>
/// The panel that makes CursorAI self-sufficient: answers, provider metadata, failures, and a
/// box to type a question into.
/// </summary>
/// <remarks>
/// Until this existed everything went to a console that only exists when launched from a
/// terminal, so someone running the executable directly saw a cursor-following eye that
/// occasionally spoke and reported nothing at all - including its own errors.
///
/// The typing box also addresses a measured problem rather than a hypothetical one. Open
/// dictation misheard "what about Michael and so" for a question about Kotter's change model,
/// and typing sidesteps that without giving up the local, quota-free, on-device speech stack.
///
/// Excluded from capture like the buddy and the guidance overlay: it is layered because of
/// AllowsTransparency, so CAPTUREBLT would otherwise place CursorAI's own answer inside the
/// next screenshot it takes.
/// </remarks>
public partial class ResponsePanelWindow : Window
{
    private static readonly Vector CursorOffset = new(26, 26);

    public ResponsePanelWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Raised when a question is typed and submitted.</summary>
    public event EventHandler<string>? QuestionSubmitted;

    /// <summary>
    /// Raised when the user asks to abandon the request in flight. Needed because a request can
    /// take over a minute on a throttled free tier, and without this the only way to stop one
    /// was to quit the application.
    /// </summary>
    public event EventHandler? CancelRequested;

    /// <summary>True while an activity owns the state, which is when cancelling is meaningful.</summary>
    private bool IsBusy { get; set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        nint handle = new WindowInteropHelper(this).Handle;

        // Deliberately NOT click-through: unlike the buddy and the overlay, this panel is
        // typed into and scrolled.
        if (!NativeMethods.TryExcludeFromCapture(handle))
        {
            Console.Error.WriteLine(
                "Warning: the response panel could not be excluded from screen capture, so its "
                + "contents may appear in later captures.");
        }
    }

    /// <summary>Shows an answer and the provider metadata beneath it.</summary>
    public void ShowAnswer(string answer, string metadata)
    {
        BodyText.Foreground = Brushes.White;
        BodyText.Text = string.IsNullOrWhiteSpace(answer) ? "(no answer returned)" : answer.Trim();
        MetadataText.Text = metadata ?? string.Empty;
        MetadataText.Visibility = string.IsNullOrWhiteSpace(metadata)
            ? Visibility.Collapsed
            : Visibility.Visible;
        BodyScroller.ScrollToTop();
        ShowNearCursor(activate: false);
    }

    /// <summary>
    /// Shows a failure. Routed here as well as to the console, because a failure the user
    /// cannot see is the state this whole panel exists to end.
    /// </summary>
    public void ShowFailure(string message)
    {
        BodyText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
        BodyText.Text = message;
        MetadataText.Visibility = Visibility.Collapsed;
        BodyScroller.ScrollToTop();
        ShowNearCursor(activate: false);
    }

    /// <summary>Opens the panel focused for typing, and clears any previous input.</summary>
    public void ShowForTyping()
    {
        ShowNearCursor(activate: true);
        InputBox.Clear();
        InputBox.Focus();
        Keyboard.Focus(InputBox);
    }

    /// <summary>
    /// Provider shown while a request is in flight, so the moment the screen leaves the machine
    /// is visible rather than implied.
    /// </summary>
    public string ProviderDisplayName { get; set; } = "the vision provider";

    /// <summary>
    /// Reflects the current activity, so a long request does not look like a hang, and offers
    /// cancellation only while there is something to cancel.
    /// </summary>
    public void SetStatus(BuddyState state)
    {
        StatusText.Text = state switch
        {
            BuddyState.Listening => "Listening...",
            BuddyState.Thinking => $"Sending your screen to {ProviderDisplayName}...",
            BuddyState.Responding => "Speaking...",
            BuddyState.Guiding => "Showing you where",
            _ => "Idle"
        };

        IsBusy = state != BuddyState.Idle;
        CancelButton.Visibility = IsBusy ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = IsBusy ? "Esc to cancel" : "Esc to dismiss";
    }

    /// <summary>Reports that the request was abandoned, replacing the in-flight status.</summary>
    public void ShowCancelled()
    {
        BodyText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F));
        BodyText.Text =
            "Cancelled." + Environment.NewLine + Environment.NewLine
            + "Note that a request already sent may still count against your provider's daily "
            + "allowance, because cancelling stops CursorAI waiting rather than un-sending it.";
        MetadataText.Visibility = Visibility.Collapsed;
        ShowNearCursor(activate: false);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    /// <summary>
    /// Positions the panel beside the cursor, clamped inside the work area. Reuses
    /// <see cref="BuddyPositioning"/> so the panel and the buddy cannot drift apart in how they
    /// handle screen edges.
    /// </summary>
    private void ShowNearCursor(bool activate)
    {
        if (!IsVisible)
        {
            if (activate)
            {
                Show();
                Activate();
            }
            else
            {
                Show();
            }
        }
        else if (activate)
        {
            Activate();
        }

        // Height is content-driven, so measure before placing it.
        UpdateLayout();

        if (!NativeMethods.GetCursorPos(out NativeMethods.NativePoint cursor)
            || !NativeMethods.TryGetCursorWorkArea(cursor, out NativeMethods.NativeRect workArea)
            || PresentationSource.FromVisual(this) is not HwndSource { CompositionTarget: { } target })
        {
            return;
        }

        Matrix fromDevice = target.TransformFromDevice;
        Point cursorInDips = fromDevice.Transform(new Point(cursor.X, cursor.Y));
        Point workTopLeft = fromDevice.Transform(new Point(workArea.Left, workArea.Top));
        Point workBottomRight = fromDevice.Transform(new Point(workArea.Right, workArea.Bottom));

        Point placement = BuddyPositioning.Calculate(
            cursorInDips,
            new Rect(workTopLeft, workBottomRight),
            new Size(ActualWidth, ActualHeight),
            CursorOffset);

        Left = placement.X;
        Top = placement.Y;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;

        // Escape means the most useful thing available: stop the request if one is running,
        // otherwise get the panel out of the way.
        if (IsBusy)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dismiss();
        }
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        string question = InputBox.Text.Trim();
        if (question.Length == 0)
        {
            return;
        }

        InputBox.Clear();
        QuestionSubmitted?.Invoke(this, question);
    }

    private void InputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        InputHint.Visibility = InputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
