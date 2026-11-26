# Purin Pointer

**Animated cursor studio for Windows** — the Windows edition of AniOnMac. Import `.ani`, `.cur`, PNG, GIF or a folder of images, remove the background in one click, tune the hotspot and make the cursor truly yours.

[繁體中文](README.tw.md) · English

## Features

- **Themes** — as many as you like; add, duplicate and delete them in the sidebar. The applied theme is tagged *In use*, and everything saves automatically.
- **4 modes + more pointers** — Normal, Link, Text and Busy (Busy drives both Windows' *Busy* and *Working in Background*); the other 12 Windows pointers live under *More pointers*. Only filled modes are applied; the rest stay as they are.
- **Any source** — `.ani` / `.cur` / `.ico`, a single PNG/JPG/BMP, an animated GIF, or a folder of numbered images (sorted like Explorer: `2.png` before `10.png`), or paste a path.
- **Timeline** — several images open on a timeline first: drag to reorder, nudge with ◀ ▶, drop frames with ✕, set the frame time and preview live.
- **Background removal** — *Auto remove* clears the background colour touching the edges; the *magic wand* erases a clicked colour on every frame; *Restore original* undoes it.
- **Hotspot** — drag it on the preview or type X/Y.
- **Windows pointer size** — the same global size as Settings › Accessibility › Mouse pointer (32–256 px).
- **Undo** — *Undo last change* goes back to before the last apply or edit; *Restore Windows appearance* puts every pointer and the size back to the defaults.
- **Appearance and language** — follow system / light / dark (pudding colours); 繁體中文, 简体中文, English, 日本語, 한국어.
- **Update check** — compares with the latest release on GitHub.
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
| `library.json` | Themes, language, appearance and the theme in use |
| `assets\` | Copies of imported cursors (the originals can be moved or deleted) |
| `cursors\` | `.cur` / `.ani` files written on apply; the registry points here |
| `backups\` | Each theme's pointer settings from before it was applied (used by *Undo last change*) |
| `native-reset-recovery.json` | Pointer settings from before the last *Restore Windows appearance* |

## Project layout

```text
PurinPointer.sln
src/PurinPointer.Core/     # themes, cursor formats, image processing, strings (cross-platform, no WPF)
src/PurinPointer/          # WPF app: UI (ported from AniOnMac), image decoding, registry
tests/PurinPointer.Tests/  # dependency-free test runner
```

## License

[GPL-3.0](LICENSE)
