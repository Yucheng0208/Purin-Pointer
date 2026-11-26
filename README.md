# Purin Pointer

**Animated cursor studio for Windows** — import `.ani`, `.cur`, PNG, GIF or a folder of images, remove the background, set size and hotspot, and apply it as your Windows pointer.

[繁體中文](README.tw.md) · English

## Features

- **Any source** — Windows `.ani` / `.cur` / `.ico`, a single PNG/JPG/BMP, animated GIF, or a folder of numbered images (sorted like Explorer: `2.png` before `10.png`).
- **All 17 Windows pointers** — Normal Select, Link Select, Text Select, Busy, Working in Background and the rest.
- **Background removal** — remove a flat edge background, or use the magic wand to erase a clicked colour (applied to every frame).
- **Hotspot** — click the preview or type X/Y.
- **Speed** — 0.25× to 4× for animations.
- **Undo** — every apply is backed up; *Undo last apply* restores the previous state. *Reset all* returns to the Windows default scheme.
- **Five languages** — 繁體中文, 简体中文, English, 日本語, 한국어 (follows the system language by default).
- **No administrator rights** — pointer settings are per-user (`HKCU\Control Panel\Cursors`), so there is no UAC prompt.

## Download and run

1. Download `PurinPointer-win-x64.zip` (or `win-arm64`) from **Releases**.
2. Extract it and run `PurinPointer.exe`. No installation needed.
3. If **Windows protected your PC** appears, click **More info → Run anyway**. (Or right-click the ZIP → **Properties** → tick **Unblock** before extracting.)

> [!IMPORTANT]
> The release is **not code-signed**. On Windows 11 with **Smart App Control** turned **on**, unsigned apps are blocked outright, with no "Run anyway" button. In that case build it yourself (below) or wait for a signed release.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) or Visual Studio 2026 with the *.NET desktop development* workload.

- **Visual Studio:** open `PurinPointer.sln`, set `PurinPointer` as the startup project, press **F5**.
- **Command line:**

```powershell
dotnet run --project src/PurinPointer                 # run the app
dotnet run --project tests/PurinPointer.Tests         # run the tests (any OS)
dotnet publish src/PurinPointer -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

To release: run the `dotnet publish` command above (use `-r win-arm64` for ARM), zip `publish\PurinPointer.exe` as `PurinPointer-win-x64.zip`, and upload it to a new GitHub release by hand.

## Where data lives

`%LOCALAPPDATA%\PurinPointer\`

| Path | Contents |
| --- | --- |
| `cursors\` | Generated `.cur` / `.ani` files the registry points to |
| `backups\` | Pointer settings from before each apply (used by *Undo*) |
| `settings.json` | Language |

## Project layout

```text
PurinPointer.sln
src/PurinPointer.Core/     # cursor formats, image processing, strings (cross-platform, no WPF)
src/PurinPointer/          # WPF app: UI, image decoding, registry
tests/PurinPointer.Tests/  # dependency-free test runner
```

## License

[GPL-3.0](LICENSE)
