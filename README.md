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

## Connecting your devices

### Philips Hue Bluetooth lights

Needs a PC with Bluetooth Low Energy. No Hue Bridge is involved.

1. A light talks to only one device at a time. Close the Philips Hue phone app, or turn off Bluetooth on the phone, so it is not holding the light.
2. If the light was paired with another device before, factory reset it first. Power-cycle it five times, or reset it from the Hue app.
3. Turn the light on and keep it close to the PC.
4. Click the tray icon, open the menu in the header and choose **Add light...**.
5. Press **Scan**, select your light in the list and press **Pair and add selected light**.

Hue Desk keeps the connection open and reconnects on its own. To use the phone app again, choose **Release all lights** from the same menu, then **Reconnect all lights** when you are done.

If pairing fails with a message about a factory reset, repeat step 2.

### Xiaomi air purifier (optional)

The **Air** tab controls a Xiaomi air purifier over your local network using the miIO protocol. It was built against a `xiaomi.airp.cpa4`; other models may use different properties and may not work.

1. Add the purifier to the Mi Home app and connect it to the same Wi-Fi network as the PC.
2. Find the purifier's IP address in your router, and give it a fixed address (DHCP reservation) so it does not change.
3. Get the device token, a 32-character hexadecimal string. Mi Home does not show it. A community tool such as [Xiaomi Cloud Tokens Extractor](https://github.com/PiotrMachowski/Xiaomi-cloud-tokens-extractor) can read it from your Mi account. The token gives full local control of the device, so keep it private.
4. Create `%APPDATA%\HueDesk\purifier.json`:

   ```json
   { "ip": "192.168.1.50", "token": "00112233445566778899aabbccddeeff" }
   ```

5. Restart Hue Desk. The Air tab now shows the purifier.

The PC must be able to reach the purifier on UDP port 54321. If the tab says it cannot reach the purifier, check the IP address and token, and see `log.txt` in `%APPDATA%\HueDesk`.

## Disclaimer

Hue Desk is an independent project. It is not affiliated with, endorsed by or sponsored by Signify or Philips. "Philips" and "Hue" are trademarks of their respective owners and are used here only to describe which lights the app works with. The Bluetooth protocol it uses is unofficial and undocumented, so use the app at your own risk.

## License

[MIT](LICENSE)
