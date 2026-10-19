# 项目架构文档

## 1. 总体架构

系统采用经典的 **MVVM + 分层** 架构，自上而下分为四层：

```
┌─────────────────────────────────────────────────────────┐
│  Views（XAML 视图层）                                     │
│  MainWindow + 7 个页面 UserControl，仅做数据绑定            │
├─────────────────────────────────────────────────────────┤
│  ViewModels（视图模型层）                                  │
│  CommunityToolkit.Mvvm，持引擎引用，暴露命令与可观察属性      │
├─────────────────────────────────────────────────────────┤
│  Services（服务层 / 业务引擎）                             │
│  ├── Modbus 协议层：ModbusRTUClient / ModbusFrame / Crc16 │
│  ├── 采集引擎：PollingService / SensorCollectService      │
│  ├── 业务引擎：AlarmEngine / ControlEngine                │
│  ├── 历史引擎：HistorySampler / HistoryStore              │
│  └── 串口层：SerialPortService                            │
├─────────────────────────────────────────────────────────┤
│  Models（领域模型）                                        │
│  SensorPoint / Device / Alarm / SystemConfig              │
└─────────────────────────────────────────────────────────┘
```

- **组合根（Composition Root）**：`App.xaml.cs` 的 `OnStartup`，负责创建所有全局引擎、完成依赖注入与事件接线。
- **依赖方向**：单向向下，上层依赖下层，下层不感知上层。View 通过 `DataTemplate` 根据 ViewModel 类型自动匹配（见 `MainWindow.xaml`）。

## 2. 启动与依赖注入（组合根）

`App.OnStartup` 是唯一的装配点，启动流程如下：

1. **加载配置**：`SystemConfig.LoadOrCreate()` 读取 `config.json`，不存在或损坏时生成示例配置。
2. **构建依赖链**：
   ```
   SerialPortService ──► ModbusRTUClient ──► PollingService ──► SensorCollectService
                                                                AlarmEngine
                                                                ControlEngine（依赖 ModbusRTUClient）
   HistoryStore ──► HistorySampler（依赖 config.Sensors）
   ```
3. **注册数据源**：`collect.AddSensors(config.Sensors)`、`control.AddDevices(config.Devices)`，把配置对象注册进运行时引擎。
4. **事件接线**：
   ```
   collect.sensorValueUpdated ──► Dispatcher.InvokeAsync(alarm.Check)  // 报警切 UI 线程
                              └──► control.Check(sensor)               // 控制留后台线程（阻塞 IO）
   ```
5. **创建主窗口**：把全部引擎注入 `MainWindowViewModel`，设置 `DataContext` 并显示。

> 注意：`PollingService` / `HistorySampler` 的 `Start()` 不是在这里调用，而是由「通信设置」页在用户点击连接后触发。

## 3. 数据模型（Models/ModBus）

| 类 | 职责 | 关键设计 |
|----|------|----------|
| `SensorPoint` | 一个测点 | 静态配置（Name/SlaveId/寄存器地址/量程/阈值…）+ 运行时状态（CurrentValue/IsOnline/IsAlarming/LastUpdateTick）分离；`IsFresh()` 判定数据新鲜度 |
| `Device` | 一个控制设备 | 通过写单寄存器（功能码 06）控制开关；自动控制参数（关联测点、OnAt/OffAt、TurnsOnBelow） |
| `Alarm` | 一条报警记录 | 类型（超上限/超下限/通信故障）、级别、状态（活跃/已确认） |
| `SystemConfig` | 系统配置 | `Sensors` + `Devices`，负责 JSON 读写 |

### 关键约定

- **运行时状态字段** 用 `[ObservableProperty]` + `[property: JsonIgnore]`，既支持 WPF 双向绑定，又不会污染配置文件。
- **`SensorPoint.ToPhysical(ushort raw)`**：原始值 → 物理值（按 DataType 处理符号位，再乘 Scale）。
- **在线（新鲜度）判定**：`IsOnline` 仅表示"最近一次读取成功"；真正的"在线"用 `IsFresh()`（`IsOnline && TickCount64 - LastUpdateTick <= StaleMs`，`StaleMs` 默认 3000ms）。监控卡片与底栏在线数统一用此口径，避免串口断开后收不到事件却仍显示在线。
- **`Clone()` 目前未被调用**：`SensorPoint.Clone()` / `Device.Clone()` 仍保留，但配置页已改为独立编辑集合，不再克隆运行对象（见 §9）。

## 4. 串口层与 Modbus 协议层

### 4.1 SerialPortService（串口）

- 实现 `ISerialPortService`，封装 `System.IO.Ports.SerialPort`。
- `Open()` 启动独立后台读线程 `ReadLoop`，持续 `Read` 并把数据通过 `DataReceived` 事件抛出。
- `ReadTimeout = 200ms`（另设 `WriteTimeout = 2000ms`、`ReadBufferSize = 8192`），正常超时忽略，异常则触发 `ErrorOccurred` 并退出读线程。
- `Open()` 失败时触发 `ErrorOccurred` 并抛出 `SerialPortFailureException`（避免上层误判为"已连接"）；`Send()` 的写超时、端口拔出/被抢占等所有异常统一包装为 `SerialPortFailureException`（不再静默 `return 0`）。
- **读线程生命周期**：读线程持有"本次连接"的 `SerialPort` 实例引用（不读字段），重连后新旧线程不会抢同一句柄；`Close()` 按"线程是否存活"决定是否 `Join`，超时才 `Dispose` 端口强制打断，确保线程必定回收；读线程异常退出时自动置 `IsOpen=false` 并释放端口，保证下次重连不会撞"端口被占用"。
- **故障上报**：`PollingService` 订阅 `ErrorOccurred` 并转为 `connectionLost`（主动断开期间抑制），`App` 同时订阅写文件日志（`logs\yyyyMMdd.log`）；`App.OnExit` 负责 `Stop()` + `Dispose()` 释放串口。
- 接口抽象 `ISerialPortService` 便于后续替换/测试。

### 4.2 ModbusRTUClient（Modbus 客户端）

- 依赖 `ISerialPortService`，通过 `DataReceived` 事件把数据推入内部缓冲区 `_rBuffer`。
- **收发事务 `TransactCore`**：
  - 用 `SemaphoreSlim _questGate`（容量 1）串行化请求，保证同一时刻只有一个请求在途。
  - 发送前清空接收缓冲 + 重置 `AutoResetEvent`，发送后按超时 `timeoutMs` 等待。
  - 先收满 5 字节头做校验（从站地址、功能码、异常码），再收完整帧。
  - 校验：从站地址匹配 → 功能码匹配 → CRC16 校验。
- 支持读线圈/离散输入/保持寄存器/输入寄存器、写单/多线圈、写单/多寄存器，以及广播写（从站 0，无回复）。广播写同样经过 `_questGate`，避免插进"请求-应答"之间打乱收发流。
- `timeoutMs`（默认 500）/ `guardMs`（默认 20）可调。

### 4.3 ModbusFrame / Crc16 / ModbusTiming

- `ModbusFrame`：纯静态类，负责请求帧构造与响应帧解析，定义了功能码常量和读写数量上限。
- `Crc16`：Modbus CRC16（多项式 `0xA001`，反射），使用预计算查找表加速；`Validate` 校验整帧。
- `ModbusTiming`：根据波特率计算字符时间、帧间隔等时序参数。

## 5. 采集引擎（轮询链路）

```
PollingService（轮询调度线程）
   │  AddPollItem(PollItem)
   │  valueUpdated / pollFailed 事件
   ▼
SensorCollectService（适配器）
   │  Dictionary<PollItem, SensorPoint> 映射
   ▼
sensorValueUpdated(SensorPoint)  ──► AlarmEngine / ControlEngine
```

### 5.1 PollingService

- 实现 `IPollingService`，维护 `List<PollItem>`（加锁保护）。
- `Start(SerialSetting)`：打开串口（失败抛 `SerialPortFailureException` 并中断，不启动轮询线程），启动后台线程 `PollLoop`；`IsPortOpen` 暴露真实链路状态。
- 轮询循环：快照 `pollItems` → 逐个检查 `nextPollTick` 决定是否到点 → 读取 → 触发 `valueUpdated` / `pollFailed`。
- **自动重连**：`TimeoutException` / `ModbusException` / 其它异常累计失败 `reconnectMaxFailCount`（3）次后，关闭串口、等待 `reconnectDelayMs`（5s，可被 `Stop()` 的 Cancel 提前唤醒）后重开。
- **链路事件**：重连成功触发 `connectionRestored`，重连失败触发 `connectionLost(reason)`（通信页据此把"已连接"改回断开，避免假在线）；`SerialPortFailureException` 视为串口硬故障，不触发重连。
- `PollItem` 含 `slaveId`、`funcCode`、`addr`、`count`、`intervalMs`、`nextPollTick`。

### 5.2 SensorCollectService（适配器模式）

- 把底层的 `PollItem` 映射到领域对象 `SensorPoint`。
- `AddSensor`：创建 `PollItem`（读保持寄存器、count=1、interval=1s）并建立 `map` 映射。
- 收到 `valueUpdated`：取 `ushort[]` 第一个值 → `sensorPoint.CurrentValue = ToPhysical(raw)` → `IsOnline = true` → 刷新 `LastUpdateTick = Environment.TickCount64` → 触发 `sensorValueUpdated`。
- 收到 `pollFailed`：`IsOnline = false`。

## 6. 业务引擎

### 6.1 AlarmEngine（报警）

- 维护 `Dictionary<SensorPoint, bool>` 记录每个测点的报警状态，做**边沿检测**：
  - 从正常 → 越限：创建 `Alarm` 加入 `ActiveAlarms`，触发 `alarmNotify`。
  - 从越限 → 正常：从 `ActiveAlarms` 移到 `HistoryAlarms`，触发 `alarmCleared`。
- `IsOutOfRange`：`!IsOnline` 直接返回 false（离线不报越限）；否则比较上下限（`CurrentValue > HighLimit || CurrentValue < LowLimit`）。
- 创建报警：按超上限/超下限判定类型，级别固定 `Warning`；恢复时用 `Source == sensor.Name` 在活跃列表定位并移到历史列表。
- `Acknowledge(alarm)` / `AcknowledgeAll()`：把活跃报警状态置为 `Acknowledged`（不移动集合）。
- 集合用 `ObservableCollection`，报警操作需在 UI 线程执行（组合根已用 `Dispatcher.InvokeAsync` 处理）。

### 6.2 ControlEngine（控制）

- 持有 `List<Device>`，通过 `ModbusRTUClient.WriteSingleReg` 写寄存器控制开关。
- **模式切换**：`SetAuto(device)` 切回自动；`SetManualMode(device)` 仅切手动（不写寄存器）；`SetManual(device, on)` 切手动并立即写命令。
- **自动**：`Check(sensor)` 遍历 Mode=Auto 且 `LinkedSensorName` 匹配的设备，按迟滞逻辑 `Evaluate`：
  - 用 `TurnsOnBelow` 区分升温类（低于阈值开）和降温类（高于阈值开）。
  - 运行中判断是否到 `OffAt`，停止中判断是否到 `OnAt`，避免频繁抖动。
- 写寄存器为阻塞 IO，控制页把命令丢到后台线程执行。
- **写失败不外抛**：`ControlEngine` 内部捕获全部异常，保持 `IsRunning` 原值（下个周期重试），通过 `writeFailed` 事件上报（组合根接到日志），避免异常冒泡回轮询线程被误判成"读失败"并触发无谓重连。
- `SetManual` 返回 `bool`，控制页据此提示成功/失败；`SetAuto` / `SetManualMode` 只改内存状态，不写寄存器。

## 7. 历史数据

- **HistorySampler**：后台线程每 `5000ms` 采样一次，遍历 `config.Sensors` 的 `CurrentValue` 生成 `HistorySample`（`Time` + `Values[]`）。
- **HistoryStore**：`Queue<HistorySample>` 环形缓冲，`MaxSamples = 17280`（24h × 每 5s），超量自动出队。新增时触发 `historySampleAdded`。
  - **线程安全**：采样线程 `Add`、UI 线程 `GetAll()/Count`，所有集合访问用 `lock` 保护，`GetAll()` 返回数组快照；事件在锁外触发，避免订阅者回调造成死锁。
- **趋势图（TrendViewModel）**：为每个测点建一条 `LineSeries<ObservablePoint>`，X 存 `DateTime.Ticks`，`Axis.Labeler` 校验 NaN/Infinity/Ticks 范围后转时间字符串（LiveCharts2 无数据时会传越界值，必须校验）。

## 8. 页面与 ViewModel 映射

`MainWindowViewModel` 构建 7 个导航项，每个 `NavItem` 持有一个页面 ViewModel，`MainWindow.xaml` 用 `DataTemplate` 按类型自动匹配 View：

| 导航项 | ViewModel | 注入依赖 | View |
|--------|-----------|----------|------|
| 实时监控 | MonitorViewModel | Config, Alarm, Control | MonitorView |
| 趋势曲线 | TrendViewModel | HistoryStore, Config | TrendView |
| 历史数据 | HistoryViewModel | HistoryStore, Config | HistoryView |
| 报警记录 | AlarmViewModel | Alarm | AlarmView |
| 设备控制 | ControlViewModel | Control, Config | ControlView |
| 参数配置 | ConfigViewModel | （无，读写 config.json） | ConfigView |
| 通信设置 | CommViewModel | Polling, HistorySampler | CommView |

### 线程模型

| 线程 | 职责 | 跨线程处理 |
|------|------|-----------|
| UI 线程 | 界面渲染、报警集合操作 | — |
| PollingService.pollLoop | 轮询读取 | 报警切回 UI 线程（Dispatcher）；控制留后台线程 |
| SerialPortService.ReadLoop | 串口读取 | 数据推入缓冲，由轮询线程消费 |
| historySampler | 定时采样 | `ObservablePoint`/标量属性由 WPF 自动跨线程；LiveCharts2 支持后台更新集合 |
| MainWindowViewModel.summaryTimer | 每 1s 统计在线测点数 | `DispatcherTimer`，天然运行在 UI 线程 |
| MonitorViewModel.freshnessTimer | 每 500ms 刷新卡片在线态 | `DispatcherTimer`，天然运行在 UI 线程 |

## 9. 配置编辑的设计决策

参数配置页（`ConfigViewModel`）持有**自己的编辑集合** `ObservableCollection<SensorPoint>` / `ObservableCollection<Device>`（构造时不注入运行中的 `config`，初始为空，可点「重新加载」从文件载入）。`Save()` 把这些集合装进一个**新的 `SystemConfig` 快照**落盘，`Reload()` 用 `SystemConfig.LoadOrCreate()` 刷新编辑集合——两条路径都**绝不修改运行中的全局 `config`**。语义为「保存 = 落盘，重启后生效」。

**原因**（为什么不直接改运行中的 `config.Sensors`）：
1. 直接 `Clear() + AddRange()` 会导致新对象孤立（没有引擎采集/报警/控制），删除的对象仍被引擎引用。
2. `HistorySampler` 后台线程并发遍历同一个 `List`，原地修改有线程安全问题。
3. 直接编辑运行对象会污染正在运行的状态。

因此配置的正确生效方式是：编辑 → 保存 → 重启，由组合根按新 `config.json` 重新接线。

> 历史实现曾用 `SensorPoint.Clone()` / `Device.Clone()` 克隆运行对象来编辑；现改为独立编辑集合后 `Clone()` 已无调用方。

## 10. 可扩展点与注意事项

- **接口抽象**：`IPollingService` / `ISerialPortService` 已抽象（含 `connectionLost` / `connectionRestored` 事件），可替换为模拟实现用于单元测试。
- **自定义异常**：位于 `Services/MeException/`——`ModbusException`（协议异常，携带功能码与异常码）、`SerialPortFailureException`（串口打开 / 写超时）。
- **HexUtil / LogWriter** 位于 `SerialAssistant`，是通用工具类，当前未被主流程接线，属于预留能力。
- **顶栏 / 底栏已接线**：`MainWindowViewModel` 已把连接状态、活跃报警数、串口参数、轮询周期、在线测点数绑定到运行状态（含 1s 刷新定时器）。
- **串口枚举**：`SerialPortUtil.GetPorts()` 优先用 WMI 获取带描述的串口，失败时回退到 `SerialPort.GetPortNames()`。
- **已知临时改动（调试用）**：`SystemConfig.LoadOrCreate()` 中"读取已有配置文件"的分支被临时注释，每次启动都会 `CreateSample()` 覆盖 `config.json`，导致「参数配置」页保存的修改重启后被覆盖，与 §9 的「保存后重启生效」暂时不符。属调试用途，恢复读取逻辑后即恢复正常。
