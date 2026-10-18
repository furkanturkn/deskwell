using System.Runtime.InteropServices;

namespace HueDesk;

public static class DesktopWidgetWindow
{
    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080;
    private const uint NoMove = 0x0002;
    private const uint NoSize = 0x0001;
    private const uint NoActivate = 0x0010;
    private const uint NoZOrder = 0x0004;
    private static readonly IntPtr BottomWindow = new(1);
    private static readonly IntPtr TopWindow = IntPtr.Zero;

    public static void SetToolWindow(IntPtr windowHandle, bool isToolWindow)
    {
        var style = GetWindowLongPtr(windowHandle, ExtendedStyleIndex).ToInt64();
        style = isToolWindow ? style | ToolWindowStyle : style & ~ToolWindowStyle;
        SetWindowLongPtr(windowHandle, ExtendedStyleIndex, new IntPtr(style));
    }

    public static void SendToBottom(IntPtr windowHandle)
    {
        SetWindowPos(windowHandle, BottomWindow, 0, 0, 0, 0, NoMove | NoSize | NoActivate);
    }

    public static void BringToTop(IntPtr windowHandle)
    {
        SetWindowPos(windowHandle, TopWindow, 0, 0, 0, 0, NoMove | NoSize);
    }

    public static void KeepAtBottom(IntPtr windowPositionPointer)
    {
        var position = Marshal.PtrToStructure<WindowPosition>(windowPositionPointer);
        if ((position.Flags & NoZOrder) != 0)
        {
            return;
        }

        position.InsertAfter = BottomWindow;
        Marshal.StructureToPtr(position, windowPositionPointer, fDeleteOld: false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        public IntPtr Handle;
        public IntPtr InsertAfter;
        public int Left;
        public int Top;
        public int Width;
        public int Height;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter, int left, int top, int width, int height, uint flags);
}
