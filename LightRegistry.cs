using System.Text.Json;

namespace HueDesk;

public sealed record SavedLight(string Name, ulong BluetoothAddress);

public sealed class LightRegistry
{
    private const string RegistryFileName = "lights.json";

    private static readonly string RegistryFilePath = Path.Combine(AppPaths.DataDirectory, RegistryFileName);
    private readonly List<SavedLight> lights;

    private LightRegistry(List<SavedLight> lights)
    {
        this.lights = lights;
    }

    public IReadOnlyList<SavedLight> Lights => lights;

    public static LightRegistry Load()
    {
        if (!File.Exists(RegistryFilePath))
        {
            return new LightRegistry([]);
        }

        var savedLights = JsonSerializer.Deserialize<List<SavedLight>>(File.ReadAllText(RegistryFilePath));
        return new LightRegistry(savedLights ?? []);
    }

    public void Add(SavedLight light)
    {
        lights.RemoveAll(existing => existing.BluetoothAddress == light.BluetoothAddress);
        lights.Add(light);
        Save();
    }

    public void Remove(ulong bluetoothAddress)
    {
        lights.RemoveAll(existing => existing.BluetoothAddress == bluetoothAddress);
        Save();
    }

    private void Save()
    {
        File.WriteAllText(RegistryFilePath, JsonSerializer.Serialize(lights, new JsonSerializerOptions { WriteIndented = true }));
    }
}
