# AMD Chipset Updater

Small Windows app that installs and updates AMD chipset drivers straight from amd.com.

## Output (`dist\`)

| File | What it is |
|---|---|
| `AMDChipsetUpdater.exe` | The program (single exe, ~55 KB) |
| `AMDChipsetUpdater-Portable-1.0.0.zip` | Portable version: exe + README |
| `AMDChipsetUpdater-Setup-1.0.0.exe` | Installer (per-user, no admin rights needed) |

## How it works

1. **Detection** – reads the CPU (WMI `Win32_Processor`, family/model) to find the socket
   (AM4, AM5, sTR5, sWRX8, sTRX4, TR4, or notebook), then reads the chipset from the
   motherboard name (`Win32_BaseBoard`, e.g. "ROG STRIX B550-F" → B550).
2. **Latest version** – opens the matching page
   `amd.com/en/support/downloads/drivers.html/chipsets/<socket>/<chipset>.html` and takes the
   `amd_chipset_software_<version>.exe` link. Falls back to the socket's default page.
3. **Installed version** – `DisplayVersion` of "AMD Chipset Software" in the Uninstall registry keys.
4. **Status** – `Updated` or `Update available` (or `Not installed`).
5. **Install / Update** – downloads with the `Referer: https://www.amd.com/` header (required by
   AMD), checks the Authenticode signature is valid and from *Advanced Micro Devices*,
   then runs the installer elevated (UAC).

## Startup check

`AMDChipsetUpdater.exe --check` (registered in `HKCU\...\Run` by the checkbox or the installer):
no window, below-normal priority, retries up to 3× if the network isn't ready, shows a Windows
notification only if an update is available, then exits.
Exit codes: `0` up to date, `1` notified, `2` error. `--check --force-notify` always notifies (testing).

## Build

Needs only Windows' built-in C# compiler (.NET Framework 4.8). Inno Setup 6 is required for the installer
(`winget install JRSoftware.InnoSetup`).

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Source

- `src\HardwareDetector.cs` – CPU/board detection and platform → AMD page mapping
- `src\AmdDriverSource.cs` – page parsing and download
- `src\InstalledDriver.cs` – installed version from the registry
- `src\SignatureVerifier.cs` – Authenticode check (WinVerifyTrust)
- `src\UpdateChecker.cs` – ties the above together
- `src\MainForm.cs` – UI
- `src\BackgroundCheck.cs` – `--check` mode and notification
- `src\Autostart.cs` – startup entry
- `installer\setup.iss` – Inno Setup script

## License

[MIT](LICENSE). Unofficial tool, not affiliated with AMD. AMD drivers are downloaded from amd.com
and remain subject to AMD's own license terms.
