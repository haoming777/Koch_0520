# PLC 缺陷通讯重构设计文档

**日期：** 2026-07-26  
**项目：** 扬州高露洁 Koch 机视觉检测系统  
**目标：** 将缺陷项→PLC 信号映射开放为 JSON 配置文件，实现纯 S7-1500 DB47 通讯，修复已发现 Bug

---

## 1. 需求概要

### 1.1 工位与缺陷项

| 工位 | 缺陷项 | 是否剔除 | 停机标识 |
|------|--------|----------|----------|
| 正面（Camera 1+2） | P号码异常 | 剔除 | 3 |
| 正面 | 盒子破 | 剔除 | 2 |
| 端面（Camera 3+4） | 搭舌缺陷（折舌异常） | 剔除 | 2 |
| 端面 | 破损 | 剔除 | 2 |
| 端面 | 边缘问题（边缘折不好） | 不剔除 | 1 |
| 侧面（Camera 7+8） | 破和褶皱 | 剔除 | 2 |
| 背面（Camera 5+6） | 日期码 NG剔除（格式错+重影） | 剔除 | 2 |
| 背面 | 日期码 NG不剔除（日期值不匹配） | 不剔除 | 1 |
| 背面 | 条形码 NG剔除（读不到） | 剔除 | 2 |
| 背面 | 条形码 NG不剔除（码值错） | 不剔除 | 1 |
| 背面 | 挂钩明显错位 | 剔除 | 2 |
| 背面 | 挂钩轻微错位 | 剔除 | 2 |
| 背面 | 盒子破损 | 剔除 | 2 |
| 整体 | 缺料 | 剔除 | 1 |

### 1.2 日期码三种状态（参照柴大师 AIRunThread.cs）

柴大师判定链路：`iResult = c1Result + c2Result + c3Result`

| 阶段 | 判断 | 失败后果 |
|------|------|----------|
| C1 分割 | Num==1→OK，否则→NG剔除 | iNGCount++ |
| C2 重影分类 | 重影→NG剔除，跳过 C3 OCR | iNGCount++ |
| C3 OCR 校验 | return 1 格式错→NG剔除，return 2 日期值不对→NG不剔除，return 0→OK | — |

最终路由：

| 状态 | 触发条件 | 信号 | 对应 Koch 代码标签 |
|------|---------|------|-------------------|
| OK | c1+c2+c3==0 | outOk | `"日期:xxx"` / `"双排:xxx"`（仅显示，不算缺陷） |
| NG 剔除 | iNGCount>0（格式错/重影/数量错） | outNg, isRemove=1 | `"日期码错误(xxx)"`、`"日期码重影"` → 统一 `IsReject=true, StopLevel=2` |
| NG 不剔除 | c3>0 且 iNGNoCount>0 且 iNGCount==0 | outNgNo, isRemove=0 | `"日期码不完全正确(xxx)"` → `IsReject=false, StopLevel=1` |

> **注意：** `"日期码重影"` 不单独配置，C2 重影在代码层面直接走 NG剔除，与 `"日期码错误"` 合并为同一条配置规则。

### 1.3 条形码三种状态（参照日期码三态模式）

柴大师示例程序无条形码逻辑，按同一模式映射：

| 状态 | 触发条件 | 对应 Koch 代码标签 | 配置 |
|------|---------|-------------------|------|
| OK | 条码匹配成功 | `"条码:xxx"`（仅显示，不算缺陷） | — |
| NG 剔除 | 完全读不到码（类比格式错） | `"条码缺少"` | `IsReject=true, StopLevel=2` |
| NG 不剔除 | 读到但值不对（类比日期值不对） | `"条码错:xxx"` | `IsReject=false, StopLevel=1` |

### 1.4 PLC 通讯需求

- 纯 S7-1500 协议（移除 Modbus 路径）
- DB47 地址空间（见 3.3 节）
- 每工位每 P 剔除信号按位打包到 Word
- 停机标识取最高优先级（3>2>1>0）
- 拍照完成后写 true，PLC 端清除
- 八个相机就绪后给 CameraReady
- 200ms 交替 1/0 心跳

---

## 2. 配置文件设计

### 2.1 新建 `VisionMeasure/Config/StationDefectConfig.json`

```json
{
  "Stations": {
    "Front": {
      "StationKey": "Front",
      "Defects": [
        { "Name": "P号错误",  "IsReject": true,  "StopLevel": 3 },
        { "Name": "P号缺少",  "IsReject": true,  "StopLevel": 3 },
        { "Name": "盒子破损",  "IsReject": true,  "StopLevel": 2 }
      ]
    },
    "EndFace": {
      "StationKey": "EndFace",
      "Defects": [
        { "Name": "搭舌缺陷",  "IsReject": true,  "StopLevel": 2 },
        { "Name": "破损",      "IsReject": true,  "StopLevel": 2 },
        { "Name": "边缘问题",  "IsReject": false, "StopLevel": 1 },
        { "Name": "缺少",      "IsReject": true,  "StopLevel": 2 }
      ]
    },
    "Side": {
      "StationKey": "Side",
      "Defects": [
        { "Name": "缺陷",  "IsReject": true, "StopLevel": 2 }
      ]
    },
    "Back": {
      "StationKey": "Back",
      "Defects": [
        { "Name": "日期码不完全正确",    "IsReject": false, "StopLevel": 1 },
        { "Name": "日期码",            "IsReject": true,  "StopLevel": 2 },
        { "Name": "条码缺少",          "IsReject": true,  "StopLevel": 2 },
        { "Name": "条码错",            "IsReject": false, "StopLevel": 1 },
        { "Name": "挂钩明显错位",       "IsReject": true,  "StopLevel": 2 },
        { "Name": "轻微挂钩错位",       "IsReject": true,  "StopLevel": 2 },
        { "Name": "盒子破损",          "IsReject": true,  "StopLevel": 2 }
      ]
    },
    "Global": {
      "StationKey": "Global",
      "Defects": [
        { "Name": "缺料", "IsReject": true, "StopLevel": 1 }
      ]
    }
  },
  "DefaultDefect": {
    "IsReject": false,
    "StopLevel": 0
  }
}
```

### 2.2 匹配规则

1. **按序匹配**：遍历配置项列表，先命中先生效。**具体项放前面，通用项放后面**（如 `"日期码不完全正确"` 在 `"日期码"` 之前，防止被通用项误匹配）
2. **精确优先**：先尝试 `==` 精确匹配，失败后降级为 `Contains` 子串匹配
3. **多缺陷**：状态值以逗号分隔时，逐个匹配取并集
4. **DefaultDefect**：以上都匹配不到时走默认值（不剔除、不停机，安全侧）

### 2.3 代码中的真实缺陷名对照

| 工位 | 配置 Name（稳定前缀） | 代码中实际字符串 |
|------|----------------------|-----------------|
| Front | `P号错误` | `P号错误(识:{pNum}/标:{refPNumber})` |
| Front | `P号缺少` | `P号缺少` |
| Front | `盒子破损` | `盒子破损` |
| EndFace | `搭舌缺陷` | `搭舌缺陷` |
| EndFace | `边缘问题` | `边缘问题` |
| EndFace | `破损` | `破损` |
| EndFace | `缺少` | `缺少` |
| Side | `缺陷` | `缺陷0`, `缺陷1`, ... |
| Back | `日期码不完全正确` | `日期码不完全正确({text})` → NG不剔除 |
| Back | `日期码` | `日期码错误({text})`、`日期码重影` → NG剔除 |
| Back | `条码缺少` | `条码缺少` → NG剔除 |
| Back | `条码错` | `条码错:{text}` → NG不剔除 |
| Back | `挂钩明显错位` | `挂钩明显错位` |
| Back | `轻微挂钩错位` | `轻微挂钩错位 {thick:F1}px` |
| Back | `盒子破损` | `盒子破损` |
| Global | `缺料` | `缺料`（待代码实现） |

---

## 3. PLC 通讯设计

### 3.1 协议选择

**只用 S7-1500**，基于 `HslCommunication.Profinet.Siemens.SiemensS7Net`。移除 `PlcResultService` 中的 Modbus 分支。

### 3.2 S7-1500 DB 地址映射

| 信号名 | DB 地址 | 数据类型 | 说明 |
|--------|---------|----------|------|
| 1#CameraFeedbackData | DB47.DBW0 | Word | 正面工位剔除位（bit0~bit15 对应第1~16盒） |
| 2#CameraFeedbackData | DB47.DBW2 | Word | 背面工位剔除位 |
| 3#CameraFeedbackData | DB47.DBW4 | Word | 端面工位剔除位 |
| 4#CameraFeedbackData | DB47.DBW6 | Word | 侧面工位剔除位 |
| 1#CameraFailureFeedback | DB47.DBB8 | Byte | 正面工位停机标识（0/1/2/3） |
| 2#CameraFailureFeedback | DB47.DBB9 | Byte | 背面工位停机标识 |
| 3#CameraFailureFeedback | DB47.DBB10 | Byte | 端面工位停机标识 |
| 4#CameraFailureFeedback | DB47.DBB11 | Byte | 侧面工位停机标识 |
| 1#CameraWorkDone | DB47.DBX12.0 | Bool | 正面拍照完成 |
| 2#CameraWorkDone | DB47.DBX12.1 | Bool | 背面拍照完成 |
| 3#CameraWorkDone | DB47.DBX12.2 | Bool | 端面拍照完成 |
| 4#CameraWorkDone | DB47.DBX12.3 | Bool | 侧面拍照完成 |
| CameraReady | DB47.DBX12.4 | Bool | 八个相机全部就绪 |
| CameraOnline | DB47.DBX12.5 | Bool | 心跳信号（200ms 交替 1/0） |

### 3.3 数据发送流程

```
检测完成 → 工位处理器生成 StatusList (P 个字符串)
  │
  ├─ 1. StationDefectConfig.Resolve(station, statusList)
  │       逐盒匹配缺陷配置 → IsReject(bool) + StopLevel(int)
  │
  ├─ 2. 聚合:
  │       ushort rejectBits = 0;
  │       int stopLevel = 0;
  │       for each box:
  │           if (any IsReject) rejectBits |= (1 << boxIndex);
  │           stopLevel = Max(stopLevel, maxStopLevelOfBox);
  │
  ├─ 3. plc.Write("DB47.DBW0/2/4/6", rejectBits);    // Word
  │     plc.Write("DB47.DBB8/9/10/11", (byte)stopLevel); // Byte
  │
  └─ 4. plc.Write("DB47.DBX12.0/1/2/3", true);       // Bool
         只写 true，PLC 端自行清除
```

### 3.4 心跳

- 地址：`DB47.DBX12.5`
- 间隔：200ms（与现有 150ms ZMC 心跳/500ms PLC 心跳区分）
- 逻辑：`bool toggle = !toggle; plc.Write("DB47.DBX12.5", toggle);`
- PLC 端判断：一段时间内值未变化 → 通讯断开

### 3.5 CameraReady

- 八个相机全部初始化完成后，`plc.Write("DB47.DBX12.4", true)`
- 具体：`CameraManager` 所有 `InitCamera` 完成且状态均为 Connected → 触发 `OnAllCamerasReady` 事件 → `PlcResultService.SendCameraReady()`

---

## 4. Bug 修复

### 4.1 侧面 StatusList 从未赋值

**文件：** `SideStationProcessor.cs`  
**问题：** `FinalizeResults()` 和 `ProcessResults()` 计算了本地 `mergedStatus`，但从未赋值给 `this.StatusList`，导致 `MainFrm` 读取 `_sideStation.StatusList` 始终为空列表。  
**修复：** 在结果确定后写入 `StatusList = new List<string>(mergedStatus)`，与 Front / Back / EndFace 工位对齐。

### 4.2 背面多缺陷覆盖

**文件：** `BackStationProcessor.cs` 第 274 行  
**问题：** `status[d.BoxIndex] = d.DefectType` 直接覆盖，当同一盒子既有条码错又有日期码错时只保留最后一个。  
**修复：**
```csharp
// Before:
status[d.BoxIndex] = d.DefectType;

// After:
if (status[d.BoxIndex] == "OK")
    status[d.BoxIndex] = d.DefectType;
else
    status[d.BoxIndex] += "," + d.DefectType;
```

### 4.3 条码/日期码缺陷名匹配

**问题：**
- 旧 `DefectPriority.json` 中配置 `"条码错误"`，代码实际生成 `"条码错:xxx"`，`Contains("条码错误")` 返回 `false`
- `"日期码重影"` 作为单独缺陷项过于细碎，应和 `"日期码错误"` 统一走 NG剔除

**修复：**
- 新 `StationDefectConfig.json` 使用子串匹配，按序命中：
  - `"日期码不完全正确"` 优先命中 `"日期码不完全正确(xxx)"` → NG不剔除
  - `"日期码"` 兜底命中 `"日期码错误(xxx)"` 和 `"日期码重影"` → NG剔除
  - `"条码缺少"` 命中 → NG剔除
  - `"条码错"` 命中 `"条码错:181712303"` → NG不剔除

---

## 5. 类设计

### 5.1 新建类

**`VisionMeasure/Config/StationDefectConfig.cs`**

```
单例模式，线程安全
- Load() : void                          // 从 JSON 文件加载
- Save() : void                          // 保存回 JSON（供后续 UI 编辑）
- Resolve(string stationKey, List<string> statusList)
    → (ushort rejectBits, int stopLevel) // 核心：状态列表→PLC信号
- GetDefectEntries(string stationKey)
    → List<DefectEntry>                  // 获取某工位所有规则
```

依赖数据模型：
```csharp
public class DefectEntry
{
    public string Name { get; set; }      // 匹配名（子串匹配）
    public bool IsReject { get; set; }    // 是否剔除
    public int StopLevel { get; set; }    // 停机标识 0/1/2/3
}

public class StationDefectRule
{
    public string StationKey { get; set; }
    public List<DefectEntry> Defects { get; set; }
}

public class StationDefectConfigRoot
{
    public Dictionary<string, StationDefectRule> Stations { get; set; }
    public DefectEntry DefaultDefect { get; set; }
}
```

### 5.2 重构类

**`VisionMeasure/Hardware/PlcResultService.cs`**

```csharp
// 重构后接口
public class PlcResultService : IDisposable
{
    // 初始化时从 S7_1500Class 注入
    public PlcResultService(S7_1500Class s7, bool simulateMode);

    // 核心发送：写入 rejectBits(Word) + stopLevel(Byte)
    public bool SendStationResult(StationType station, ushort rejectBits,
                                   int stopLevel, int pCount);

    // 拍照完成：只写 true，PLC 清除
    public bool SendStationComplete(StationType station);

    // 全部相机就绪
    public bool SendCameraReady();

    // 心跳控制
    public void StartHeartbeat();
    public void StopHeartbeat();
}
```

**`PLC监控/Class/S7_1500Class.cs`**

- 心跳地址改为 `DB47.DBX12.5`
- 心跳逻辑改为 200ms 交替 `WriteBool(true/false)`
- 移除 `DB1000.DBD48` 硬编码
- 新增 `WriteByte(string dbAddr, byte value)` 方法（HSL 原生支持 `plc.Write(dbAddr, byte)`）

### 5.3 废弃类/功能

| 类/功能 | 处理 |
|----------|------|
| `VisionMeasure/Hardware/PlcCommunication.cs` | 整体移除（纯桩代码，从未使用） |
| `DefectPriorityConfig.cs` + `DefectPriority.json` | 被 StationDefectConfig 替代，保留兼容期后移除 |
| `PlcResultService` 中 Modbus 分支 | 移除，仅保留 S7-1500 |

---

## 6. MainFrm 集成

### 6.1 初始化流程变更

```
MainFrm 构造
  ├─ StationDefectConfig.Load()           // 加载缺陷配置 JSON
  ├─ S7_1500Class 实例化 → ConnectModbus()
  ├─ PlcResultService 实例化（注入 S7_1500Class）
  │     └─ StartHeartbeat()               // 200ms 交替 1/0
  ├─ CameraManager.InitAll()
  │     └─ OnAllCamerasReady → SendCameraReady()
  └─ 工位处理器初始化（不变）
```

### 6.2 检测结果回调重构

现在每个工位各自写 PLC 调用。重构后统一为：

```csharp
// Front (特殊: 有 OnPlcResult 事件)
_frontStation.OnPlcResult += (pCount, _, _) => {
    var (bits, level) = StationDefectConfig.Instance.Resolve("Front", _frontStation.StatusList);
    _plcResultService.SendStationResult(StationType.Front, bits, level, pCount);
    _plcResultService.SendStationComplete(StationType.Front);
};

// Back / EndFace / Side (OnResultReady → OnStationResult)
void OnStationResult(ProductResult result) {
    if (result.BackResult.HasValue)    SendPlcForStation(StationType.Back,    _backStation.StatusList);
    if (result.EndFaceResult.HasValue) SendPlcForStation(StationType.EndFace, _endFaceStation.StatusList);
    if (result.SideResult.HasValue)    SendPlcForStation(StationType.Side,    _sideStation.StatusList);
}

void SendPlcForStation(StationType st, List<string> statusList) {
    var (bits, level) = StationDefectConfig.Instance.Resolve(st.ToString(), statusList);
    _plcResultService.SendStationResult(st, bits, level, statusList.Count);
    _plcResultService.SendStationComplete(st);
}
```

---

## 7. 涉及文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `VisionMeasure/Config/StationDefectConfig.json` | **新建** | 缺陷→PLC 映射配置 |
| `VisionMeasure/Config/StationDefectConfig.cs` | **新建** | 配置加载+匹配引擎 |
| `VisionMeasure/Hardware/PlcResultService.cs` | **重构** | 纯 S7-1500，新接口 |
| `VisionMeasure/Hardware/PlcCommunication.cs` | **删除** | 桩代码 |
| `PLC监控/Class/S7_1500Class.cs` | **微改** | DB47 心跳 200ms，新增 WriteByte |
| `VisionMeasure/Stations/FrontStationProcessor.cs` | **不动** | 仅外部调用方式变 |
| `VisionMeasure/Stations/BackStationProcessor.cs` | **修复** | 多缺陷覆盖 Bug |
| `VisionMeasure/Stations/EndFaceStationProcessor.cs` | **不动** | OK |
| `VisionMeasure/Stations/SideStationProcessor.cs` | **修复** | StatusList 赋值 Bug |
| `VisionMeasure/From/MainFrm.cs` | **重构** | 统一 PLC 发送逻辑 |
| `VisionMeasure/Config/DefectPriorityConfig.cs` | **标记废弃** | 被替代 |
| `bin/Config/StationDefectConfig.json` | **新建** | 运行时配置副本 |
| `setup.ini` | **修改** | 移除 Modbus 相关项，加 S7 DB47 地址 |

---

## 8. 注意事项

1. **S7-1500Class 是 PLC监控 项目的类**，PlcResultService（VisionMeasure 项目）通过项目引用调用它
2. **字节序**：HslCommunication 默认 Big-Endian，S7-1500 也是 Big-Endian，无需转换
3. **侧面工位 classId→中文名映射**暂不处理，等后续 YOLO 模型有确定的 classId 含义后再加
4. **"缺料"缺陷**当前代码中尚无对应产生逻辑，预留 Global 配置，后续接入时直接用
5. **兼容期**：旧的 `DefectPriorityConfig` 保留文件不删，但不再加载，防止回滚需要
