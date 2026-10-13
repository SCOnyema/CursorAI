using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CursorAI.App.Input;

internal sealed class HotkeyService : IDisposable
{
    private const int ActivationHotkeyId = 0x4355;
    private const int CaptureHotkeyId = 0x4356;
    private const int VisionHotkeyId = 0x4357;
    private const int QuitHotkeyId = 0x4358;
    private const int TypeHotkeyId = 0x4359;

    private readonly HwndSource _source;
    private readonly nint _windowHandle;
    private bool _isActivationRegistered;
    private bool _isCaptureRegistered;
    private bool _isVisionRegistered;
    private bool _isQuitRegistered;
    private bool _isTypeRegistered;
    private bool _isDisposed;
    private bool _isHookInstalled;

    private readonly HotkeyConfiguration _configuration;

    internal HotkeyService(HwndSource source, HotkeyConfiguration configuration)
    {
        _source = source;
        _windowHandle = source.Handle;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        try
        {
            List<string> unavailable = [];

            _isActivationRegistered = Register(
                ActivationHotkeyId,
                _configuration.Activation,
                "idle/listening toggle",
                HotkeyConfiguration.ActivationVariable,
                unavailable);

            _isCaptureRegistered = Register(
                CaptureHotkeyId,
                _configuration.Capture,
                "local capture",
                HotkeyConfiguration.CaptureVariable,
                unavailable);

            _isVisionRegistered = Register(
                VisionHotkeyId,
                _configuration.Vision,
                "vision analysis",
                HotkeyConfiguration.VisionVariable,
                unavailable);

            _isQuitRegistered = Register(
                QuitHotkeyId,
                _configuration.Quit,
                "quit",
                HotkeyConfiguration.QuitVariable,
                unavailable);

            _isTypeRegistered = Register(
                TypeHotkeyId,
                _configuration.Type,
                "type a question",
                HotkeyConfiguration.TypeVariable,
                unavailable);

            UnavailableHotkeys = unavailable;

            if (!_isActivationRegistered && !_isCaptureRegistered && !_isVisionRegistered
                && !_isQuitRegistered && !_isTypeRegistered)
            {
                throw new InvalidOperationException(
                    "None of the CursorAI hotkeys could be registered. Another application owns "
                    + "all three combinations, so CursorAI would have no way to be activated.");
            }

            _source.AddHook(WindowProcedure);
            _isHookInstalled = true;
        }
        catch
        {
            RollBackRegistrations();
            throw;
        }
    }

    /// <summary>
    /// Human-readable names of the hotkeys another application already owned. Empty when all
    /// three registered. A partially registered service is a supported state: the remaining
    /// shortcuts still work.
    /// </summary>
    internal IReadOnlyList<string> UnavailableHotkeys { get; private set; } = [];

    internal event EventHandler? ActivationHotkeyPressed;

    internal event EventHandler? CaptureHotkeyPressed;

    internal event EventHandler? VisionHotkeyPressed;

    internal event EventHandler? QuitHotkeyPressed;

    internal event EventHandler? TypeHotkeyPressed;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        List<string> failures = [];
        int firstError = 0;
        TryUnregister(TypeHotkeyId, _configuration.Type.DisplayName, ref _isTypeRegistered, failures, ref firstError);
        TryUnregister(QuitHotkeyId, _configuration.Quit.DisplayName, ref _isQuitRegistered, failures, ref firstError);
        TryUnregister(VisionHotkeyId, _configuration.Vision.DisplayName, ref _isVisionRegistered, failures, ref firstError);
        TryUnregister(CaptureHotkeyId, _configuration.Capture.DisplayName, ref _isCaptureRegistered, failures, ref firstError);
        TryUnregister(ActivationHotkeyId, _configuration.Activation.DisplayName, ref _isActivationRegistered, failures, ref firstError);

        if (_isHookInstalled)
        {
            _source.RemoveHook(WindowProcedure);
            _isHookInstalled = false;
        }

        ActivationHotkeyPressed = null;
        CaptureHotkeyPressed = null;
        VisionHotkeyPressed = null;
        QuitHotkeyPressed = null;
        TypeHotkeyPressed = null;

        if (failures.Count > 0)
        {
            throw new Win32Exception(firstError, $"Could not unregister: {string.Join(", ", failures)}.");
        }
    }

    /// <summary>
    /// Registers one shortcut, recording an unavailable one with the variable that changes it.
    /// </summary>
    private bool Register(
        int id,
        HotkeyDefinition hotkey,
        string role,
        string environmentVariable,
        List<string> unavailable)
    {
        if (NativeMethods.RegisterHotkey(_windowHandle, id, hotkey))
        {
            return true;
        }

        unavailable.Add($"{hotkey.DisplayName} ({role}) - set {environmentVariable} to choose another");
        return false;
    }

    private void RollBackRegistrations()
    {
        RollBackRegistration(TypeHotkeyId, _configuration.Type.DisplayName, ref _isTypeRegistered);
        RollBackRegistration(QuitHotkeyId, _configuration.Quit.DisplayName, ref _isQuitRegistered);
        RollBackRegistration(VisionHotkeyId, _configuration.Vision.DisplayName, ref _isVisionRegistered);
        RollBackRegistration(CaptureHotkeyId, _configuration.Capture.DisplayName, ref _isCaptureRegistered);
        RollBackRegistration(ActivationHotkeyId, _configuration.Activation.DisplayName, ref _isActivationRegistered);
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
        else if (wordParameter == VisionHotkeyId)
        {
            VisionHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (wordParameter == QuitHotkeyId)
        {
            QuitHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (wordParameter == TypeHotkeyId)
        {
            TypeHotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return 0;
    }
}
