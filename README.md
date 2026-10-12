# 温湿度采集控制系统（TempHumiditySystem）

基于 WPF + .NET 8 的温湿度监控系统，通过 **Modbus RTU** 协议经串口采集温湿度传感器数据，实现实时监控、越限报警、设备自动/手动控制、历史数据记录与趋势曲线展示。

## 功能特性

| 模块 | 说明 |
|------|------|
| 实时监控 | 按位置（`GroupName`）分组的温湿度卡片（同组需同时具备温度与湿度测点才生成卡片），越限标红，在线态按数据新鲜度刷新，展示设备运行状态与活跃报警 |
| 趋势曲线 | 每个测点一条实时曲线（LiveCharts2），X 轴为时间 |
| 历史数据 | 每 5s 采样一次，内存保留 24h，支持导出 CSV（UTF-8 BOM，Excel 不乱码） |
| 报警记录 | 活跃报警 / 历史报警分离，支持单条确认与全部确认 |
| 设备控制 | 加热器/加湿器/除湿机/风机的手动开关与手动/自动模式切换 |
| 参数配置 | 测点表 + 设备表编辑，保存到 `config.json`，重启后生效 |
| 通信设置 | 串口选择 + 波特率等参数配置，连接后启动轮询与采样 |

## 技术栈

- **框架**: WPF，`.NET 8`（`net8.0-windows`）
- **MVVM**: `CommunityToolkit.Mvvm` 8.4.2
- **图表**: `LiveChartsCore.SkiaSharpView.WPF` 2.0.0-rc5.4
- **串口**: `System.IO.Ports` 10.0.11
- **串口枚举**: `System.Management` 8.0.0（WMI）

## 快速开始

### 环境要求

- Windows 10/11
- .NET 8 SDK
- 可用的串口设备（或使用虚拟串口进行调试）

### 构建与运行

```powershell
# 还原依赖
dotnet restore

# 运行
dotnet run --project TempHumiditySystem/TempHumiditySystem.csproj
```

或使用 Visual Studio 2022 打开 `TempHumiditySystem.sln` 直接 F5 运行。

### 使用流程

1. 启动时会生成示例配置（`%APPDATA%\TempHumiditySystem\config.json`）：1 组传感器（1#仓库温度 + 1#仓库湿度，共 2 测点）与 4 台设备（加热器 / 加湿器 / 除湿机 / 风机）。**注意**：当前 `SystemConfig.LoadOrCreate()` 的"读取已有配置"分支被注释，每次启动都会用示例配置覆盖该文件（见「已知问题」）。
2. 进入「通信设置」页，选择串口与波特率，点击「连接」。
3. 连接成功后轮询线程开始采集，历史采样线程每 5s 记录一次数据。
4. 在「参数配置」页修改测点/设备后保存，**重启程序生效**。

## 项目结构

```
TempHumiditySystem/
├── TempHumiditySystem.sln            # 解决方案
└── TempHumiditySystem/
    ├── App.xaml / App.xaml.cs        # 应用入口 + 组合根（组装引擎、接线、注入）
    ├── MainWindow.xaml / .cs         # 主窗口（顶栏 + 左侧导航 + 内容区 + 底栏）
    ├── TempHumiditySystem.csproj
    ├── Models/
    │   └── ModBus/                   # 领域模型
    │       ├── SensorPoint.cs        # 测点（静态配置 + 运行时状态）
    │       ├── Device.cs             # 控制设备
    │       ├── Alarm.cs              # 报警记录
    │       └── SystemConfig.cs       # 系统配置（JSON 读写）
    ├── Services/
    │   ├── Interface/                # 抽象接口（IPollingService / ISerialPortService）
    │   ├── MeException/              # 自定义异常
    │   │   ├── ModbusException.cs    # Modbus 协议异常（功能码 + 异常码）
    │   │   └── SerialPortFailureException.cs # 串口打开/写超时异常
    │   ├── SerialAssistant/          # 串口层
    │   │   ├── SerialPortService.cs  # 串口读写（后台读线程）
    │   │   ├── SerialSetting.cs      # 串口参数
    │   │   ├── SerialPortUtil.cs     # 串口枚举（WMI）
    │   │   ├── SerialPortInfo.cs
    │   │   ├── HexUtil.cs            # ASCII/HEX 转换工具（预留，未接线）
    │   │   └── LogWriter.cs          # 日志写入工具（预留，未接线）
    │   └── Modbus/                   # Modbus RTU 协议层 + 业务引擎
    │       ├── ModbusRTUClient.cs    # Modbus 客户端（收发/CRC/超时）
    │       ├── ModbusFrame.cs        # 数据帧构造与解析
    │       ├── Crc16.cs              # CRC16 校验（查表法）
    │       ├── ModbusTiming.cs       # 波特率相关时序计算
    │       ├── PollItem.cs           # 轮询项
    │       ├── PollingService.cs     # 轮询调度（后台线程 + 自动重连）
    │       ├── SensorCollectService.cs # 采集适配器（PollItem→SensorPoint）
    │       ├── AlarmEngine.cs        # 报警引擎
    │       ├── ControlEngine.cs      # 控制引擎
    │       ├── HistoryStore.cs       # 历史存储（环形队列）
    │       ├── HistorySample.cs      # 采样数据
    │       └── HistorySampler.cs     # 历史采样线程
    ├── ViewModels/                   # 页面 ViewModel
    │   ├── MainWindowViewModel.cs    # 主窗口 VM + 导航
    │   ├── MonitorViewModel.cs       # 实时监控
    │   ├── TrendViewModel.cs         # 趋势曲线
    │   ├── HistoryViewModel.cs       # 历史数据
    │   ├── AlarmViewModel.cs         # 报警记录
    │   ├── ControlViewModel.cs       # 设备控制
    │   ├── ConfigViewModel.cs        # 参数配置
    │   ├── CommViewModel.cs          # 通信设置
    │   └── NavItem.cs                # 导航项
    └── Views/                        # 页面视图（7 个 XAML）
```

## 配置说明

配置文件路径：`%APPDATA%\TempHumiditySystem\config.json`，统一使用带 BOM 的 UTF-8 读写，中文以明文落盘，字段名大小写不敏感。

### 测点（SensorPoint）

| 字段 | 说明 |
|------|------|
| `Name` | 测点名 |
| `SlaveId` | Modbus 从站地址 |
| `RegisterAddress` | 寄存器地址 |
| `DataType` | 数据类型：`UInt16` / `Int16` / `Float32` |
| `Scale` | 缩放系数（原始值 × Scale = 物理值） |
| `Unit` | 单位（如 ℃、%RH） |
| `Category` | 类别：`Temperature` / `Humidity` |
| `HighLimit` / `LowLimit` | 报警上/下限 |
| `GroupName` | 分组名（同一位置温度+湿度归一组） |

### 设备（Device）

| 字段 | 说明 |
|------|------|
| `Name` / `Type` | 设备名 / 类型（Heater/Humidifier/Dehumidifier/Fan） |
| `SlaveId` / `WriteAddress` | 从站地址 / 写寄存器地址（功能码 06） |
| `LinkedSensorName` | 关联测点名 |
| `OnAt` / `OffAt` | 开启/关闭阈值 |
| `TurnsOnBelow` | 开启方向：`true`=低于阈值开启（升温类）；`false`=高于阈值开启（降温类） |

运行时状态字段（`SensorPoint.CurrentValue` / `IsOnline` / `IsAlarming` / `LastUpdateTick`、`Device.Mode` / `IsRunning`）均标记 `[JsonIgnore]`，不会写入配置文件。

在线判定：`SensorPoint.IsFresh()` 以「最近一次成功读取的时刻 `LastUpdateTick` 未超过 `StaleMs`（默认 3000ms，≈3 倍轮询周期）」为准；监控卡片与底栏"在线测点数"统一采用该口径。

## 已知问题 / 注意事项

- **配置暂不会持久化生效（调试期间的临时改动）**：`SystemConfig.LoadOrCreate()` 中「读取已有配置」的分支被临时注释，每次启动都会 `CreateSample()` 覆盖 `config.json`，因此「参数配置」页保存的修改会在下次启动被覆盖。属调试用途，恢复读取逻辑后即恢复正常。
- `SensorPoint.Clone()` / `Device.Clone()` 目前无调用方（配置页已改为独立编辑集合），暂时保留。
- `HexUtil` / `LogWriter` 为预留工具类，尚未接入主流程。

## 相关文档

- [项目架构文档](./ARCHITECTURE.md)
