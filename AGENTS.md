# Repository Guidelines

Windows Forms (.NET Framework 4.7.2) machine-vision app for the Colgate Yangzhou Koch packaging line: eight DaHua cameras on a Zmc motion controller, triggered by a Siemens S7-1500 PLC, with YOLO (ONNX) and SmartMore ViMo inference; `CLAUDE.md` has deeper architecture notes.

## Project Structure & Module Organization

- `VisionMeasure/VisionMeasure.sln` — main solution (9 projects); the EXE project is `VisionMeasure/视觉模板.csproj`, built to the repo-root `bin/`.
- `VisionMeasure/` — app code: `Hardware/`, `Stations/` (Front/Back/EndFace/Side), `Detection/`, `AI/`, `Config/`, `Models/`, `Services/`, `Utils/`; UI forms in `From/`.
- Plugin DLLs (output to `bin/`): `产品管理`→`SetProduct`, `用户管理`→`SetUser`, `系统设置`→`SetSystem`, `相机设置`→`SetCamera`, `算法调试`→`SetVision`, `PLC监控`; shared code in `CommonLib/`, `AIsdk/`. `选项卡/` is a standalone tab host outside the solution.
- Runtime assets: `bin/Config/*.json` (DetectionParams, StationDefectConfig, barcode.config), `setup.ini` (read from the working directory), `Libs/` (native DLLs), `docs/superpowers/specs/` (design specs), `ov1/`, `ov2/` (sample slot images).

## Build, Test, and Development Commands

- Build in Visual Studio 2022+ (`Debug|x64` or `Release|x64`), or from a developer prompt: `msbuild VisionMeasure/VisionMeasure.sln /p:Configuration=Release /p:Platform=x64`.
- Restore NuGet first (`packages.config`; `packages/` is untracked): Visual Studio restores on build, or run `nuget restore`.
- Run `bin/VisionMeasure.exe` (needs the USB dongle, ZMC, PLC, cameras). For hardware-free runs set `模拟模式=true` in `bin/Config/DetectionParams.json`. Logs: `bin/Logs/`.

## Coding Style & Naming Conventions

C# 7.3, tab indentation, UTF-8 (BOM) files; comments and user-visible strings stay Chinese. PascalCase types/members, `_camelCase` private fields, `Frm`/`Form` suffix for windows, existing namespaces (`VisionMeasure.Utils`, `CommonLib`, `PLC调试.Class`). Wrap hardware/imaging calls in try/catch and log via `Logger`; keep image work off the UI thread. `.csproj` files are legacy (non-SDK): register new files in `<Compile Include>`, easiest through Visual Studio.

## Testing Guidelines

There are no automated tests; verification is manual and hardware-in-the-loop. Exercise the `算法调试` / `PLC监控` plugins and the test forms in `VisionMeasure/From/` (`BarcodeTestForm`, `DateCodeTestForm`, `DetectionParametersForm`), then state what you checked when committing.

## Commit & Pull Request Guidelines

Commits go directly to `master` in Chinese with an imperative summary, often with a `YYMMDD` suffix (`添加侧面溢出逻辑, 取消侧面活体检测260920`); risky edits start with a `修改前备份YYMMDD` backup commit (`修改前备份260901`). Describe scope, station/PLC impact, and config edits (`setup.ini`, `bin/Config/*.json`). Never commit `*.vimosln`, model weights, or machine-specific paths. In a PR, summarize the change, list verification steps and hardware, and flag field-deployment or calibration impact.

## Notes for Automated Edits

Never hand-edit `*.Designer.cs`, `*.resx`, or `Properties/*.Designer.cs` files; they are generated. Preserve file encoding and Chinese strings when rewriting a file, and keep edits targeted; avoid reformatting untouched code.
