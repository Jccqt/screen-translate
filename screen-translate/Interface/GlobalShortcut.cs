using System.Runtime.InteropServices;
using screen_translate.Settings;

namespace screen_translate.Interface;

public interface IGlobalShortcut : IDisposable
{
    event EventHandler? Pressed;
    string? TrySet(Keys shortcut);
}

/// <summary>A private message window owns registration; replacing a shortcut is transactional.</summary>
public sealed class GlobalShortcut : NativeWindow, IGlobalShortcut
{
    private int _registeredId;
    private Keys _shortcut;
    private bool _disposed;
    public event EventHandler? Pressed;

    public string? TrySet(Keys shortcut)
    {
        if (_disposed) return "The application is closing.";
        if (!InterfaceSettings.IsValidShortcut(shortcut)) return "Use Ctrl or Alt with a letter, number, or function key. Shift is optional.";
        if (_registeredId != 0 && shortcut == _shortcut) return null;
        if (Handle == 0) CreateHandle(new CreateParams { Parent = new nint(-3) }); // HWND_MESSAGE
        uint modifiers = 0x4000; // MOD_NOREPEAT
        if ((shortcut & Keys.Alt) != 0) modifiers |= 1;
        if ((shortcut & Keys.Control) != 0) modifiers |= 2;
        if ((shortcut & Keys.Shift) != 0) modifiers |= 4;
        int nextId = _registeredId == 1 ? 2 : 1;
        if (!RegisterHotKey(Handle, nextId, modifiers, (uint)(shortcut & Keys.KeyCode)))
            return "This shortcut is unavailable or used by another application. Choose a different shortcut.";
        if (_registeredId != 0) UnregisterHotKey(Handle, _registeredId);
        _registeredId = nextId;
        _shortcut = shortcut;
        return null;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam == _registeredId && !_disposed)
            Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registeredId != 0) UnregisterHotKey(Handle, _registeredId);
        _registeredId = 0;
        if (Handle != 0) DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
