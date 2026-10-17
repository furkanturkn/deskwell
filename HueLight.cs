using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HueDesk;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

public sealed record LightState(bool IsOn, byte Brightness, ushort TemperatureMirek);
public sealed class HueLight : IDisposable
{
    private static readonly TimeSpan MinimumRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(8);

    private readonly CancellationTokenSource lifetime = new();
    private GattCharacteristic? powerCharacteristic;
    private GattCharacteristic? brightnessCharacteristic;
    private GattCharacteristic? temperatureCharacteristic;
    private GattCharacteristic? colorCharacteristic;

    public HueLight(SavedLight savedLight)
    {
        SavedLight = savedLight;
    }

    public event Action? ConnectionChanged;

    public event Action<LightState>? StateChanged;

    public SavedLight SavedLight { get; }

    public ConnectionState Connection { get; private set; } = ConnectionState.Disconnected;

    public string? LastError { get; private set; }

    public LightState? LastState { get; private set; }

    public void StartKeepingConnected()
    {
        _ = Task.Run(() => KeepConnectedAsync(lifetime.Token));
    }

    public async Task SetPowerAsync(bool isOn)
    {
        await WriteAsync(powerCharacteristic, [isOn ? (byte)1 : (byte)0]);
        UpdateState(state => state with { IsOn = isOn });
    }

    public async Task SetBrightnessAsync(byte brightness)
    {
        var clampedBrightness = Math.Clamp(brightness, HueProtocol.MinimumBrightness, HueProtocol.MaximumBrightness);
        await WriteAsync(brightnessCharacteristic, [clampedBrightness]);
        UpdateState(state => state with { Brightness = clampedBrightness });
    }

    public async Task SetTemperatureAsync(ushort temperatureMirek)
    {
        var clampedTemperature = Math.Clamp(temperatureMirek, HueProtocol.MinimumTemperatureMirek, HueProtocol.MaximumTemperatureMirek);
        await WriteAsync(temperatureCharacteristic, BitConverter.GetBytes(clampedTemperature));
        UpdateState(state => state with { TemperatureMirek = clampedTemperature });
    }

    public Task SetColorAsync(double hue, double saturation)
    {
        var (chromaticityX, chromaticityY) = HueColor.ToChromaticity(hue, saturation);
        var value = new byte[4];
        BitConverter.TryWriteBytes(value.AsSpan(0, 2), (ushort)Math.Round(chromaticityX * ushort.MaxValue));
        BitConverter.TryWriteBytes(value.AsSpan(2, 2), (ushort)Math.Round(chromaticityY * ushort.MaxValue));
        return WriteAsync(colorCharacteristic, value);
    }

    public async Task ApplySceneAsync(Scene scene)
    {
        await SetPowerAsync(true);
        await SetBrightnessAsync(scene.Brightness);
        if (scene.TemperatureMirek is { } temperatureMirek)
        {
            await SetTemperatureAsync(temperatureMirek);
        }
        else if (scene.Hue is { } hue && scene.Saturation is { } saturation)
        {
            await SetColorAsync(hue, saturation);
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
    }

    private async Task KeepConnectedAsync(CancellationToken cancellationToken)
    {
        var retryDelay = MinimumRetryDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndWaitForDisconnectAsync(cancellationToken);
                retryDelay = MinimumRetryDelay;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Logger.Write($"{SavedLight.Name}: connection attempt failed", exception);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaximumRetryDelay.TotalSeconds));
            }

            ClearCharacteristics();
            SetConnection(ConnectionState.Disconnected);
            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ConnectAndWaitForDisconnectAsync(CancellationToken cancellationToken)
    {
        SetConnection(ConnectionState.Connecting);

        using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(SavedLight.BluetoothAddress)
            ?? throw new InvalidOperationException("Light is not reachable.");
        using var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
        session.MaintainConnection = true;

        var servicesResult = await device.GetGattServicesForUuidAsync(HueProtocol.ControlServiceUuid, BluetoothCacheMode.Uncached);
        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
        {
            throw new InvalidOperationException($"Control service unavailable ({servicesResult.Status}).");
        }

        using var service = servicesResult.Services[0];
        var characteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        if (characteristicsResult.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Characteristics unavailable ({characteristicsResult.Status}).");
        }

        powerCharacteristic = FindCharacteristic(characteristicsResult, HueProtocol.PowerCharacteristicUuid);
        brightnessCharacteristic = FindCharacteristic(characteristicsResult, HueProtocol.BrightnessCharacteristicUuid);
        temperatureCharacteristic = FindCharacteristic(characteristicsResult, HueProtocol.TemperatureCharacteristicUuid);
        colorCharacteristic = characteristicsResult.Characteristics.FirstOrDefault(characteristic => characteristic.Uuid == HueProtocol.ColorCharacteristicUuid);

        var initialState = await ReadStateAsync();
        LastError = null;
        LastState = initialState;
        SetConnection(ConnectionState.Connected);
        StateChanged?.Invoke(initialState);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnConnectionStatusChanged(BluetoothLEDevice changedDevice, object changeArguments)
        {
            if (changedDevice.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                disconnected.TrySetResult();
            }
        }

        device.ConnectionStatusChanged += OnConnectionStatusChanged;
        try
        {
            await disconnected.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }

    private async Task<LightState> ReadStateAsync()
    {
        var powerBytes = await ReadAsync(powerCharacteristic);
        var brightnessBytes = await ReadAsync(brightnessCharacteristic);
        var temperatureBytes = await ReadAsync(temperatureCharacteristic);
        return new LightState(powerBytes[0] != 0, brightnessBytes[0], BitConverter.ToUInt16(temperatureBytes, 0));
    }

    private static GattCharacteristic FindCharacteristic(GattCharacteristicsResult result, Guid uuid)
    {
        return result.Characteristics.FirstOrDefault(characteristic => characteristic.Uuid == uuid)
            ?? throw new InvalidOperationException($"Characteristic {uuid} not found.");
    }

    private static async Task<byte[]> ReadAsync(GattCharacteristic? characteristic)
    {
        var target = characteristic ?? throw new InvalidOperationException("Light is not connected.");
        var result = await target.ReadValueAsync(BluetoothCacheMode.Uncached);
        if (result.Status == GattCommunicationStatus.Success)
        {
            return result.Value.ToArray();
        }

        throw new InvalidOperationException(DescribeFailure("Read", result.Status, result.ProtocolError));
    }

    private static async Task WriteAsync(GattCharacteristic? characteristic, byte[] value)
    {
        var target = characteristic ?? throw new InvalidOperationException("Light is not connected.");
        var result = await target.WriteValueWithResultAsync(value.AsBuffer(), GattWriteOption.WriteWithResponse);
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException(DescribeFailure("Write", result.Status, result.ProtocolError));
        }
    }

    private static string DescribeFailure(string operation, GattCommunicationStatus status, byte? protocolError)
    {
        return protocolError == HueProtocol.InsufficientEncryptionProtocolError
            ? $"{operation} refused: the light is not paired with this PC."
            : $"{operation} failed ({status}).";
    }

    private void UpdateState(Func<LightState, LightState> change)
    {
        if (LastState is null)
        {
            return;
        }

        LastState = change(LastState);
        StateChanged?.Invoke(LastState);
    }

    private void ClearCharacteristics()
    {
        colorCharacteristic = null;
        powerCharacteristic = null;
        brightnessCharacteristic = null;
        temperatureCharacteristic = null;
    }

    private void SetConnection(ConnectionState connection)
    {
        if (Connection == connection)
        {
            return;
        }

        Connection = connection;
        ConnectionChanged?.Invoke();
    }
}
