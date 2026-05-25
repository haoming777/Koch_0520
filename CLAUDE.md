# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WinForms (.NET Framework 4.7.2) industrial machine vision inspection system for a Koch packaging machine at Colgate-Palmolive Yangzhou. Controls 8 DaHua cameras synchronized with a Zmc motion controller, triggered by a Siemens S7-1500 PLC, running YOLO (ONNX) and SmartMore ViMo AI models for defect detection.

## Build & Run

- **Solution:** `VisionMeasure\VisionMeasure.sln` (Visual Studio 2022, 10 projects)
- **Build:** Open in VS2022, build with Debug|x64 or Release|x64 configuration. All projects output to `bin\` at repo root.
- **Run:** Execute `bin\VisionMeasure.exe`. Requires connected hardware (cameras, PLC, motion card) or simulate mode (`DetectionParameters.Camera.GetSimulateMode()`).
- **NuGet:** Uses `packages.config` (not PackageReference). Restore via VS or `nuget restore`. Packages stored in `VisionMeasure\packages\`.
- **No tests exist** in this project.

## Architecture

### Solution Structure (10 projects, plugin-style)

| Project | Role |
|---|---|
| **VisionMeasure** (`视觉模板.csproj`) | Main WinForms EXE — entry point, main window, hardware, detection, stations |
| **CommonLib** | Shared library — global state, interfaces, VisionPro tooling, motion control, SQLite |
| **选项卡** | Tab host shell — plugin container |
| **AIsdk** | SmartMore ViMo inference wrapper |
| **产品管理** | Product/SKU management plugin |
| **用户管理** | User management plugin |
| **系统设置** | System settings plugin |
| **相机设置** | Camera settings plugin |
| **算法调试** | Algorithm debugging plugin |
| **PLC监控** | PLC monitoring plugin |

All plugin projects implement `CommonLib.IFormPlugin` (`GetForm()`, `SetParams()`, `setMainListener()`). The main form loads them as tab pages.

### Startup Sequence (VisionMeasure/Program.cs)

1. Load `DetectionParameters` (JSON config)
2. Load SKU database from SQLite via `SkuDatabase`
3. Load all AI models via `AiModelManager` (YOLO ONNX + ViMo `.vimosln`)
4. Connect to Zmc motion controller (`MotionControlManager`)
5. Connect to Siemens PLC via Modbus TCP (`PlcCommunication`)
6. Initialize 8 DaHua cameras in parallel (`CameraManager`)
7. Show `MainFrm`

### Key Subsystems

**Hardware layer** (`VisionMeasure/Hardware/`):
- `CameraManager.cs` — 8 DaHua cameras, parallel init, start/stop
- `CameraTriggerManager.cs` — synchronized trigger via motion card
- `MotionControlManager.cs` — Zmc motion card (flying photography)
- `PlcCommunication.cs` — Siemens S7-1500 via Modbus TCP (HslCommunication)

**Stations** (`VisionMeasure/Stations/`): Each handles 2 cameras. `FrontStationProcessor`, `BackStationProcessor`, `EndFaceStationProcessor`, `SideStationProcessor`.

**Detection** (`VisionMeasure/Detection/`): `DefectDetectionService` orchestrates per-station AI inference. Station-specific: `FrontDamageInspection`, `HookDamageDetector`, `SideDefectProcessor`.

**AI** (`VisionMeasure/AI/`): `Vimo.cs` (SmartMore ViMo), `YoloOnnxSegmentation.cs` (YOLO ONNX), `ModelOutputs.cs`.

**Utils** (`VisionMeasure/Utils/`): `SkuDatabase`, `SQLiteHelper`, `ImageBufferPool`, `ImageCropper`, `ResultDrawer`, `BitmapFastConverter`, `PerformanceMonitor`.

### Global State (CommonLib/GlobalVar.cs)

Static globals for PLC connection (`ModBus`), camera SDK handles (`CameraSdk1`-`CameraSdk8`), and thresholds. Used across all projects — thread safety is not guaranteed.

### Configuration

- **`setup.ini`** (repo root) — Runtime config: camera serial numbers, AI model paths, PLC/motion IPs, I/O port mappings, production counts. Read via `IniAPI`.
- **`SystemConfig`** (CommonLib) — Singleton loaded from `setup.ini` + app config.
- **`DetectionParameters`** (VisionMeasure/Config) — JSON-based detection parameters (thresholds, ROI, etc.).
- **`ModelPathConfig`** (VisionMeasure/Config) — AI model file paths, read from `setup.ini [AI_Models]` section.

### AI Models

Two inference engines used simultaneously:
- **YOLO ONNX** (`Microsoft.ML.OnnxRuntime`) — defect detection models: box break, film break, hook damage, side defects. Uses GPU 0.
- **SmartMore ViMo** (`.vimosln` files) — OCR models: P-code recognition, date code recognition. Uses GPU 1.

Model paths configured in `setup.ini` under `[AI_Models]`, relative to `ModelRootPath`.

### Hardware Dependencies

Most of the app requires physical hardware. A simulate mode exists (`GetSimulateMode()`) but its coverage varies. Key external DLLs (not in repo) are referenced from absolute paths:
- Cognex VisionPro 59.2 (`E:\Vision_Pro\...`)
- HslCommunication, MT.Camera.SDK, XL.Tool, CLIDelegate
- USB dongle (XL.UsbDog) for licensing

### Simulate Mode

Controlled by `DetectionParameters.Camera.GetSimulateMode()`. When active, `CameraManager` returns dummy images instead of live camera feed, `PlcCommunication` skips actual PLC connection, and `MotionControlManager` simulates motion triggers. Not all code paths may handle simulate mode correctly — verify before assuming.

## Key Patterns

- `.csproj` files use Chinese names: `视觉模板.csproj` = VisionMeasure, `产品管理.csproj` = Product Management, etc.
- The solution file and all project `.csproj` files use Chinese names internally; solution GUIDs are stable.
- WinForms Designer files (`*.Designer.cs`) are auto-generated — never manually edit.
- `GlobalVar` static class is the de-facto service locator for hardware handles; new code should prefer dependency injection where practical.
- Image data flows as `OpenCvSharp.Mat` through the pipeline; `BitmapFastConverter` bridges to `System.Drawing.Bitmap` for WinForms display.
