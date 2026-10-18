namespace Deskwell;

public static class HueProtocol
{
    public static readonly Guid AdvertisedServiceUuid = Guid.Parse("0000fe0f-0000-1000-8000-00805f9b34fb");
    public static readonly Guid ControlServiceUuid = Guid.Parse("932c32bd-0000-47a2-835a-a8d455b859dd");
    public static readonly Guid PowerCharacteristicUuid = Guid.Parse("932c32bd-0002-47a2-835a-a8d455b859dd");
    public static readonly Guid BrightnessCharacteristicUuid = Guid.Parse("932c32bd-0003-47a2-835a-a8d455b859dd");
    public static readonly Guid TemperatureCharacteristicUuid = Guid.Parse("932c32bd-0004-47a2-835a-a8d455b859dd");

    public static readonly Guid ColorCharacteristicUuid = Guid.Parse("932c32bd-0005-47a2-835a-a8d455b859dd");

    public const byte MinimumBrightness = 1;
    public const byte MaximumBrightness = 254;
    public const ushort MinimumTemperatureMirek = 153;
    public const ushort MaximumTemperatureMirek = 500;
    public const ushort DefaultTemperatureMirek = 366;
    public const byte InsufficientEncryptionProtocolError = 15;
}
