using System.Runtime.InteropServices;

namespace HueDesk;

public sealed class MainForm : Form
{
    private const int HotkeyMessage = 0x0312;
    private const int ToggleHotkeyId = 1;
    private const uint ModifierAlt = 0x0001;
    private const uint ModifierControl = 0x0002;
    private const int DropShadowClassStyle = 0x20000;
    private const int DwmDarkModeAttribute = 20;
    private const int DwmCornerPreferenceAttribute = 33;
    private const int DwmRoundCorner = 2;
    private const int ContentWidth = 380;
    private const int HeaderHeight = 214;
    private const int SideMargin = 16;
    private const int ScreenEdgeGap = 12;
    private const int BrightnessSendDelayMilliseconds = 60;
    private const int ScheduleCheckSeconds = 30;
    private const int TrayClickGuardMilliseconds = 300;

    private readonly LightRegistry registry;
    private readonly AppSettings settings;
    private readonly List<HueLight> activeLights = [];
    private readonly Dictionary<HueLight, string> appliedSceneNames = [];
    private readonly FlowLayoutPanel panelHost = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(SideMargin, 4, SideMargin, 8),
        BackColor = Theme.Background,
    };

    private readonly Panel header = new() { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = Theme.Background };
    private readonly Label subtitleLabel = new() { AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly ToggleSwitch masterToggle = new();
    private readonly ModernSlider masterBrightness = new() { Minimum = HueProtocol.MinimumBrightness, Maximum = HueProtocol.MaximumBrightness };
    private readonly Label masterBrightnessValue = new() { AutoSize = false, Size = new Size(44, 20), TextAlign = ContentAlignment.MiddleRight, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly ToggleSwitch autoToggle = new();
    private readonly Label autoSubtitleLabel = new() { AutoSize = false, Size = new Size(ContentWidth - 2 * SideMargin, 18), Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly Chip addLightChip = new() { Text = "+  Add light", Size = new Size(ContentWidth - 2 * SideMargin, 38), Margin = new Padding(0) };
    private readonly System.Windows.Forms.Timer masterBrightnessTimer = new() { Interval = BrightnessSendDelayMilliseconds };
    private readonly System.Windows.Forms.Timer scheduleTimer = new() { Interval = ScheduleCheckSeconds * 1000 };
    private bool isAutoHideSuppressed;
    private DateTime lastAutoHideTime = DateTime.MinValue;

    public MainForm(LightRegistry registry)
    {
        this.registry = registry;
        settings = AppSettings.Load();

        Text = "Hue Desk";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Background;
        ClientSize = new Size(ContentWidth, 520);

        BuildHeader();
        addLightChip.Click += (_, _) => ShowAddLightDialog();
        panelHost.Controls.Add(addLightChip);
        Controls.Add(panelHost);
        Controls.Add(header);

        masterBrightnessTimer.Tick += (_, _) => SendMasterBrightness();
        scheduleTimer.Tick += (_, _) => ApplyScheduledScene();
        scheduleTimer.Start();

        _ = Handle;

        foreach (var savedLight in registry.Lights)
        {
            StartLight(savedLight);
        }

        FitToContent();
    }

    public bool HasLights => activeLights.Count > 0;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ClassStyle |= DropShadowClassStyle;
            return createParams;
        }
    }

    public void ShowNearTray()
    {
        FitToContent();
        Show();
        Activate();
    }

    public void ToggleNearTray()
    {
        if (Visible)
        {
            Hide();
            return;
        }

        var wasJustHiddenByDeactivation = (DateTime.UtcNow - lastAutoHideTime).TotalMilliseconds < TrayClickGuardMilliseconds;
        if (!wasJustHiddenByDeactivation)
        {
            ShowNearTray();
        }
    }

    public void StopAllLights()
    {
        foreach (var light in activeLights)
        {
            light.Dispose();
        }

        activeLights.Clear();
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        var darkMode = 1;
        var cornerPreference = DwmRoundCorner;
        DwmSetWindowAttribute(Handle, DwmDarkModeAttribute, ref darkMode, sizeof(int));
        DwmSetWindowAttribute(Handle, DwmCornerPreferenceAttribute, ref cornerPreference, sizeof(int));

        if (!RegisterHotKey(Handle, ToggleHotkeyId, ModifierControl | ModifierAlt, (uint)Keys.L))
        {
            Logger.Write("Global hotkey Ctrl+Alt+L is already taken by another application.", null);
        }
    }

    protected override void OnHandleDestroyed(EventArgs eventArgs)
    {
        UnregisterHotKey(Handle, ToggleHotkeyId);
        base.OnHandleDestroyed(eventArgs);
    }

    protected override void OnDeactivate(EventArgs eventArgs)
    {
        base.OnDeactivate(eventArgs);
        if (Visible && !isAutoHideSuppressed)
        {
            lastAutoHideTime = DateTime.UtcNow;
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs paintEventArgs)
    {
        base.OnPaint(paintEventArgs);
        using var borderPen = new Pen(Theme.CardBorder);
        paintEventArgs.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage && message.WParam == ToggleHotkeyId)
        {
            ToggleNearTray();
            return;
        }

        base.WndProc(ref message);
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(eventArgs);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int hotkeyId, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int hotkeyId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);

    private void BuildHeader()
    {
        var titleLabel = new Label { Text = "Hue Desk", AutoSize = true, Font = Theme.TitleFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Location = new Point(SideMargin, 12) };
        subtitleLabel.Location = new Point(SideMargin + 2, 42);
        masterToggle.Location = new Point(ContentWidth - SideMargin - masterToggle.Width, 18);

        var brightnessCaption = new Label { Text = "All lights", AutoSize = false, Size = new Size(76, 22), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Location = new Point(SideMargin, 76) };
        var sliderLeft = SideMargin + 76;
        masterBrightness.Location = new Point(sliderLeft, 74);
        masterBrightness.Size = new Size(ContentWidth - SideMargin - 44 - sliderLeft - 4, 26);
        masterBrightnessValue.Location = new Point(ContentWidth - SideMargin - 44, 76);

        header.Controls.AddRange([titleLabel, subtitleLabel, masterToggle, brightnessCaption, masterBrightness, masterBrightnessValue]);

        const int chipWidth = 64;
        const int chipGap = 7;
        for (var index = 0; index < Scenes.All.Count; index++)
        {
            var scene = Scenes.All[index];
            var chip = new Chip { Text = scene.Name, Size = new Size(chipWidth, 30), Location = new Point(SideMargin + index * (chipWidth + chipGap), 116) };
            chip.Click += (_, _) => ApplySceneToAll(scene);
            header.Controls.Add(chip);
        }

        var autoTitle = new Label { Text = "Auto by time of day", AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Location = new Point(SideMargin, 162) };
        autoSubtitleLabel.Location = new Point(SideMargin, 190);
        autoToggle.Location = new Point(ContentWidth - SideMargin - autoToggle.Width, 162);
        autoToggle.SetCheckedSilently(settings.AutoScenesEnabled);
        header.Controls.AddRange([autoTitle, autoSubtitleLabel, autoToggle]);

        masterToggle.CheckedChanged += (_, _) => SetPowerOfAll(masterToggle.Checked);
        masterBrightness.ValueChanged += (_, _) => masterBrightnessValue.Text = $"{(int)Math.Round(masterBrightness.Value * 100.0 / HueProtocol.MaximumBrightness)}%";
        masterBrightness.UserChanged += (_, _) => masterBrightnessTimer.Start();
        autoToggle.CheckedChanged += (_, _) => OnAutoToggled();
        masterBrightness.SetValueSilently(HueProtocol.MaximumBrightness);
        RefreshAutoSubtitle();
    }

    private void StartLight(SavedLight savedLight)
    {
        var light = new HueLight(savedLight);
        activeLights.Add(light);

        var panel = new LightPanel(light, RemoveLight);
        panel.LayoutHeightChanged += FitToContent;
        panelHost.Controls.Add(panel);
        panelHost.Controls.SetChildIndex(panel, panelHost.Controls.IndexOf(addLightChip));

        light.StateChanged += _ => BeginInvokeIfCreated(RefreshMaster);
        light.ConnectionChanged += () => BeginInvokeIfCreated(() =>
        {
            RefreshMaster();
            ApplyScheduledScene();
        });

        light.StartKeepingConnected();
        RefreshMaster();
    }

    private void RemoveLight(SavedLight savedLight)
    {
        var light = activeLights.FirstOrDefault(active => active.SavedLight == savedLight);
        if (light is null)
        {
            return;
        }

        light.Dispose();
        activeLights.Remove(light);
        appliedSceneNames.Remove(light);
        registry.Remove(savedLight.BluetoothAddress);

        var panel = panelHost.Controls.OfType<LightPanel>().FirstOrDefault(candidate => candidate.SavedLight == savedLight);
        if (panel is not null)
        {
            panelHost.Controls.Remove(panel);
            panel.Dispose();
        }

        RefreshMaster();
        FitToContent();
    }

    private void ShowAddLightDialog()
    {
        isAutoHideSuppressed = true;
        try
        {
            using var dialog = new AddLightForm(registry.Lights.Select(saved => saved.BluetoothAddress));
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.AddedLight is null)
            {
                return;
            }

            registry.Add(dialog.AddedLight);
            StartLight(dialog.AddedLight);
            FitToContent();
        }
        finally
        {
            isAutoHideSuppressed = false;
        }
    }

    private void FitToContent()
    {
        var contentHeight = panelHost.Padding.Vertical;
        foreach (Control child in panelHost.Controls)
        {
            contentHeight += child.Height + child.Margin.Vertical;
        }

        var workingArea = Screen.PrimaryScreen!.WorkingArea;
        var maximumHeight = workingArea.Height - 2 * ScreenEdgeGap;
        var totalHeight = Math.Min(HeaderHeight + contentHeight, maximumHeight);
        ClientSize = new Size(ContentWidth, totalHeight);
        Location = new Point(workingArea.Right - Width - ScreenEdgeGap, workingArea.Bottom - Height - ScreenEdgeGap);
    }

    private void BeginInvokeIfCreated(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(action);
        }
    }

    private IEnumerable<HueLight> ConnectedLights => activeLights.Where(light => light.Connection == ConnectionState.Connected);

    private void RefreshMaster()
    {
        var connectedLights = ConnectedLights.ToList();
        subtitleLabel.Text = activeLights.Count == 0
            ? "No lights yet"
            : $"{connectedLights.Count} of {activeLights.Count} lights connected";

        var knownStates = connectedLights.Select(light => light.LastState).OfType<LightState>().ToList();
        masterToggle.SetCheckedSilently(knownStates.Any(state => state.IsOn));
        masterToggle.Enabled = connectedLights.Count > 0;
        masterBrightness.Enabled = connectedLights.Count > 0;
        if (knownStates.Count > 0)
        {
            masterBrightness.SetValueSilently((int)knownStates.Average(state => state.Brightness));
        }
    }

    private void SetPowerOfAll(bool isOn)
    {
        foreach (var light in ConnectedLights)
        {
            _ = RunSafelyAsync(light, () => light.SetPowerAsync(isOn));
        }
    }

    private void SendMasterBrightness()
    {
        masterBrightnessTimer.Stop();
        var brightness = (byte)masterBrightness.Value;
        foreach (var light in ConnectedLights)
        {
            _ = RunSafelyAsync(light, () => light.SetBrightnessAsync(brightness));
        }
    }

    private void ApplySceneToAll(Scene scene)
    {
        var scheduledName = Scenes.ScheduledAt(TimeOnly.FromDateTime(DateTime.Now)).Name;
        foreach (var light in ConnectedLights)
        {
            appliedSceneNames[light] = scheduledName;
            _ = RunSafelyAsync(light, () => light.ApplySceneAsync(scene));
        }
    }

    private void OnAutoToggled()
    {
        settings.AutoScenesEnabled = autoToggle.Checked;
        settings.Save();
        RefreshAutoSubtitle();
        if (settings.AutoScenesEnabled)
        {
            appliedSceneNames.Clear();
            ApplyScheduledScene();
        }
    }

    private void ApplyScheduledScene()
    {
        RefreshAutoSubtitle();
        if (!settings.AutoScenesEnabled)
        {
            return;
        }

        var scene = Scenes.ScheduledAt(TimeOnly.FromDateTime(DateTime.Now));
        foreach (var light in ConnectedLights)
        {
            if (appliedSceneNames.GetValueOrDefault(light) == scene.Name)
            {
                continue;
            }

            appliedSceneNames[light] = scene.Name;
            _ = RunSafelyAsync(light, () => light.ApplySceneAsync(scene));
        }
    }

    private void RefreshAutoSubtitle()
    {
        var current = Scenes.ScheduledAt(TimeOnly.FromDateTime(DateTime.Now));
        var schedule = string.Join(" · ", Scenes.All.Where(scene => scene.AutoStart is not null).OrderBy(scene => scene.AutoStart).Select(scene => $"{scene.Name} {scene.AutoStart:HH}"));
        autoSubtitleLabel.Text = settings.AutoScenesEnabled ? $"Now: {current.Name}  ·  {schedule}" : "Off: lights stay as you leave them";
    }

    private static async Task RunSafelyAsync(HueLight light, Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            Logger.Write($"{light.SavedLight.Name}: command failed", exception);
        }
    }
}
