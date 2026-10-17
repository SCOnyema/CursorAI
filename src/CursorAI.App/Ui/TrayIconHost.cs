using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CursorAI.App.Input;

namespace CursorAI.App.Ui;

/// <summary>
/// The notification-area icon and menu: CursorAI's only persistent, discoverable surface.
/// </summary>
/// <remarks>
/// Without this, an installed build presents a cursor-following eye and nothing else. There is
/// no window, no taskbar entry and no menu, so nothing tells a new user that any shortcut
/// exists, and the only way to stop it was a shortcut they had no way to learn.
///
/// Built on WinForms' NotifyIcon rather than hand-rolled Shell_NotifyIcon interop. The raw API
/// needs a NOTIFYICONDATA structure, a dedicated message-only window and menu interop, because
/// the buddy window is WS_EX_NOACTIVATE and click-through and TrackPopupMenu misbehaves on it.
/// That is a lot of fragile surface for a tray icon, and WinForms runs on the WPF dispatcher's
/// message loop without trouble.
/// </remarks>
internal sealed class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private bool _isDisposed;

    internal TrayIconHost(HotkeyConfiguration hotkeys, TrayActions actions)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(actions);

        _menu = new ContextMenuStrip();
        Add(actions.Ask, "Ask by voice", hotkeys.Activation.DisplayName);
        Add(actions.Type, "Type a question", hotkeys.Type.DisplayName);
        Add(actions.Describe, "Explain this screen", hotkeys.Vision.DisplayName);
        Add(actions.Capture, "Save a screenshot (local only)", hotkeys.Capture.DisplayName);
        _menu.Items.Add(new ToolStripSeparator());
        Add(actions.Cancel, "Cancel the current request", null);
        Add(actions.NewConversation, "New conversation", null);
        _menu.Items.Add(new ToolStripSeparator());
        Add(actions.ShowShortcuts, "Shortcuts and status", null);
        Add(actions.OpenSettings, "Settings", hotkeys.Settings.DisplayName);
        _menu.Items.Add(new ToolStripSeparator());
        Add(actions.Quit, "Quit CursorAI", hotkeys.Quit.DisplayName);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "CursorAI",
            Visible = true,
            ContextMenuStrip = _menu
        };

        // Double-click is the conventional "do the main thing" gesture for a tray application.
        if (actions.Type is { } openTypeBox)
        {
            _icon.DoubleClick += (_, _) => openTypeBox();
        }
    }

    private void Add(Action? action, string label, string? shortcut)
    {
        if (action is null)
        {
            return;
        }

        string text = shortcut is null ? label : $"{label}   ({shortcut})";
        ToolStripMenuItem item = new(text);
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                // A menu click must never take the application down.
                Console.Error.WriteLine(
                    $"Tray action '{label}' failed: {exception.GetType().Name}: {exception.Message}");
                Debug.WriteLine($"Tray action failed: {exception}");
            }
        };
        _menu.Items.Add(item);
    }

    /// <summary>
    /// Loads the icon from embedded resources rather than from disk, because a single-file
    /// published build has no icon file sitting next to the executable.
    /// </summary>
    private static Icon LoadIcon()
    {
        Assembly assembly = typeof(TrayIconHost).Assembly;
        string? name = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith("cursorai.ico", StringComparison.OrdinalIgnoreCase));

        if (name is not null)
        {
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }

        // A generic icon is a poor outcome but far better than no tray icon at all, which would
        // leave the application with no discoverable surface again.
        Console.Error.WriteLine("Tray icon resource missing; using the system application icon.");
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // Hidden before disposal: a NotifyIcon disposed while visible can leave a dead icon in
        // the notification area until the user hovers over it.
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}

/// <summary>What the tray menu can do. Null entries are omitted from the menu.</summary>
internal sealed record TrayActions(
    Action? Ask = null,
    Action? Type = null,
    Action? Describe = null,
    Action? Capture = null,
    Action? Cancel = null,
    Action? NewConversation = null,
    Action? ShowShortcuts = null,
    Action? OpenSettings = null,
    Action? Quit = null);
