namespace HueDesk;

public sealed class MainForm : Form
{
    private const int DropShadowClassStyle = 0x20000;
    private const int DwmDarkModeAttribute = 20;
    private const int DwmCornerPreferenceAttribute = 33;
    private const int DwmRoundCorner = 2;
    private const int ContentWidth = 380;
    private const int InitialHeight = 520;
    private const int HeaderHeight = 162;
    private const int TabsTop = 120;
    private const int TabsHeight = 30;
    private const int ScenesPageHeight = 116;
    private const int ToolsPageHeight = 152;
    private const int LightsTabIndex = 0;
    private const int ScenesTabIndex = 1;
    private const int ToolsTabIndex = 2;
    private const int SideMargin = 16;
    private const int InnerWidth = ContentWidth - 2 * SideMargin;
    private const int ScreenEdgeGap = 12;
    private const int HostTopPadding = 4;
    private const int HostBottomPadding = 8;
    private const int TitleTop = 12;
    private const int SubtitleTop = 42;
    private const int SubtitleLeftOffset = 2;
    private const int MasterToggleTop = 16;
    private const int MasterSliderTop = 74;
    private const int MasterRowTop = 76;
    private const int CaptionWidth = 76;
    private const int ValueWidth = 44;
    private const int ValueHeight = 20;
    private const int CaptionHeight = 22;
    private const int SliderHeight = 26;
    private const int SliderRightGap = 4;
    private const int ScenesTop = 8;
    private const int SceneChipWidth = 64;
    private const int SceneChipHeight = 30;
    private const int SceneChipGap = 7;
    private const int AutoRowTop = 56;
    private const int AutoSubtitleTop = 84;
    private const int AutoSubtitleHeight = 18;
    private const int SyncRowTop = 56;
    private const int SyncSubtitleTop = 84;
    private const int SyncSubtitleHeight = 18;
    private const int SmoothnessRowTop = 118;
    private const int SmoothnessChipWidth = 56;
    private const int SmoothnessChipHeight = 26;
    private const int SmoothnessChipGap = 8;
    private const int SmoothnessSliderTopOffset = -2;
    private const int SmoothnessSaveDelayMilliseconds = 500;
    private const int EggRowTop = 8;
    private const int EggChipWidth = 84;
    private const int EggChipHeight = 30;
    private const int EggChipGap = 6;
    private const int EggCancelChipWidth = 80;
    private const int EggCaptionTopOffset = 4;
    private const int MenuChipWidth = 30;
    private const int MenuChipHeight = 28;
    private const int MenuChipTop = 16;
    private const int MenuChipGap = 12;
    private const int BrightnessSendDelayMilliseconds = 60;
    private const int TrayClickGuardMilliseconds = 300;

    private readonly LightRegistry registry;
    private readonly List<HueLight> activeLights = [];
    private readonly SceneScheduler sceneScheduler;
    private readonly GlobalHotkey toggleHotkey;
    private readonly EggTimer eggTimer = new();
    private readonly ScreenSync screenSync;
    private readonly ToggleSwitch syncToggle = new();
    private readonly AppSettings settings = AppSettings.Load();
    private readonly ModernSlider smoothnessSlider = new() { Minimum = 0, Maximum = ScreenSync.MaximumSmoothness };
    private readonly Label smoothnessValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly System.Windows.Forms.Timer smoothnessSaveTimer = new() { Interval = SmoothnessSaveDelayMilliseconds };
    private readonly List<Chip> eggPresetChips = [];
    private readonly Label eggCountdownLabel = new() { AutoSize = true, Font = Theme.HeadingFont, ForeColor = Theme.Accent, BackColor = Color.Transparent, Visible = false };
    private readonly Chip eggCancelChip = new() { Text = "Cancel", Visible = false };
    private readonly FlowLayoutPanel panelHost = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = Theme.Background,
    };

    private readonly SegmentedControl tabSwitch = new("Lights", "Scenes", "Tools");
    private readonly Panel scenesPage = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };
    private readonly Panel toolsPage = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };
    private readonly Panel header = new() { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = Theme.Background };
    private readonly Label subtitleLabel = new() { AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly PowerButton masterToggle = new() { Icon = PowerIcon.Power };
    private readonly ModernSlider masterBrightness = new() { Minimum = HueProtocol.MinimumBrightness, Maximum = HueProtocol.MaximumBrightness };
    private readonly Label masterBrightnessValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly ToggleSwitch autoToggle = new();
    private readonly Label autoSubtitleLabel = new() { AutoSize = false, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly Chip menuChip = new() { Text = "...", Size = new Size(MenuChipWidth, MenuChipHeight) };
    private readonly ContextMenuStrip headerMenu = new();
    private readonly System.Windows.Forms.Timer masterBrightnessTimer = new() { Interval = BrightnessSendDelayMilliseconds };
    private string lightsSummary = string.Empty;
    private bool isAutoHideSuppressed;
    private bool isCelebrating;
    private DateTime lastAutoHideTime = DateTime.MinValue;

    public MainForm(LightRegistry registry)
    {
        this.registry = registry;
        screenSync = new ScreenSync(() => ConnectedLights) { Smoothness = settings.ScreenSyncSmoothness };
        sceneScheduler = new SceneScheduler(settings, () => ConnectedLights, () => screenSync.IsEnabled);
        sceneScheduler.Ticked += RefreshAutoSubtitle;

        Text = "Hue Desk";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Background;
        ClientSize = new Size(ContentWidth, InitialHeight);

        panelHost.Padding = new Padding(SideMargin, HostTopPadding, SideMargin, HostBottomPadding);
        headerMenu.Items.Add("Add light...", null, (_, _) => ShowAddLightDialog());
        headerMenu.Items.Add("Reset all lights", null, (_, _) => ResetAllLights());
        var connectionItem = new ToolStripMenuItem();
        connectionItem.Click += (_, _) => ChangeConnectionOfAll(!HasActiveConnections);
        headerMenu.Items.Add(connectionItem);
        headerMenu.Opening += (_, _) => connectionItem.Text = HasActiveConnections ? "Release all lights (free for phone app)" : "Reconnect all lights";
        menuChip.Click += (_, _) => headerMenu.Show(menuChip, new Point(0, menuChip.Height));
        BuildHeader();
        Controls.Add(panelHost);
        Controls.Add(scenesPage);
        Controls.Add(toolsPage);
        Controls.Add(header);

        masterBrightnessTimer.Tick += (_, _) => SendMasterBrightness();
        eggTimer.Changed += RefreshEggTimer;
        eggTimer.Finished += OnEggTimerFinished;

        toggleHotkey = new GlobalHotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.L);
        toggleHotkey.Pressed += ToggleNearTray;

        _ = Handle;

        foreach (var savedLight in registry.Lights)
        {
            StartLight(savedLight);
        }

        FitToContent();
    }

    public event Action? EggTimerFinished;

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
        sceneScheduler.Dispose();
        smoothnessSaveTimer.Dispose();
        screenSync.Stop();
        screenSync.Dispose();
        eggTimer.Dispose();
        headerMenu.Dispose();
        toggleHotkey.Dispose();
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        var darkMode = 1;
        var cornerPreference = DwmRoundCorner;
        DwmWindow.SetAttribute(Handle, DwmDarkModeAttribute, ref darkMode);
        DwmWindow.SetAttribute(Handle, DwmCornerPreferenceAttribute, ref cornerPreference);
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

    private bool HasActiveConnections => activeLights.Any(light => !light.IsReleased);

    private IEnumerable<HueLight> ConnectedLights => activeLights.Where(light => light.IsConnected);

    private void BuildHeader()
    {
        var titleLabel = new Label { Text = "Hue Desk", AutoSize = true, Font = Theme.TitleFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Location = new Point(SideMargin, TitleTop) };
        subtitleLabel.Location = new Point(SideMargin + SubtitleLeftOffset, SubtitleTop);
        masterToggle.Location = new Point(ContentWidth - SideMargin - masterToggle.Width, MasterToggleTop);
        menuChip.Location = new Point(masterToggle.Left - MenuChipGap - MenuChipWidth, MenuChipTop);

        var brightnessCaption = new Label { Text = "All lights", AutoSize = false, Size = new Size(CaptionWidth, CaptionHeight), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Location = new Point(SideMargin, MasterRowTop) };
        var sliderLeft = SideMargin + CaptionWidth;
        masterBrightness.Location = new Point(sliderLeft, MasterSliderTop);
        masterBrightness.Size = new Size(ContentWidth - SideMargin - ValueWidth - sliderLeft - SliderRightGap, SliderHeight);
        masterBrightnessValue.Size = new Size(ValueWidth, ValueHeight);
        masterBrightnessValue.Location = new Point(ContentWidth - SideMargin - ValueWidth, MasterRowTop);

        tabSwitch.Location = new Point(SideMargin, TabsTop);
        tabSwitch.Size = new Size(InnerWidth, TabsHeight);
        tabSwitch.SelectedChanged += (_, _) => ShowTab(tabSwitch.SelectedIndex);
        header.Controls.AddRange([titleLabel, subtitleLabel, menuChip, masterToggle, brightnessCaption, masterBrightness, masterBrightnessValue, tabSwitch]);

        for (var index = 0; index < Scenes.All.Count; index++)
        {
            var scene = Scenes.All[index];
            var chip = new Chip { Text = scene.Name, Size = new Size(SceneChipWidth, SceneChipHeight), Location = new Point(SideMargin + index * (SceneChipWidth + SceneChipGap), ScenesTop) };
            chip.Click += (_, _) => ApplySceneToAll(scene);
            scenesPage.Controls.Add(chip);
        }

        var autoTitle = new Label { Text = "Auto by time of day", AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Location = new Point(SideMargin, AutoRowTop) };
        autoSubtitleLabel.Size = new Size(InnerWidth, AutoSubtitleHeight);
        autoSubtitleLabel.Location = new Point(SideMargin, AutoSubtitleTop);
        autoToggle.Location = new Point(ContentWidth - SideMargin - autoToggle.Width, AutoRowTop);
        autoToggle.SetCheckedSilently(sceneScheduler.IsEnabled);
        scenesPage.Controls.AddRange([autoTitle, autoSubtitleLabel, autoToggle]);

        BuildEggTimerRow();
        BuildScreenSyncRow();

        masterToggle.CheckedChanged += (_, _) => SetPowerOfAll(masterToggle.Checked);
        masterBrightness.ValueChanged += (_, _) => masterBrightnessValue.Text = $"{(int)Math.Round(masterBrightness.Value * 100.0 / HueProtocol.MaximumBrightness)}%";
        masterBrightness.UserChanged += (_, _) => masterBrightnessTimer.Start();
        autoToggle.CheckedChanged += (_, _) =>
        {
            sceneScheduler.IsEnabled = autoToggle.Checked;
            RefreshAutoSubtitle();
        };
        masterBrightness.SetValueSilently(HueProtocol.MaximumBrightness);
        RefreshAutoSubtitle();
    }

    private void BuildEggTimerRow()
    {
        var caption = new Label { Text = "Egg timer", AutoSize = false, Size = new Size(CaptionWidth, CaptionHeight), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Location = new Point(SideMargin, EggRowTop + EggCaptionTopOffset) };
        toolsPage.Controls.Add(caption);

        var chipLeft = SideMargin + CaptionWidth;
        for (var index = 0; index < EggTimer.Presets.Count; index++)
        {
            var preset = EggTimer.Presets[index];
            var chip = new Chip { Text = $"{preset.Name} {preset.Minutes}m", Size = new Size(EggChipWidth, EggChipHeight), Location = new Point(chipLeft + index * (EggChipWidth + EggChipGap), EggRowTop) };
            chip.Click += (_, _) => StartEggTimer(preset);
            eggPresetChips.Add(chip);
            toolsPage.Controls.Add(chip);
        }

        eggCountdownLabel.Location = new Point(chipLeft, EggRowTop + EggCaptionTopOffset);
        eggCancelChip.Size = new Size(EggCancelChipWidth, EggChipHeight);
        eggCancelChip.Location = new Point(ContentWidth - SideMargin - EggCancelChipWidth, EggRowTop);
        eggCancelChip.Click += (_, _) => eggTimer.Cancel();
        toolsPage.Controls.AddRange([eggCountdownLabel, eggCancelChip]);
    }

    private void BuildScreenSyncRow()
    {
        var title = new Label { Text = "Screen sync", AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Location = new Point(SideMargin, SyncRowTop) };
        var subtitle = new Label { Text = "Lights follow the colors on your screen", AutoSize = false, Size = new Size(InnerWidth, SyncSubtitleHeight), Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Location = new Point(SideMargin, SyncSubtitleTop) };
        syncToggle.Location = new Point(ContentWidth - SideMargin - syncToggle.Width, SyncRowTop);
        syncToggle.CheckedChanged += (_, _) =>
        {
            if (syncToggle.Checked)
            {
                screenSync.Start();
                return;
            }

            screenSync.Stop();
        };
        toolsPage.Controls.AddRange([title, subtitle, syncToggle]);
        BuildSmoothnessRow();
    }

    private void BuildSmoothnessRow()
    {
        var caption = new Label { Text = "Softness", AutoSize = false, Size = new Size(CaptionWidth, CaptionHeight), TextAlign = ContentAlignment.MiddleLeft, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Location = new Point(SideMargin, SmoothnessRowTop) };
        var resetChip = new Chip { Text = "Reset", Size = new Size(SmoothnessChipWidth, SmoothnessChipHeight), Location = new Point(ContentWidth - SideMargin - SmoothnessChipWidth, SmoothnessRowTop) };
        var valueLeft = resetChip.Left - SmoothnessChipGap - ValueWidth;
        var sliderLeft = SideMargin + CaptionWidth;
        smoothnessSlider.Location = new Point(sliderLeft, SmoothnessRowTop + SmoothnessSliderTopOffset);
        smoothnessSlider.Size = new Size(valueLeft - SliderRightGap - sliderLeft, SliderHeight);
        smoothnessValue.Size = new Size(ValueWidth, ValueHeight);
        smoothnessValue.Location = new Point(valueLeft, SmoothnessRowTop);

        smoothnessSlider.ValueChanged += (_, _) => smoothnessValue.Text = $"{smoothnessSlider.Value}%";
        smoothnessSlider.UserChanged += (_, _) =>
        {
            screenSync.Smoothness = smoothnessSlider.Value;
            smoothnessSaveTimer.Stop();
            smoothnessSaveTimer.Start();
        };
        smoothnessSaveTimer.Tick += (_, _) =>
        {
            smoothnessSaveTimer.Stop();
            settings.ScreenSyncSmoothness = screenSync.Smoothness;
            settings.Save();
        };
        resetChip.Click += (_, _) => ResetSmoothness();
        smoothnessSlider.SetValueSilently(settings.ScreenSyncSmoothness);
        smoothnessValue.Text = $"{smoothnessSlider.Value}%";
        toolsPage.Controls.AddRange([caption, smoothnessSlider, smoothnessValue, resetChip]);
    }

    private void ResetSmoothness()
    {
        smoothnessSlider.SetValueSilently(ScreenSync.DefaultSmoothness);
        smoothnessValue.Text = $"{ScreenSync.DefaultSmoothness}%";
        screenSync.Smoothness = ScreenSync.DefaultSmoothness;
        settings.ScreenSyncSmoothness = ScreenSync.DefaultSmoothness;
        settings.Save();
    }

    private void ResetAllLights()
    {
        foreach (var light in ConnectedLights)
        {
            _ = LightCommands.RunSafely(light, light.ResetToDefault);
        }
    }

    private void StartEggTimer(EggPreset preset)
    {
        if (!isCelebrating)
        {
            eggTimer.Start(preset.Duration);
        }
    }

    private void RefreshEggTimer()
    {
        var isRunning = eggTimer.IsRunning;
        foreach (var chip in eggPresetChips)
        {
            chip.Visible = !isRunning;
        }

        eggCountdownLabel.Visible = isRunning;
        eggCancelChip.Visible = isRunning;
        var remaining = eggTimer.Remaining;
        eggCountdownLabel.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        RefreshSubtitle();
    }

    private void OnEggTimerFinished()
    {
        EggTimerFinished?.Invoke();
        _ = PlayEggCelebration();
    }

    private async Task PlayEggCelebration()
    {
        isCelebrating = true;
        screenSync.IsSuspended = true;
        try
        {
            await LightShow.PlayCelebration(ConnectedLights.ToList());
        }
        finally
        {
            screenSync.IsSuspended = false;
            isCelebrating = false;
        }
    }

    private void StartLight(SavedLight savedLight)
    {
        var light = new HueLight(savedLight);
        activeLights.Add(light);

        var panel = new LightPanel(light, RemoveLight);
        panel.LayoutHeightChanged += FitToContent;
        panelHost.Controls.Add(panel);

        light.StateChanged += _ => BeginInvokeIfCreated(RefreshMaster);
        light.ConnectionChanged += () => BeginInvokeIfCreated(() =>
        {
            RefreshMaster();
            sceneScheduler.ApplyDue();
            RefreshAutoSubtitle();
        });

        light.StartKeepingConnected();
        RefreshMaster();
    }

    private void RemoveLight(SavedLight savedLight)
    {
        var light = activeLights.FirstOrDefault(active => active.SavedLight == savedLight);
        if (light is null || !ConfirmRemoval(savedLight))
        {
            return;
        }

        light.Dispose();
        activeLights.Remove(light);
        sceneScheduler.Forget(light);
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

    private bool ConfirmRemoval(SavedLight savedLight)
    {
        isAutoHideSuppressed = true;
        try
        {
            var answer = MessageBox.Show(
                this,
                $"Remove \"{savedLight.Name}\" from Hue Desk?\n\nTo add it again later, factory reset the light first.",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            return answer == DialogResult.Yes;
        }
        finally
        {
            isAutoHideSuppressed = false;
        }
    }

    public void ShowAddLightDialog()
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

    private void ShowTab(int tabIndex)
    {
        panelHost.Visible = tabIndex == LightsTabIndex;
        scenesPage.Visible = tabIndex == ScenesTabIndex;
        toolsPage.Visible = tabIndex == ToolsTabIndex;
        FitToContent();
    }

    private int MeasureContentHeight()
    {
        if (tabSwitch.SelectedIndex == ScenesTabIndex)
        {
            return ScenesPageHeight;
        }

        if (tabSwitch.SelectedIndex == ToolsTabIndex)
        {
            return ToolsPageHeight;
        }

        var lightsHeight = panelHost.Padding.Vertical;
        foreach (Control child in panelHost.Controls)
        {
            lightsHeight += child.Height + child.Margin.Vertical;
        }

        return lightsHeight;
    }

    private void FitToContent()
    {
        var contentHeight = MeasureContentHeight();

        var workingArea = Screen.FromControl(this).WorkingArea;
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

    private void RefreshMaster()
    {
        var connectedLights = ConnectedLights.ToList();
        lightsSummary = activeLights.Count == 0
            ? "No lights yet"
            : $"{connectedLights.Count} of {activeLights.Count} lights connected";
        RefreshSubtitle();

        var knownStates = connectedLights.Select(light => light.LastState).OfType<LightState>().ToList();
        masterToggle.SetCheckedSilently(knownStates.Any(state => state.IsOn));
        masterToggle.Enabled = connectedLights.Count > 0;
        masterBrightness.Enabled = connectedLights.Count > 0;
        if (knownStates.Count > 0)
        {
            masterBrightness.SetValueSilently((int)knownStates.Average(state => state.Brightness));
        }
    }

    private void RefreshSubtitle()
    {
        var remaining = eggTimer.Remaining;
        subtitleLabel.ForeColor = eggTimer.IsRunning ? Theme.Accent : Theme.TextSecondary;
        subtitleLabel.Text = eggTimer.IsRunning
            ? $"Egg timer {(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}"
            : lightsSummary;
    }

    private void ChangeConnectionOfAll(bool shouldBeConnected)
    {
        foreach (var light in activeLights)
        {
            if (shouldBeConnected)
            {
                light.StartKeepingConnected();
                continue;
            }

            light.Release();
        }
    }

    private void SetPowerOfAll(bool isOn)
    {
        foreach (var light in ConnectedLights)
        {
            _ = LightCommands.RunSafely(light, () => light.SetPower(isOn));
        }
    }

    private void SendMasterBrightness()
    {
        masterBrightnessTimer.Stop();
        var brightness = (byte)masterBrightness.Value;
        foreach (var light in ConnectedLights)
        {
            _ = LightCommands.RunSafely(light, () => light.SetBrightness(brightness));
        }
    }

    private void ApplySceneToAll(Scene scene)
    {
        var lights = ConnectedLights.ToList();
        sceneScheduler.MarkCurrentSceneApplied(lights);
        foreach (var light in lights)
        {
            _ = LightCommands.RunSafely(light, () => light.ApplyScene(scene));
        }
    }

    private void RefreshAutoSubtitle()
    {
        var schedule = string.Join(" · ", Scenes.All.Where(scene => scene.AutoStart is not null).OrderBy(scene => scene.AutoStart).Select(scene => $"{scene.Name} {scene.AutoStart:HH}"));
        autoSubtitleLabel.Text = sceneScheduler.IsEnabled
            ? $"Now: {sceneScheduler.CurrentScene.Name}  ·  {schedule}"
            : "Off: lights stay as you leave them";
    }
}
