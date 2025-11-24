

![DeskOCR Banner](banner.png)

DeskOCR — possibly the fastest, lowest‑memory offline OCR application on Windows.

---

This is a .NET 8 OCR (Optical Character Recognition) application built with WPF. It runs entirely offline on CPU, avoiding privacy risks from network calls and compatibility issues related to GPU drivers. It supports configurable hotkeys, a result window, a selection window, and flexible “extensions” to perform external actions (such as search or translate) on recognized text.

[中文版本](README.md)

## System Requirements

- .NET 8.0 or later
- Windows 10/11

## NuGet Dependencies

- Microsoft.ML.OnnxRuntime (1.19.2) — ONNX Runtime
- Newtonsoft.Json (13.0.1) — JSON serialization
- OpenCvSharp4 (4.11.0.20250507) — OpenCV image processing core library
- Sdcb.OpenCvSharp4.mini.runtime.win-x64 (4.11.0.35) — lightweight OpenCV runtime for Windows x64
- System.Drawing.Common (6.0.0) — image processing
- Microsoft.Extensions.Configuration (8.0.0) — configuration management
- Microsoft.Extensions.Configuration.Json (8.0.0) — JSON configuration support

## Build and Run

### Installation
1. Download the latest `DeskOCR.zip` from the [Releases page](https://github.com/yourusername/DeskOCR/releases)
2. Extract the `DeskOCR.zip` to any directory
3. Run `DeskOCR.exe`

### Run in Development

1. Clone or download the project locally
2. In the project root, run:

```bash
dotnet restore
dotnet build OR dotnet build -c Release
```

### Publish Executables

The project supports publishing self‑contained executables for multiple architectures:

#### Windows x64
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

#### Windows x86
```bash
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true
```

#### Windows ARM64
```bash
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```
- Since I don’t have an ARM64 Windows device, I cannot test the ARM64 build. Please ensure all dependencies support ARM64 and test on your side.

**Publish Parameters**
- `-c Release`: use the Release configuration
- `--self-contained true`: create a self‑contained deployment that includes the .NET runtime
- `-p:PublishSingleFile=true`: publish as a single executable

**Notes**
- Self‑contained deployments increase file size but do not require .NET runtime installation on the target machine
- If you publish a single‑file build, the first launch may be slightly slower

## Features

- 📸 Screenshot OCR
- ⌨️ Global and window hotkeys (configurable)
- 🔧 System tray integration
- 🧩 Extensions: two configurable actions (open external URLs for search/translate, etc.)
- 📋 Result window and selection window views
- 🎯 High‑accuracy text recognition
- 🚀 Lightweight runtime with performance optimizations

## Performance

- On supported CPUs, with optimized ONNX Runtime configuration, inference approaches “instant”
- Low‑memory design:
  - Mat/object pools significantly reduce allocations and GC pressure (`Core/OCRManager.cs:16`, `Core/OCRManager.cs:119`, `Core/OCRManager.cs:162`)
  - Lightweight OpenCvSharp mini runtime, loading core modules only
- Fully offline: no network calls, no heavy background services

Actual performance depends on hardware and model size. Architecturally, this project aims to be “among the fastest and lowest memory footprint” for offline OCR on Windows.

## Configuration

Configuration is stored in `appsettings.json`. Example:

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
  "Extension1Name": "Search",
  "Extension1Hotkey": "Alt+E",
  "Extension1PreferredIndex": 3,
  "Extension1Enabled": true,
  "Extension2Name": "Translate",
  "Extension2Hotkey": "Alt+R",
  "Extension2PreferredIndex": 0,
  "Extension2Enabled": false
}
```

- `Hotkey`: global OCR hotkey
- `ResultWindowFontSize`: result window font size
- `OCRMode`: `Classic`, `Silent`, or `Selection`
- `CopyOriginalKey`, `CloseKey`: hotkeys for the result/selection windows
- `SelectionClearKey`, `SelectionSelectAllKey`: hotkeys for the selection window
- `TranslationUrls`: URL templates used by extensions; if a template contains `你好`, it will be replaced with the query; otherwise `?q=` or `&q=` is appended as needed
- `Extension1*` / `Extension2*`: extension name, hotkey, preferred URL index, and enabled flag

After saving settings, open windows apply changes immediately (both the result window and the selection window do not require a restart).

## Usage Guide

- Configure hotkeys and extensions on the settings page
- Use the global hotkey for screenshot recognition
- In the result window, use buttons or hotkeys to copy/close
- Use extension buttons to open external actions (search/translate) based on `TranslationUrls`; you can modify `appsettings.json` to add URL templates
- Switch OCR modes: `Classic`, `Silent`, or `Selection`

## Notes

- You may see some warnings during build, but they do not affect normal application operation
- Ensure all required model files are correctly placed in the project root directory
- The project uses a lightweight OpenCV runtime, including only core functional modules to reduce size

### Install Required Runtime Libraries
Install the following runtime libraries on the target machine:

**Microsoft Visual C++ Redistributable**
- Download and install the latest Microsoft Visual C++ Redistributable
- Links: https://aka.ms/vs/17/release/vc_redist.x64.exe (64‑bit)
- Links: https://aka.ms/vs/17/release/vc_redist.x86.exe (32‑bit)

## Install .NET 8.0

- Download and install the .NET 8.0 SDK
- Link: https://dotnet.microsoft.com/download/dotnet/8.0

## Acknowledgements

This project uses the following excellent open‑source projects — sincere thanks:

### 🙏 Special Thanks

- **[PaddlePaddle/PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)** — provides powerful OCR models and algorithms. PaddleOCR is a deep‑learning‑based OCR toolkit supporting multi‑language detection and recognition, forming the technical foundation of this project’s core functionality.

- **[sdcb/opencvsharp-mini-runtime](https://github.com/sdcb/opencvsharp-mini-runtime)** — offers a lightweight OpenCV runtime that significantly reduces application size while maintaining excellent performance. With automated CI/CD pipelines, it builds and tests reliable OpenCV bindings across platforms.

### 🔧 Tech Stack

- **.NET 8** — modern cross‑platform development framework
- **WPF** — Windows desktop UI
- **OpenCvSharp4** — .NET wrapper for OpenCV
- **ONNX Runtime** — high‑performance ML inference engine

## License

This project follows the corresponding open‑source licenses. When using this project, please ensure compliance with all dependency licenses.

## Contributing

Issues and Pull Requests are welcome to improve this project!

---

*If this project helps you, please consider giving it a ⭐ Star!*