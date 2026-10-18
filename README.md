# Hue Desk

Windows tray app that stays connected to Philips Hue Bluetooth lights (no Bridge needed).

- Power, brightness, warmth and color per light, plus all-lights controls
- Scenes: Morning, Work, Gaming, Relax, Night; optional automatic scene by time of day
- Global hotkey `Ctrl+Alt+L` toggles the panel; starts with Windows

## Build

```bash
dotnet build -c Release
```

Output: `bin/Release/net10.0-windows10.0.19041.0/HueDesk.exe`. Data lives in `%APPDATA%\HueDesk`.

## Pairing

A light bonds to one device. Factory reset it (power-cycle five times), close the phone app, then use **Add light** in Hue Desk.

## Disclaimer

Hue Desk is an independent project. It is not affiliated with, endorsed by or sponsored by Signify or Philips. "Philips" and "Hue" are trademarks of their respective owners and are used here only to describe which lights the app works with. The Bluetooth protocol it uses is unofficial and undocumented, so use the app at your own risk.

## License

[MIT](LICENSE)
