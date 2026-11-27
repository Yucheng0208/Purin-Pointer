<p align="center">
  <img src="docs/images/logo.png" alt="Purin Pointer" width="240">
</p>

<h1 align="center">Purin Pointer</h1>

<p align="center">
  <b>Windows 动画光标工作室</b><br>
  把图片、GIF 和光标文件变成你专属的 Windows 鼠标指针。
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.tw.md">繁體中文</a> · <b>简体中文</b> · <a href="README.ja.md">日本語</a> · <a href="README.ko.md">한국어</a>
</p>

---

Purin Pointer 是 AniOnMac 的 Windows 版。导入 `.ani`、`.cur`、PNG、GIF 或整个图片文件夹，一键去背景、放好定位点，就能应用为 Windows 的鼠标指针。不需要绘图技巧，也不需要管理员权限。

![Purin Pointer 主窗口](docs/images/tw/main.png)

## 功能

- **主题**：想做几套就做几套，在侧栏新建、复制、删除。正在应用的主题会标示“使用中”，所有更改自动保存。
- **4 种模式＋13 种指针**：普通光标、链接选择、文本选择、等待状态，其余 13 种 Windows 指针在“更多指针”里，包括“后台运行”（没有另外设置时沿用等待状态）。只应用已填入的模式。
- **支持多种素材**：`.ani`、`.cur`、`.ico`、PNG、JPG、BMP、动态 GIF，或按编号命名的图片文件夹。
- **时间轴**：调整帧顺序、删除帧、设置每帧时间并实时预览。
- **去背景**：纯色背景用“自动去背景”，指定颜色用“魔棒”，一次处理每一帧。
- **定位点**：在放大的预览上拖动，或直接输入 X／Y。
- **定位点校准**：用你自己的指针依次点 5 个圆点，程序会测出尖端偏了多少并自动修正定位点，点击应用即可生效。
- **绘制风格**：每个模式可分别选择“高分辨率”（边缘平滑）、“像素感”（看得到一格一格的方块）或“锯齿边”（像旧式指针的硬边），预览会显示 Windows 实际绘制的样子。
- **指针大小**：与 Windows“设置”相同的全局大小（32–256 px），两边同步。
- **随时撤销**：“撤销上次更改”和“恢复 Windows 原生外观”一键完成。
- **浅色与深色**：布丁配色，支持繁體中文、简体中文、English、日本語、한국어。

## 下载

1. 到 [Releases](https://github.com/Yucheng0208/Purin-Pointer/releases/latest) 下载 ZIP：
   - `PurinPointer-win-x64.zip`：大多数 Windows 电脑（Intel／AMD）
   - `PurinPointer-win-arm64.zip`：ARM 笔记本（例如 Snapdragon）
2. 解压后运行 `PurinPointer.exe`。免安装，也不需要另外安装 .NET。
3. 如果出现“**Windows 已保护你的电脑**”，点击“**更多信息 → 仍要运行**”。

> [!IMPORTANT]
> 发布版本**没有代码签名**。Windows 11 开启“**智能应用控制**”时，未签名的程序会被直接阻止。遇到这种情况，请[从源代码构建](docs/engineering.tw.md#build-and-run)。

系统要求：Windows 10 或 Windows 11。

## 快速上手

1. 选择一个模式标签页，例如“**普通光标**”。
2. 把图片、GIF 或 `.ani` 文件拖到“**01 导入光标**”。
3. 如果有背景，点击“**04 去背景**”里的“**自动去背景**”。
4. 在预览上把定位点的十字拖到指针的尖端。
5. 点击“**应用主题**”（或按 **Ctrl + Enter**）。

完整说明请看[操作手册](docs/user-guide.tw.md)（繁体中文）。

## 文档

| 文档 | 内容 |
| --- | --- |
| [操作手册](docs/user-guide.tw.md)（繁体中文） | 每个界面与功能的分步说明，以及疑难解答 |
| [工程说明](docs/engineering.tw.md)（繁体中文） | 架构、构建、测试、与 Windows 的集成方式，以及发布流程 |

## 从源代码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet run --project src/PurinPointer
```

```powershell
dotnet run --project tests/PurinPointer.Tests
```

发布方式与项目结构请看[工程说明](docs/engineering.tw.md)（繁体中文）。

## 许可证

[GPL-3.0](LICENSE)
