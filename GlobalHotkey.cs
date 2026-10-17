using System.Runtime.InteropServices;

namespace HueDesk;

public sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const int HotkeyId = 1;

    public GlobalHotkey(HotkeyModifiers modifiers, Keys key)
    {
        CreateHandle(new CreateParams());
        if (!RegisterHotKey(Handle, HotkeyId, (uint)modifiers, (uint)key))
        {
            Logger.Write($"Global hotkey {modifiers}+{key} is already taken by another application.", null);
        }
    }

    public event Action? Pressed;

    public void Dispose()
    {
        UnregisterHotKey(Handle, HotkeyId);
        DestroyHandle();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage && message.WParam == HotkeyId)
        {
            Pressed?.Invoke();
            return;
        }

        base.WndProc(ref message);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int hotkeyId, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int hotkeyId);
}

[Flags]
public enum HotkeyModifiers : uint
{
    Alt = 0x0001,
    Control = 0x0002,
}
