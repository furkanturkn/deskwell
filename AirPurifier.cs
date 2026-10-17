using System.Text.Json;
using System.Text.Json.Nodes;

namespace HueDesk;

public sealed record PurifierConfig(string Ip, string Token)
{
    private const string ConfigFileName = "purifier.json";

    private static readonly string ConfigFilePath = Path.Combine(AppPaths.DataDirectory, ConfigFileName);

    public static string FilePath => ConfigFilePath;

    public static PurifierConfig? Load()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return null;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<PurifierConfig>(File.ReadAllText(ConfigFilePath), options);
        return config is { Ip.Length: > 0, Token.Length: > 0 } ? config : null;
    }
}

public enum PurifierMode
{
    Auto = 0,
    Sleep = 1,
    Manual = 2,
}

public sealed record PurifierState(bool IsOn, PurifierMode? Mode, int? ParticleDensity, int? FilterLifePercent, int? FanLevel);

public sealed class AirPurifier : IDisposable
{
    public const int MinimumFanLevel = 1;
    public const int MaximumFanLevel = 14;

    private const int StatusCodeSuccess = 0;
    private const int ProbeMaximumService = 12;
    private const int ProbeMaximumProperty = 16;

    private static readonly PropertyAddress Power = new(2, 1);
    private static readonly PropertyAddress Mode = new(2, 4);
    private static readonly PropertyAddress ParticleDensity = new(3, 4);
    private static readonly PropertyAddress FilterLife = new(4, 1);
    private static readonly PropertyAddress FanLevel = new(9, 11);

    private readonly MiioClient client;

    public AirPurifier(PurifierConfig config)
    {
        client = new MiioClient(config.Ip, config.Token);
    }

    public async Task<PurifierState> ReadState()
    {
        var values = await ReadProperties([Power, Mode, ParticleDensity, FilterLife, FanLevel]);
        var isOn = values[0]?.GetValue<bool>() ?? throw new InvalidDataException("The purifier did not report its power state.");
        var modeValue = values[1]?.GetValue<int>();
        return new PurifierState(
            isOn,
            modeValue is { } mode && Enum.IsDefined(typeof(PurifierMode), mode) ? (PurifierMode)mode : null,
            values[2]?.GetValue<int>(),
            values[3]?.GetValue<int>(),
            values[4]?.GetValue<int>());
    }

    public Task SetPower(bool isOn)
    {
        return WriteProperty(Power, JsonValue.Create(isOn));
    }

    public Task SetMode(PurifierMode mode)
    {
        return WriteProperty(Mode, JsonValue.Create((int)mode));
    }

    public Task SetFanLevel(int level)
    {
        return WriteProperty(FanLevel, JsonValue.Create(Math.Clamp(level, MinimumFanLevel, MaximumFanLevel)));
    }

    public async Task ProbeAllProperties()
    {
        for (var service = 1; service <= ProbeMaximumService; service++)
        {
            var addresses = Enumerable.Range(1, ProbeMaximumProperty).Select(property => new PropertyAddress(service, property)).ToList();
            var results = await client.Call("get_properties", BuildGetParameters(addresses));
            foreach (var result in results.AsArray().Where(result => result?["code"]?.GetValue<int>() == StatusCodeSuccess))
            {
                Logger.Write($"purifier probe siid={result!["siid"]} piid={result["piid"]} value={result["value"]?.ToJsonString()}");
            }
        }
    }

    public void Dispose()
    {
        client.Dispose();
    }

    private async Task<List<JsonNode?>> ReadProperties(IReadOnlyList<PropertyAddress> addresses)
    {
        var results = await client.Call("get_properties", BuildGetParameters(addresses));
        return addresses
            .Select(address => results.AsArray().FirstOrDefault(result => result?["siid"]?.GetValue<int>() == address.Service && result["piid"]?.GetValue<int>() == address.Property))
            .Select(result => result?["code"]?.GetValue<int>() == StatusCodeSuccess ? result["value"] : null)
            .ToList();
    }

    private async Task WriteProperty(PropertyAddress address, JsonNode value)
    {
        var parameters = new JsonArray(new JsonObject
        {
            ["did"] = "purifier",
            ["siid"] = address.Service,
            ["piid"] = address.Property,
            ["value"] = value,
        });
        var results = await client.Call("set_properties", parameters);
        var code = results.AsArray().FirstOrDefault()?["code"]?.GetValue<int>();
        if (code != StatusCodeSuccess)
        {
            throw new InvalidOperationException($"The purifier rejected siid={address.Service} piid={address.Property} with code {code}.");
        }
    }

    private static JsonArray BuildGetParameters(IEnumerable<PropertyAddress> addresses)
    {
        var parameters = new JsonArray();
        foreach (var address in addresses)
        {
            parameters.Add(new JsonObject { ["did"] = "purifier", ["siid"] = address.Service, ["piid"] = address.Property });
        }

        return parameters;
    }

    private readonly record struct PropertyAddress(int Service, int Property);
}
