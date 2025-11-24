
![DeskOCR Banner](banner.png)

DeskOCR —— 可能是最快、占用内存最低的Windows 平台离线 OCR程序。

---

这是一个基于 .NET 8 的 OCR（光学字符识别）应用程序，使用 WPF 构建。完全基于cpu离线运行，不会因为联网运行造成你的隐私数据泄露，也不会因为GPU驱动问题导致兼容问题。支持可配置热键、结果窗口、选择窗口，以及灵活的“扩展”机制，可对识别文本执行外部动作（如搜索或翻译）。

[English Version](README_EN.md)

## 系统要求

- .NET 8.0 或更高版本
- Windows10/11 操作系统

## NuGet 依赖包

- Microsoft.ML.OnnxRuntime (1.19.2) - ONNX 运行时
- Newtonsoft.Json (13.0.1) - JSON 序列化
- OpenCvSharp4 (4.11.0.20250507) - OpenCV 图像处理核心库
- Sdcb.OpenCvSharp4.mini.runtime.win-x64 (4.11.0.35) - OpenCV Windows x64 轻量级运行时
- System.Drawing.Common (6.0.0) - 图像处理
- Microsoft.Extensions.Configuration (8.0.0) - 配置管理
- Microsoft.Extensions.Configuration.Json (8.0.0) - JSON 配置支持

## 构建和运行

### 安装使用
1. 从 [发布页面](https://github.com/yourusername/DeskOCR/releases) 下载最新版本的 `DeskOCR.zip` 文件
2. 解压 `DeskOCR.zip` 文件到任意目录
3. 运行 `DeskOCR.exe` 文件

### 开发环境运行

1. 克隆或下载项目到本地
2. 在项目根目录下运行：
   ```bash
   dotnet restore
   dotnet build 或 dotnet build -c Release
   ```

### 发布可执行文件

项目支持发布为多种架构的独立可执行文件：

#### Windows x64 架构
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

#### Windows x86 架构
```bash
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true
```

#### Windows ARM64 架构
```bash
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```
- 由于我没有 ARM64 windows设备，无法测试 ARM64 版本，ARM64 版本需要确保所有依赖项都支持 ARM64 架构，请自行测试

**发布参数说明：**
- `-c Release`：使用 Release 配置
- `--self-contained true`：创建自包含部署，包含 .NET 运行时
- `-p:PublishSingleFile=true`：将应用程序发布为单个可执行文件

**注意事项：**
- 自包含部署会增加文件大小，但无需在目标机器上安装 .NET 运行时
- 如果你构建了单文件发布版本，首次启动可能稍慢


## 功能特性

- 📸 屏幕截图 OCR 识别
- ⌨️ 全局与窗口热键（可配置）
- 🔧 系统托盘集成
- 🧩 扩展：两个可配置动作（打开外部 URL 进行搜索/翻译等）
- 📋 结果窗口与选择窗口视图
- 🎯 高精度文本识别
- 🚀 轻量级运行时，性能优化

## 性能

- 在支持的 CPU 上，结合优化的 ONNX Runtime 配置，推理速度接近“即刻”
- 低内存占用的设计：
  - Mat/对象池显著减少分配与 GC 压力（`Core/OCRManager.cs:16`、`Core/OCRManager.cs:119`、`Core/OCRManager.cs:162`）
  - 轻量级 OpenCvSharp mini 运行时，仅加载核心模块
- 完全离线：无网络调用、无沉重的后台服务

实际性能与硬件与模型大小相关；本项目从架构上力求在 Windows 平台离线 OCR 场景达到“几乎最快、占用内存最低”。

## 配置

配置存储在 `appsettings.json`。示例：

```json
{
  "Hotkey": "Alt+W",
  "ResultWindowFontSize": "12",
  "CopyOriginalKey": "Alt+C",
  "CloseKey": "Alt+D",
  "SelectionClearKey": "Alt+X",
  "SelectionSelectAllKey": "Alt+A",
  "OCRMode": "Classic",
  "TranslationUrls": [
    "https://fanyi.so.com/?q=你好",
    "https://translate.google.com/?q=你好",
    "https://fanyi.baidu.com/mtpe-individual/transText?query=你好",
    "https://www.google.com/search?q=你好"
  ],
  "Extension1Name": "搜索",
  "Extension1Hotkey": "Alt+E",
  "Extension1PreferredIndex": 3,
  "Extension1Enabled": true,
  "Extension2Name": "翻译",
  "Extension2Hotkey": "Alt+R",
  "Extension2PreferredIndex": 0,
  "Extension2Enabled": false
}
```

- `Hotkey`：全局 OCR 快捷键
- `ResultWindowFontSize`：结果窗口字体大小
- `OCRMode`：`Classic`、`Silent` 或 `Selection`
- `CopyOriginalKey`、`CloseKey`：结果/选择窗口快捷键
- `SelectionClearKey`、`SelectionSelectAllKey`：选择窗口快捷键
- `TranslationUrls`：扩展使用的 URL 模板；若包含 `你好`，会替换为查询文本；否则按需附加 `?q=` 或 `&q=`
- `Extension1*` / `Extension2*`：扩展名称、快捷键、首选 URL 索引、启用状态

保存设置后，已打开的窗口会立即应用（结果窗口与选择窗口无需重启）。

## 使用指南

- 在设置界面配置热键与扩展
- 使用全局快捷键进行截图识别
- 在结果窗口中使用按钮或快捷键进行复制/关闭
- 使用扩展按钮基于 `TranslationUrls` 打开外部动作（搜索/翻译），可以修改appsettings.json自行配置添加 URL 模板
- 可以切换 OCR 模式：`经典模式`、`静默模式` 或 `选择模式`

## 注意事项

- 构建过程中可能会出现一些警告，但不影响应用程序的正常运行
- 确保所有必需的模型文件都已正确放置在项目根目录中
- 项目使用轻量级的 OpenCV 运行时，仅包含核心功能模块以减小体积





### 安装必要的运行时库
在目标机器上安装以下运行时库：

**Microsoft Visual C++ Redistributable**
- 下载并安装最新版本的 Microsoft Visual C++ Redistributable
- 链接：https://aka.ms/vs/17/release/vc_redist.x64.exe (64位)
- 链接：https://aka.ms/vs/17/release/vc_redist.x86.exe (32位)


## 安装dotnet 8.0

- 下载并安装 .NET 8.0 SDK
- 链接：https://dotnet.microsoft.com/download/dotnet/8.0


## 致谢

本项目使用了以下优秀的开源项目，在此表示诚挚的感谢：

### 🙏 特别感谢

- **[PaddlePaddle/PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)** - 提供了强大的 OCR 模型和算法支持。PaddleOCR 是一个基于深度学习的 OCR 工具库，支持多种语言的文本检测和识别，为本项目的核心功能提供了技术基础。

- **[sdcb/opencvsharp-mini-runtime](https://github.com/sdcb/opencvsharp-mini-runtime)** - 提供了轻量级的 OpenCV 运行时包，显著减小了应用程序体积，同时保持了出色的性能。该项目通过自动化 CI/CD 管道构建和测试，为多平台提供了可靠的 OpenCV 绑定。

### 🔧 技术栈

- **.NET 8** - 现代化的跨平台开发框架
- **WPF** - Windows 桌面应用程序用户界面
- **OpenCvSharp4** - .NET 的 OpenCV 包装器
- **ONNX Runtime** - 高性能机器学习推理引擎

## 许可证

本项目遵循相应的开源许可证。使用本项目时，请确保遵守所有依赖项目的许可证要求。

## 贡献

欢迎提交 Issue 和 Pull Request 来改进这个项目！

---

*如果这个项目对您有帮助，请考虑给它一个 ⭐ Star！*

