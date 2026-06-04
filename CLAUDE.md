# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WinForms (.NET Framework 4.7.2) industrial machine vision inspection system for a Koch packaging machine at Colgate-Palmolive Yangzhou. Controls 8 DaHua cameras synchronized with a Zmc motion controller, triggered by a Siemens S7-1500 PLC, running YOLO (ONNX) and SmartMore ViMo AI models for defect detection.

## Build & Run

- **Solution:** VisionMeasure/VisionMeasure.sln (Visual Studio 2022, 10 projects)
- **Build:** Open in VS2022, build with Debug|x64 or Release|x64 configuration
- **Run:** Execute bin/VisionMeasure.exe. Requires connected hardware or simulate mode.
- **NuGet:** Uses packages.config. Restore via VS or nuget restore.
- **No tests exist** in this project.

## Architecture

### Solution Structure (10 projects, plugin-style)

| Project | Role |
|---|---|
| VisionMeasure (视觉模板.csproj) | Main WinForms EXE |
| CommonLib | Shared library (global state, interfaces, SQLite) |
| 选项卡 | Tab host shell (plugin container) |
| AIsdk | SmartMore ViMo inference wrapper |
| 产品管理 | Product/SKU management plugin |
| 用户管理 | User management plugin |
| 系统设置 | System settings plugin |
| 相机设置 | Camera settings plugin |
| 算法调试 | Algorithm debugging plugin |
| PLC监控 | PLC monitoring plugin |

### Startup Sequence

1. Load DetectionParameters (JSON config)
2. Load SKU database (SQLite via SkuDatabase)
3. Load AI models (YOLO ONNX + ViMo .vimosln)
4. Connect to Zmc motion controller
5. Connect to Siemens PLC (Modbus TCP)
6. Initialize 8 DaHua cameras
7. Show MainFrm

### Key Subsystems

**Hardware:** CameraManager.cs, CameraTriggerManager.cs, MotionControlManager.cs, PlcCommunication.cs

**Stations:** FrontStationProcessor, BackStationProcessor, EndFaceStationProcessor, SideStationProcessor (each handles 2 cameras)

**Detection:** DefectDetectionService, FrontDamageInspection, HookDamageDetector, SideDefectProcessor

**AI:** Vimo.cs (ViMo), YoloOnnxSegmentation.cs (YOLO ONNX), ModelOutputs.cs

**Utils:** SkuDatabase, SQLiteHelper, ImageBufferPool, ImageCropper, ResultDrawer, BitmapFastConverter, PerformanceMonitor

### Configuration

- **setup.ini** (repo root) — Runtime config: camera SNs, AI model paths, PLC/motion IPs, I/O ports, production counts
- **SystemConfig** (CommonLib) — Singleton from setup.ini + app config
- **DetectionParameters** (VisionMeasure/Config) — JSON detection params (thresholds, ROI, etc.)
- **ModelPathConfig** (VisionMeasure/Config) — AI model file paths from setup.ini [AI_Models]

### Camera Trigger I/O Mapping

| Camera | Station | Input Port | Output Port |
|---|---|---|---|
| 1 (正面左) | Front | IN4 | OUT9 |
| 2 (正面右) | Front | IN4 | OUT8 |
| 3 (上端面) | EndFace | IN10 | OUT10 |
| 4 (下端面) | EndFace | IN10 | OUT11 |
| 5 (背面左) | Back | IN4 | OUT12 |
| 6 (背面右) | Back | IN4 | OUT13 |
| 7 (左侧面) | Side | IN13 | OUT14 |
| 8 (右侧面) | Side | IN13 | OUT15 |

Side station uses IN12 for motion axis sensor with configurable edge mode.

### AI Models

Two inference engines:
- **YOLO ONNX** (Microsoft.ML.OnnxRuntime, GPU 0) — box break, film break, hook damage, side defects
- **ViMo** (.vimosln files, GPU 1) — P-code OCR, date code OCR

Model paths in setup.ini [AI_Models], relative to ModelRootPath.

### Hardware Dependencies

Requires physical hardware for full operation. External DLLs:
- Cognex VisionPro 59.2, HslCommunication, MT.Camera.SDK, XL.Tool, CLIDelegate
- USB dongle (XL.UsbDog) for licensing

### Simulate Mode

Controlled by DetectionParameters.Camera.GetSimulateMode(). When active, CameraManager returns dummy images, PlcCommunication skips actual connection, MotionControlManager simulates triggers.

## Key Patterns

- .csproj files use Chinese names
- WinForms Designer files (.Designer.cs) are auto-generated - never edit
- Image data flows as OpenCvSharp.Mat; BitmapFastConverter bridges to System.Drawing.Bitmap
- GlobalVar static class is the de-facto service locator
