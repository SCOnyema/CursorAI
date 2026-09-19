using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CursorAI.App.Input;

internal sealed class HotkeyService : IDisposable
{
    private const int ActivationHotkeyId = 0x4355;
    private const int CaptureHotkeyId = 0x4356;

    private readonly HwndSource _source;
    private readonly nint _windowHandle;
    private bool _isActivationRegistered;
    private bool _isCaptureRegistered;
    private bool _isDisposed;
    private bool _isHookInstalled;

    internal HotkeyService(HwndSource source)
    {
        _source = source;
        _windowHandle = source.Handle;

        try
        {
            NativeMethods.RegisterActivationHotkey(_windowHandle, ActivationHotkeyId);
            _isActivationRegistered = true;

            NativeMethods.RegisterCaptureHotkey(_windowHandle, CaptureHotkeyId);
            _isCaptureRegistered = true;

            _source.AddHook(WindowProcedure);
            _isHookInstalled = true;
        }
        catch
        {
            RollBackRegistrations();
            throw;
        }
    }

    internal event EventHandler? ActivationHotkeyPressed;

    internal event EventHandler? CaptureHotkeyPressed;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        List<string> failures = [];
        int firstError = 0;
        TryUnregister(CaptureHotkeyId, "Ctrl + Alt + Shift + S", ref _isCaptureRegistered, failures, ref firstError);
        TryUnregister(ActivationHotkeyId, "Ctrl + Alt + Space", ref _isActivationRegistered, failures, ref firstError);

        if (_isHookInstalled)
        {
            _source.RemoveHook(WindowProcedure);
            _isHookInstalled = false;
        }

        ActivationHotkeyPressed = null;
        CaptureHotkeyPressed = null;

        if (failures.Count > 0)
        {
            throw new Win32Exception(firstError, $"Could not unregister: {string.Join(", ", failures)}.");
        }
    }

    private void RollBackRegistrations()
    {
        RollBackRegistration(CaptureHotkeyId, "Ctrl + Alt + Shift + S", ref _isCaptureRegistered);
        RollBackRegistration(ActivationHotkeyId, "Ctrl + Alt + Space", ref _isActivationRegistered);
    }

    private void RollBackRegistration(int id, string name, ref bool isRegistered)
    {
        if (!isRegistered)
        {
            return;
        }

        if (!NativeMethods.UnregisterHotKey(_windowHandle, id))
        {
            Debug.WriteLine(
                $"Could not roll back {name} registration. Win32 error: {Marshal.GetLastPInvokeError()}.");
        }

        isRegistered = false;
    }

    private void TryUnregister(
        int id,
        string name,
        ref bool isRegistered,
        List<string> failures,
        ref int firstError)
    {
        if (!isRegistered)
        {
            return;
        }

        if (!NativeMethods.UnregisterHotKey(_windowHandle, id))
        {
            int error = Marshal.GetLastPInvokeError();
            firstError = firstError == 0 ? error : firstError;
            failures.Add(name);
        }

        isRegistered = false;
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message != NativeMethods.HotkeyMessage)
        {
            return 0;
        }

        if (wordParameter == ActivationHotkeyId)
        {
            ActivationHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (wordParameter == CaptureHotkeyId)
        {
            CaptureHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }
}
