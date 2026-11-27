<p align="center">
  <img src="docs/images/logo.png" alt="Purin Pointer" width="240">
</p>

<h1 align="center">Purin Pointer</h1>

<p align="center">
  <b>Windows 動畫游標工作室</b><br>
  把圖片、GIF 和游標檔變成你專屬的 Windows 滑鼠游標。
</p>

<p align="center">
  <a href="README.md">English</a> · <b>繁體中文</b> · <a href="README.zh-Hans.md">简体中文</a> · <a href="README.ja.md">日本語</a> · <a href="README.ko.md">한국어</a>
</p>

---

Purin Pointer 是 AniOnMac 的 Windows 版。匯入 `.ani`、`.cur`、PNG、GIF 或整個圖片資料夾，一鍵去背、放好定位點，就能套用成 Windows 的滑鼠游標。不需要繪圖技巧，也不需要系統管理員權限。

![Purin Pointer 主視窗](docs/images/tw/main.png)

## 功能

- **主題**：想做幾套就做幾套，在側欄新增、複製、刪除。套用中的主題會標示「使用中」，所有變更自動儲存。
- **4 種模式＋13 種指標**：普通游標、連結指向、文字選取、等待狀態，其餘 13 種 Windows 指標在「更多指標」裡，包括「在背景作業」（沒有另外設定時沿用等待狀態）。只套用已填入的模式。
- **支援多種素材**：`.ani`、`.cur`、`.ico`、PNG、JPG、BMP、動態 GIF，或依編號命名的圖片資料夾。
- **時間軸**：調整畫格順序、刪除畫格、設定每格時間並即時預覽。
- **去背**：單色背景用「自動去背」，指定顏色用「魔術棒」，一次處理每一格。
- **定位點**：在放大的預覽上拖曳，或直接輸入 X／Y。
- **定位點校正**：用你自己的指標依序點 5 個點點，程式會量出尖端偏了多少並自動修正定位點，按套用即可生效。
- **繪製風格**：每個模式可各自選「高解析」（邊緣平滑）、「像素感」（看得到一格一格的方塊）或「鋸齒邊」（像舊式游標的硬邊），預覽會顯示 Windows 實際畫出來的樣子。
- **指標大小**：與 Windows「設定」相同的全域大小（32–256 px），兩邊同步。
- **隨時復原**：「復原上次變更」和「恢復 Windows 原生外觀」一鍵完成。
- **淺色與深色**：布丁配色，支援繁體中文、简体中文、English、日本語、한국어。

## 下載

1. 到 [Releases](https://github.com/Yucheng0208/Purin-Pointer/releases/latest) 下載 ZIP：
   - `PurinPointer-win-x64.zip`：大多數 Windows 電腦（Intel／AMD）
   - `PurinPointer-win-arm64.zip`：ARM 筆電（例如 Snapdragon）
2. 解壓縮後執行 `PurinPointer.exe`。免安裝，也不需要另外安裝 .NET。
3. 如果出現「**Windows 已保護您的電腦**」，按「**其他資訊 → 仍要執行**」。

> [!IMPORTANT]
> 發佈版本**沒有程式碼簽章**。Windows 11 開啟「**智慧型應用程式控制**」時，未簽章的程式會被直接封鎖。遇到這種情況，請[從原始碼建置](docs/engineering.tw.md#build-and-run)。

系統需求：Windows 10 或 Windows 11。

## 快速上手

1. 選一個模式分頁，例如「**普通游標**」。
2. 把圖片、GIF 或 `.ani` 檔拖曳到「**01 匯入游標**」。
3. 如果有背景，按「**04 去背**」裡的「**自動去背**」。
4. 在預覽上把定位點的十字拖到游標的尖端。
5. 按「**套用主題**」（或按 **Ctrl + Enter**）。

完整說明請看[操作手冊](docs/user-guide.tw.md)。

## 文件

| 文件 | 內容 |
| --- | --- |
| [操作手冊](docs/user-guide.tw.md) | 每個畫面與功能的逐步說明，以及疑難排解 |
| [工程說明](docs/engineering.tw.md) | 架構、建置、測試、與 Windows 的整合方式，以及發佈流程 |

## 從原始碼建置

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet run --project src/PurinPointer
```

```powershell
dotnet run --project tests/PurinPointer.Tests
```

發佈方式與專案結構請看[工程說明](docs/engineering.tw.md)。

## 授權

[GPL-3.0](LICENSE)
