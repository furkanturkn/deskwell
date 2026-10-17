using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace HueDesk;

public enum CaptureStatus
{
    Captured,
    Unchanged,
    Unavailable,
}

public sealed class DesktopDuplicationCapture : IDisposable
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext deviceContext;
    private readonly IDXGIOutputDuplication duplication;
    private ID3D11Texture2D? stagingTexture;

    private DesktopDuplicationCapture(ID3D11Device device, ID3D11DeviceContext deviceContext, IDXGIOutputDuplication duplication)
    {
        this.device = device;
        this.deviceContext = deviceContext;
        this.duplication = duplication;
    }

    public static DesktopDuplicationCapture? TryCreate(string displayDeviceName)
    {
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, new FeatureLevel[] { FeatureLevel.Level_11_0 }, out ID3D11Device device, out ID3D11DeviceContext deviceContext).CheckError();
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var output = FindOutput(adapter, displayDeviceName);
            using var output1 = output.QueryInterface<IDXGIOutput1>();
            return new DesktopDuplicationCapture(device, deviceContext, output1.DuplicateOutput(device));
        }
        catch (Exception exception)
        {
            Logger.Write("Desktop duplication is unavailable", exception);
            return null;
        }
    }

    public (CaptureStatus Status, ScreenColor? Color) Capture()
    {
        try
        {
            return CaptureFrame();
        }
        catch (Exception exception)
        {
            Logger.Write("Desktop duplication capture failed", exception);
            return (CaptureStatus.Unavailable, null);
        }
    }

    public void Dispose()
    {
        stagingTexture?.Dispose();
        duplication.Dispose();
        deviceContext.Dispose();
        device.Dispose();
    }

    private static IDXGIOutput FindOutput(IDXGIAdapter adapter, string displayDeviceName)
    {
        IDXGIOutput? firstOutput = null;
        for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
        {
            firstOutput ??= output;
            if (output.Description.DeviceName == displayDeviceName)
            {
                return output;
            }
        }

        return firstOutput ?? throw new InvalidOperationException("No display output found.");
    }

    private (CaptureStatus Status, ScreenColor? Color) CaptureFrame()
    {
        var acquireResult = duplication.AcquireNextFrame(0, out _, out var frameResource);
        if (acquireResult == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return (CaptureStatus.Unchanged, null);
        }

        if (acquireResult.Failure)
        {
            return (CaptureStatus.Unavailable, null);
        }

        try
        {
            using var frameTexture = frameResource.QueryInterface<ID3D11Texture2D>();
            var frameDescription = frameTexture.Description;
            var staging = GetStagingTexture(frameDescription);
            deviceContext.CopyResource(staging, frameTexture);
        }
        finally
        {
            frameResource.Dispose();
            duplication.ReleaseFrame();
        }

        return (CaptureStatus.Captured, ReadStagingTexture());
    }

    private ID3D11Texture2D GetStagingTexture(Texture2DDescription frameDescription)
    {
        if (stagingTexture is { } existing && existing.Description.Width == frameDescription.Width && existing.Description.Height == frameDescription.Height)
        {
            return existing;
        }

        stagingTexture?.Dispose();
        var stagingDescription = frameDescription with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };
        var created = device.CreateTexture2D(stagingDescription);
        stagingTexture = created;
        return created;
    }

    private ScreenColor ReadStagingTexture()
    {
        var staging = stagingTexture ?? throw new InvalidOperationException("Staging texture is missing.");
        var description = staging.Description;
        var mapped = deviceContext.Map(staging, 0, MapMode.Read);
        try
        {
            return ScreenGridSampler.Average(mapped.DataPointer, (int)mapped.RowPitch, (int)description.Width, (int)description.Height);
        }
        finally
        {
            deviceContext.Unmap(staging, 0);
        }
    }
}
