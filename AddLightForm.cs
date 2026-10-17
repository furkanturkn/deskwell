using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace HueDesk;

public sealed class AddLightForm : Form
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(10);

    private readonly ListBox foundLightsListBox = new() { Dock = DockStyle.Fill, DisplayMember = nameof(FoundLight.Display) };
    private readonly Label statusLabel = new() { Dock = DockStyle.Bottom, Height = Theme.Scale(44), ForeColor = Color.Gainsboro, Padding = new Padding(Theme.Scale(8), Theme.Scale(4), Theme.Scale(8), Theme.Scale(4)) };
    private readonly Button scanButton = new() { Text = "Scan", Dock = DockStyle.Bottom, Height = Theme.Scale(32) };
    private readonly Button addButton = new() { Text = "Pair and add selected light", Dock = DockStyle.Bottom, Height = Theme.Scale(32) };
    private readonly HashSet<ulong> knownAddresses;

    public AddLightForm(IEnumerable<ulong> alreadySavedAddresses)
    {
        knownAddresses = alreadySavedAddresses.ToHashSet();
        Text = "Add a light";
        Size = new Size(Theme.Scale(380), Theme.Scale(380));
        Font = new Font("Segoe UI", 9f * Theme.UiScale);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(32, 32, 36);
        ForeColor = Color.White;

        Controls.Add(foundLightsListBox);
        Controls.Add(statusLabel);
        Controls.Add(addButton);
        Controls.Add(scanButton);
        scanButton.Click += async (_, _) => await Scan();
        addButton.Click += async (_, _) => await PairSelected();
        Shown += async (_, _) => await Scan();
    }

    public SavedLight? AddedLight { get; private set; }

    private async Task Scan()
    {
        scanButton.Enabled = false;
        addButton.Enabled = false;
        foundLightsListBox.Items.Clear();
        statusLabel.Text = "Scanning… Make sure the light is on and not held by the phone app.";

        var found = new Dictionary<ulong, string>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, args) =>
        {
            if (!args.Advertisement.ServiceUuids.Contains(HueProtocol.AdvertisedServiceUuid))
            {
                return;
            }

            lock (found)
            {
                found[args.BluetoothAddress] = args.Advertisement.LocalName;
            }
        };

        watcher.Start();
        await Task.Delay(ScanDuration);
        watcher.Stop();

        lock (found)
        {
            foreach (var (address, name) in found.Where(entry => !knownAddresses.Contains(entry.Key)))
            {
                foundLightsListBox.Items.Add(new FoundLight(address, string.IsNullOrWhiteSpace(name) ? "Hue light" : name));
            }
        }

        statusLabel.Text = foundLightsListBox.Items.Count == 0 ? "No new Hue lights found." : "Select a light and pair it.";
        scanButton.Enabled = true;
        addButton.Enabled = true;
    }

    private async Task PairSelected()
    {
        if (foundLightsListBox.SelectedItem is not FoundLight selected)
        {
            statusLabel.Text = "Select a light first.";
            return;
        }

        addButton.Enabled = false;
        statusLabel.Text = "Pairing…";
        try
        {
            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(selected.BluetoothAddress)
                ?? throw new InvalidOperationException("Light is not reachable.");
            if (!device.DeviceInformation.Pairing.IsPaired)
            {
                var customPairing = device.DeviceInformation.Pairing.Custom;
                customPairing.PairingRequested += (_, requested) => requested.Accept();
                var result = await customPairing.PairAsync(DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.Encryption);
                if (result.Status != DevicePairingResultStatus.Paired)
                {
                    throw new InvalidOperationException($"Pairing failed ({result.Status}). Factory reset the light in the Hue app first.");
                }
            }

            AddedLight = new SavedLight(selected.Name, selected.BluetoothAddress);
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception)
        {
            Logger.Write($"Pairing {selected.Name} failed", exception);
            statusLabel.Text = exception.Message;
            addButton.Enabled = true;
        }
    }

    private sealed record FoundLight(ulong BluetoothAddress, string Name)
    {
        public string Display => $"{Name}  ({BluetoothAddress:X12})";
    }
}
