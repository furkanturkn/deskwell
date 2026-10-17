using Microsoft.Win32;

namespace HueDesk;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "HueDesk";
    private const string StartupMarkerFileName = "startup-configured";

    private readonly NotifyIcon notifyIcon;
    private readonly MainForm mainForm;
    private readonly ToolStripMenuItem startWithWindowsItem;

    public TrayApplicationContext()
    {
        mainForm = new MainForm(LightRegistry.Load());
        ApplyStartWithWindowsDefault();
        startWithWindowsItem = new ToolStripMenuItem("Start with Windows") { Checked = IsStartWithWindowsEnabled(), CheckOnClick = true };
        startWithWindowsItem.CheckedChanged += (_, _) => SetStartWithWindows(startWithWindowsItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => mainForm.ShowNearTray());
        menu.Items.Add(startWithWindowsItem);
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        var trayIcon = CreateTrayIcon();
        notifyIcon = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "Hue Desk",
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.MouseClick += (_, clicked) =>
        {
            if (clicked.Button == MouseButtons.Left)
            {
                ToggleWindow();
            }
        };

        if (!mainForm.HasLights)
        {
            mainForm.ShowNearTray();
        }
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var glowBrush = new SolidBrush(Theme.Accent);
            using var corePen = new Pen(Theme.OnAccent, 2.5f);
            graphics.FillEllipse(glowBrush, 3, 3, 26, 26);
            graphics.DrawArc(corePen, 10, 9, 12, 12, 200, 140);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void ApplyStartWithWindowsDefault()
    {
        var markerPath = Path.Combine(AppPaths.DataDirectory, StartupMarkerFileName);
        if (!File.Exists(markerPath))
        {
            SetStartWithWindows(true);
            File.WriteAllText(markerPath, string.Empty);
            return;
        }

        if (IsStartWithWindowsEnabled())
        {
            SetStartWithWindows(true);
        }
    }

    private static bool IsStartWithWindowsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath);
        return key?.GetValue(StartupValueName) is not null;
    }

    private static void SetStartWithWindows(bool isEnabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Startup registry key is unavailable.");
        if (isEnabled)
        {
            key.SetValue(StartupValueName, $"\"{Environment.ProcessPath}\"");
            return;
        }

        key.DeleteValue(StartupValueName, throwOnMissingValue: false);
    }

    private void ToggleWindow()
    {
        mainForm.ToggleNearTray();
    }

    private void ExitApplication()
    {
        notifyIcon.Visible = false;
        mainForm.StopAllLights();
        notifyIcon.Dispose();
        mainForm.Dispose();
        ExitThread();
    }
}
