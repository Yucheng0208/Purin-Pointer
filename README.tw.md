# Purin Pointer

**Windows 動畫鼠標工作室**：匯入 `.ani`、`.cur`、PNG、GIF 或圖片資料夾，去除背景、設定大小和熱點，一鍵套用成 Windows 鼠標。

**繁體中文** · [English](README.md)

## 特色

- **什麼素材都能用**：Windows `.ani`／`.cur`／`.ico`、單張 PNG／JPG／BMP、動態 GIF，或依編號命名的圖片資料夾（排序和檔案總管一樣，`2.png` 在 `10.png` 前面）。
- **17 種 Windows 指標**：一般選取、連結選取、文字選取、忙碌、在背景作業等全部支援。
- **去除背景**：一鍵去除邊緣的單色背景，或用魔術棒點選要擦除的顏色，會套用到每一幀。
- **熱點**：點一下預覽圖，或直接輸入 X／Y。
- **速度**：動畫可調整為 0.25×～4×。
- **可以復原**：每次套用前都會備份，「復原上次套用」可以回到前一個狀態；「全部恢復 Windows 預設」則回到系統預設配置。
- **五種語言**：繁體中文、简体中文、English、日本語、한국어，預設跟隨系統語言。
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
| `cursors\` | 產生的 `.cur`／`.ani` 檔，登錄檔會指向這裡 |
| `backups\` | 每次套用前的指標設定（「復原」會用到） |
| `settings.json` | 語言設定 |

## 授權

[GPL-3.0](LICENSE)
