using System.Runtime.InteropServices;

namespace HueDesk;

public static class DwmWindow
{
    public static void SetAttribute(IntPtr windowHandle, int attribute, ref int value)
    {
        DwmSetWindowAttribute(windowHandle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);
}
