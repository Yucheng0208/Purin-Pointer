# Purin Pointer

**Windows 動畫游標工作室**：AniOnMac 的 Windows 版。匯入 `.ani`、`.cur`、PNG、GIF 或圖片資料夾，一鍵去背、調整定位點，打造你的專屬游標。

**繁體中文** · [English](README.md)

## 特色

- **主題**：可建立任意數量的主題，新增、複製、刪除都在左側欄；目前套用中的主題會標示「使用中」，所有設定自動儲存。
- **4 種模式＋更多指標**：普通游標、連結指向、文字選取、等待狀態（同時套用到 Windows 的「忙碌」與「在背景作業」）；其餘 12 種 Windows 指標在「更多指標」選單裡。只套用已填入的模式，其他維持目前狀態。
- **什麼素材都能用**：`.ani`／`.cur`／`.ico`、單張 PNG／JPG／BMP、動態 GIF，或依編號命名的圖片資料夾（排序和檔案總管一樣，`2.png` 在 `10.png` 前面），也可以直接貼上路徑。
- **時間軸**：多張圖片會先開時間軸，可拖曳排序、◀ ▶ 微調、✕ 刪除、設定每格時間並即時預覽。
- **去背**：「自動去背」移除與邊緣相連的底色；「魔術棒」在放大的預覽上點要移除的顏色，套用到每一幀；可「還原原圖」。
- **定位點**：在預覽上拖曳，或輸入 X／Y。
- **Windows 指標大小**：與「設定 › 協助工具 › 滑鼠指標」相同的全域大小（32–256 px）。
- **復原**：「復原上次變更」回到套用或編輯之前；「恢復 Windows 原生外觀」把全部指標與大小改回預設。
- **外觀與語言**：跟隨系統／淺色／深色（布丁色系）；繁體中文、简体中文、English、日本語、한국어。
- **檢查更新**：比對 GitHub 上的最新版本。
- **不需要管理員權限**：鼠標設定是每個使用者各自的（`HKCU\Control Panel\Cursors`），不會跳出 UAC。

## 下載與執行

1. 到 **Releases** 下載 `PurinPointer-win-x64.zip`（ARM 電腦請下載 `win-arm64`）。
2. 解壓縮後執行 `PurinPointer.exe`，不需要安裝。
3. 如果出現「**Windows 已保護您的電腦**」，點「**其他資訊 → 仍要執行**」。也可以在解壓縮前，對 ZIP 按右鍵 →「**內容**」→ 勾選「**解除封鎖**」。

> [!IMPORTANT]
> 發佈版本**沒有程式碼簽章**。Windows 11 開啟「**智慧型應用程式控制**」時，未簽章的程式會被直接封鎖，沒有「仍要執行」可以點。遇到這種情況，請照下方說明自行建置，或等待有簽章的版本。

## 從原始碼建置

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)，或安裝了「.NET 桌面開發」工作負載的 Visual Studio 2026。

- **Visual Studio**：開啟 `PurinPointer.sln`，把 `PurinPointer` 設為啟動專案，按 **F5**。
- **命令列**：

```powershell
dotnet run --project src/PurinPointer                 # 執行程式
dotnet run --project tests/PurinPointer.Tests         # 執行測試（任何作業系統都可以）
dotnet publish src/PurinPointer -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

發佈方式：執行上面的 `dotnet publish` 指令（ARM 版改用 `-r win-arm64`），把 `publish\PurinPointer.exe` 壓縮成 `PurinPointer-win-x64.zip`，再手動上傳到 GitHub 的新 Release。

## 資料存放位置

`%LOCALAPPDATA%\PurinPointer\`

| 路徑 | 內容 |
| --- | --- |
| `library.json` | 主題、語言、外觀、使用中的主題 |
| `assets\` | 匯入的游標副本（原檔可以移動或刪除） |
| `cursors\` | 套用時產生的 `.cur`／`.ani`，登錄檔會指向這裡 |
| `backups\` | 每個主題套用前的指標設定（「復原上次變更」會用到） |
| `native-reset-recovery.json` | 上次「恢復 Windows 原生外觀」之前的指標設定 |

## 授權

[GPL-3.0](LICENSE)
