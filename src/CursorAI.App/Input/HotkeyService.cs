using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CursorAI.App.Input;

internal sealed class HotkeyService : IDisposable
{
    private const int ActivationHotkeyId = 0x4355;

    private readonly HwndSource _source;
    private readonly nint _windowHandle;
    private bool _isRegistered;

    internal HotkeyService(HwndSource source)
    {
        _source = source;
        _windowHandle = source.Handle;

        NativeMethods.RegisterActivationHotkey(_windowHandle, ActivationHotkeyId);
        _isRegistered = true;
        _source.AddHook(WindowProcedure);
    }

    internal event EventHandler? ActivationHotkeyPressed;

    public void Dispose()
    {
        if (!_isRegistered)
        {
            return;
        }

        bool unregistered = NativeMethods.UnregisterHotKey(_windowHandle, ActivationHotkeyId);
        int error = unregistered ? 0 : Marshal.GetLastPInvokeError();

        _source.RemoveHook(WindowProcedure);
        _isRegistered = false;

        if (!unregistered)
        {
            throw new Win32Exception(error, "Could not unregister the CursorAI activation hotkey.");
        }
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == NativeMethods.HotkeyMessage && wordParameter == ActivationHotkeyId)
        {
            ActivationHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }
}
