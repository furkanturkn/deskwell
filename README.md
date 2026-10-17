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

## Standards

Shared coding standards come from the `.pinq-doq` submodule: clone with `git clone --recurse-submodules`.
