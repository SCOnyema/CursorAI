using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CursorAI.App.Capture;
using CursorAI.App.Vision;

namespace CursorAI.App.Overlay;

/// <summary>
/// A transparent, click-through window covering one monitor, used to draw guidance over the
/// elements a vision provider located.
/// </summary>
/// <remarks>
/// Excluded from screen capture, for the same reason the buddy window is: it is layered
/// because of AllowsTransparency, so CAPTUREBLT would pull it into CursorAI's own screenshots.
/// Were that allowed, highlights drawn for one question would appear in the capture for the
/// next and the model would start describing its own guidance.
/// </remarks>
public partial class GuidanceOverlayWindow : Window
{
    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(12);

    private readonly DispatcherTimer _dismissTimer;
    private Storyboard? _pulse;

    public GuidanceOverlayWindow()
    {
        InitializeComponent();

        _dismissTimer = new DispatcherTimer { Interval = AutoDismissAfter };
        _dismissTimer.Tick += (_, _) => HideGuidance();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        nint handle = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableClickThrough(handle);

        if (!NativeMethods.TryExcludeFromCapture(handle))
        {
            Console.Error.WriteLine(
                "Warning: guidance overlay could not be excluded from screen capture, so "
                + "highlights may appear in later captures.");
        }
    }

    /// <summary>
    /// Draws the supplied targets over the monitor the capture came from.
    /// </summary>
    /// <param name="monitor">The monitor that was captured, in physical screen pixels.</param>
    /// <param name="targets">Targets positioned in screenshot pixels.</param>
    /// <param name="imagePixelWidth">Captured image width, used to clamp overreaching boxes.</param>
    /// <param name="imagePixelHeight">Captured image height.</param>
    public void ShowGuidance(
        CapturedMonitorInfo monitor,
        IReadOnlyList<VisionTarget> targets,
        int imagePixelWidth,
        int imagePixelHeight)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(targets);

        // Shown before measuring, because the device transform is only available once the
        // window has a presentation source, and it is per-monitor.
        if (!IsVisible)
        {
            Show();
        }

        if (PresentationSource.FromVisual(this) is not HwndSource { CompositionTarget: { } target })
        {
            return;
        }

        Matrix fromDevice = target.TransformFromDevice;
        Point topLeft = fromDevice.Transform(new Point(monitor.Bounds.X, monitor.Bounds.Y));
        Point bottomRight = fromDevice.Transform(new Point(
            monitor.Bounds.X + monitor.Bounds.Width,
            monitor.Bounds.Y + monitor.Bounds.Height));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = bottomRight.X - topLeft.X;
        Height = bottomRight.Y - topLeft.Y;

        // One physical pixel in device-independent units: 0.8 at 120 DPI.
        double dipsPerPixel = fromDevice.M11;

        TargetLayer.Children.Clear();
        int drawn = 0;
        foreach (VisionTarget visionTarget in targets)
        {
            if (OverlayGeometry.ClampToImage(visionTarget.Bounds, imagePixelWidth, imagePixelHeight)
                is not { } clamped)
            {
                continue;
            }

            Rect rect = OverlayGeometry.ToOverlayRect(clamped, dipsPerPixel);
            AddHighlight(rect, visionTarget.Label);
            drawn++;
        }

        if (drawn == 0)
        {
            HideGuidance();
            return;
        }

        _pulse = (Storyboard)FindResource("HighlightPulse");
        _pulse.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);

        _dismissTimer.Stop();
        _dismissTimer.Start();
    }

    /// <summary>Clears and hides the overlay. Safe to call when nothing is shown.</summary>
    public void HideGuidance()
    {
        _dismissTimer.Stop();

        if (_pulse is not null)
        {
            _pulse.Remove(this);
            _pulse = null;
        }

        TargetLayer.Children.Clear();
        TargetLayer.Opacity = 1;

        if (IsVisible)
        {
            Hide();
        }
    }

    private void AddHighlight(Rect rect, string label)
    {
        Rectangle box = new()
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = 4,
            RadiusY = 4,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x43)),
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x9F, 0x43))
        };
        Canvas.SetLeft(box, rect.X);
        Canvas.SetTop(box, rect.Y);
        TargetLayer.Children.Add(box);

        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        Border caption = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x25, 0x22, 0x38)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 3),
            Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            }
        };

        Canvas.SetLeft(caption, rect.X);
        // Above the box where there is room, otherwise just inside its top edge.
        Canvas.SetTop(caption, rect.Y >= 22 ? rect.Y - 22 : rect.Y + 2);
        TargetLayer.Children.Add(caption);
    }
}
