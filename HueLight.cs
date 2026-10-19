using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace Deskwell;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Released,
}

public enum LightWriteMode
{
    Confirmed,
    Fast,
}

public enum LightMode
{
    White,
    Color,
}

public sealed record LightState(bool IsOn, byte Brightness, ushort TemperatureMirek);

public sealed record LightColor(double Hue, double Saturation);

public sealed record LightSnapshot(LightState State, LightMode Mode, LightColor? Color);

public sealed class HueLight : IDisposable
{
    private static readonly TimeSpan MinimumRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(8);

    private CancellationTokenSource? connectionLoop;
    private bool isReleased;
    private DateTime connectedSince;
    private LightSnapshot? stateLostWhileDisconnected;
    private LightSnapshot? expectedState;
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

    public bool IsConnected => Connection == ConnectionState.Connected;

    public string? LastError { get; private set; }

    public LightState? LastState { get; private set; }

    public LightMode Mode { get; private set; } = LightMode.White;

    public LightColor? LastColor { get; private set; }

    public bool IsReleased => isReleased;

    public void StartKeepingConnected()
    {
        if (connectionLoop is not null)
        {
            return;
        }

        isReleased = false;
        var loop = new CancellationTokenSource();
        connectionLoop = loop;
        _ = Task.Run(() => KeepConnected(loop.Token));
    }

    public void Release()
    {
        if (connectionLoop is null)
        {
            return;
        }

        isReleased = true;
        connectionLoop.Cancel();
        connectionLoop.Dispose();
        connectionLoop = null;
        ClearCharacteristics();
        SetConnection(ConnectionState.Released);
    }

    public async Task SetPower(bool isOn)
    {
        await Write(powerCharacteristic, [isOn ? (byte)1 : (byte)0]);
        UpdateState(state => state with { IsOn = isOn });
    }

    public async Task SetBrightness(byte brightness, LightWriteMode writeMode = LightWriteMode.Confirmed)
    {
        var clampedBrightness = Math.Clamp(brightness, HueProtocol.MinimumBrightness, HueProtocol.MaximumBrightness);
        await Write(brightnessCharacteristic, [clampedBrightness], writeMode);
        UpdateState(state => state with { Brightness = clampedBrightness });
    }

    public async Task SetTemperature(ushort temperatureMirek)
    {
        var clampedTemperature = Math.Clamp(temperatureMirek, HueProtocol.MinimumTemperatureMirek, HueProtocol.MaximumTemperatureMirek);
        await Write(temperatureCharacteristic, BitConverter.GetBytes(clampedTemperature));
        Mode = LightMode.White;
        UpdateState(state => state with { TemperatureMirek = clampedTemperature });
    }

    public async Task SetColor(double hue, double saturation, LightWriteMode writeMode = LightWriteMode.Confirmed)
    {
        var (chromaticityX, chromaticityY) = HueColor.ToChromaticity(hue, saturation);
        var value = new byte[4];
        BitConverter.TryWriteBytes(value.AsSpan(0, 2), (ushort)Math.Round(chromaticityX * ushort.MaxValue));
        BitConverter.TryWriteBytes(value.AsSpan(2, 2), (ushort)Math.Round(chromaticityY * ushort.MaxValue));
        await Write(colorCharacteristic, value, writeMode);
        LastColor = new LightColor(hue, saturation);
        Mode = LightMode.Color;
    }

    public void ExpectState(LightSnapshot snapshot)
    {
        expectedState = snapshot;
    }

    public void ClearExpectedState()
    {
        expectedState = null;
    }

    public LightSnapshot? TakeLostState()
    {
        var lostState = stateLostWhileDisconnected;
        stateLostWhileDisconnected = null;
        return lostState;
    }

    public LightSnapshot? CaptureSnapshot()
    {
        var state = LastState;
        return state is null ? null : new LightSnapshot(state, Mode, LastColor);
    }

    public async Task Restore(LightSnapshot snapshot)
    {
        await SetBrightness(snapshot.State.Brightness);
        if (snapshot.Mode == LightMode.Color && snapshot.Color is { } color)
        {
            await SetColor(color.Hue, color.Saturation);
        }
        else
        {
            await SetTemperature(snapshot.State.TemperatureMirek);
        }

        await SetPower(snapshot.State.IsOn);
    }

    public async Task ResetToDefault()
    {
        await SetPower(true);
        await SetBrightness(HueProtocol.MaximumBrightness);
        await SetTemperature(HueProtocol.DefaultTemperatureMirek);
    }

    public async Task ApplyScene(Scene scene)
    {
        await SetPower(true);
        await SetBrightness(scene.Brightness);
        if (scene.TemperatureMirek is { } temperatureMirek)
        {
            await SetTemperature(temperatureMirek);
        }
        else if (scene.Hue is { } hue && scene.Saturation is { } saturation)
        {
            await SetColor(hue, saturation);
        }
    }

    public void Dispose()
    {
        connectionLoop?.Cancel();
        connectionLoop?.Dispose();
    }

    private async Task KeepConnected(CancellationToken cancellationToken)
    {
        var retryDelay = MinimumRetryDelay;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndWaitForDisconnect(cancellationToken);
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

    private async Task ConnectAndWaitForDisconnect(CancellationToken cancellationToken)
    {
        SetConnection(ConnectionState.Connecting);

        using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(SavedLight.BluetoothAddress)
            ?? throw new InvalidOperationException("Light is not reachable.");
        using var session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);
        session.MaintainConnection = true;
        using var connectionRequest = device.RequestPreferredConnectionParameters(BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
        if (connectionRequest.Status != BluetoothLEPreferredConnectionParametersRequestStatus.Success)
        {
            Logger.Write($"{SavedLight.Name}: fast connection request returned {connectionRequest.Status}", null);
        }

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

        var initialState = await ReadState();
        LastError = null;
        stateLostWhileDisconnected = FindLostState(LastState, initialState);
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

    private LightSnapshot? FindLostState(LightState? previousState, LightState currentState)
    {
        var expected = expectedState;
        expectedState = null;
        if (expected is not null)
        {
            return expected;
        }

        if (previousState is null)
        {
            return null;
        }

        var isColorModeUnverifiable = Mode == LightMode.Color;
        return previousState == currentState && !isColorModeUnverifiable ? null : new LightSnapshot(previousState, Mode, LastColor);
    }

    private async Task<LightState> ReadState()
    {
        var powerBytes = await Read(powerCharacteristic);
        var brightnessBytes = await Read(brightnessCharacteristic);
        var temperatureBytes = await Read(temperatureCharacteristic);
        return new LightState(powerBytes[0] != 0, brightnessBytes[0], BitConverter.ToUInt16(temperatureBytes, 0));
    }

    private static GattCharacteristic FindCharacteristic(GattCharacteristicsResult result, Guid uuid)
    {
        return result.Characteristics.FirstOrDefault(characteristic => characteristic.Uuid == uuid)
            ?? throw new InvalidOperationException($"Characteristic {uuid} not found.");
    }

    private static async Task<byte[]> Read(GattCharacteristic? characteristic)
    {
        var target = characteristic ?? throw new InvalidOperationException("Light is not connected.");
        var result = await target.ReadValueAsync(BluetoothCacheMode.Uncached);
        if (result.Status == GattCommunicationStatus.Success)
        {
            return result.Value.ToArray();
        }

        throw new InvalidOperationException(DescribeFailure("Read", result.Status, result.ProtocolError));
    }

    private static async Task Write(GattCharacteristic? characteristic, byte[] value, LightWriteMode writeMode = LightWriteMode.Confirmed)
    {
        var target = characteristic ?? throw new InvalidOperationException("Light is not connected.");
        var writeOption = writeMode == LightWriteMode.Fast ? GattWriteOption.WriteWithoutResponse : GattWriteOption.WriteWithResponse;
        var result = await target.WriteValueWithResultAsync(value.AsBuffer(), writeOption);
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
        if (isReleased && connection != ConnectionState.Released)
        {
            return;
        }

        if (Connection == connection)
        {
            return;
        }

        var wasConnected = Connection == ConnectionState.Connected;
        Connection = connection;
        LogConnectionTransition(wasConnected, connection);
        ConnectionChanged?.Invoke();
    }

    private void LogConnectionTransition(bool wasConnected, ConnectionState connection)
    {
        if (connection == ConnectionState.Connected)
        {
            connectedSince = DateTime.Now;
            Logger.Write($"{SavedLight.Name}: connected");
        }
        else if (wasConnected)
        {
            Logger.Write($"{SavedLight.Name}: connection lost after {(DateTime.Now - connectedSince).TotalSeconds:0} s ({connection})");
        }
    }
}
