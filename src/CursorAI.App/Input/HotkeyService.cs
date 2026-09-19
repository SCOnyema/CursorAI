using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CursorAI.App.Input;

internal sealed class HotkeyService : IDisposable
{
    private const int ActivationHotkeyId = 0x4355;

    private readonly HwndSource _source;
    private readonly nint _windowHandle;
    private bool _isDisposed;
    private bool _isHookInstalled;
    private bool _isRegistered;

    internal HotkeyService(HwndSource source)
    {
        _source = source;
        _windowHandle = source.Handle;

        NativeMethods.RegisterActivationHotkey(_windowHandle, ActivationHotkeyId);
        _isRegistered = true;

        try
        {
            _source.AddHook(WindowProcedure);
            _isHookInstalled = true;
        }
        catch
        {
            RollBackRegistration();
            throw;
        }
    }

    internal event EventHandler? ActivationHotkeyPressed;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        bool unregistered = !_isRegistered
            || NativeMethods.UnregisterHotKey(_windowHandle, ActivationHotkeyId);
        int unregisterError = unregistered ? 0 : Marshal.GetLastPInvokeError();
        _isRegistered = false;

        if (_isHookInstalled)
        {
            _source.RemoveHook(WindowProcedure);
            _isHookInstalled = false;
        }

        ActivationHotkeyPressed = null;

        if (!unregistered)
        {
            throw new Win32Exception(unregisterError, "Could not unregister the CursorAI activation hotkey.");
        }
    }

    private void RollBackRegistration()
    {
        if (!_isRegistered)
        {
            return;
        }

        if (!NativeMethods.UnregisterHotKey(_windowHandle, ActivationHotkeyId))
        {
            Debug.WriteLine(
                $"Could not roll back the CursorAI hotkey registration. Win32 error: {Marshal.GetLastPInvokeError()}.");
        }

        _isRegistered = false;
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
