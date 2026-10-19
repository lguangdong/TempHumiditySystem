# 项目架构文档

## 1. 总体架构

系统采用 **MVVM + 分层** 架构，自上而下分为四层，并配有日志、异常兜底、共享 UI 资源等横切能力：

```
┌──────────────────────────────────────────────────────────────┐
│  Views（XAML 视图层）                                          │
│  MainWindow + 7 个页面 UserControl，只做数据绑定与简单样式        │
├──────────────────────────────────────────────────────────────┤
│  ViewModels（视图模型层）                                       │
│  CommunityToolkit.Mvvm：可观察属性、命令、编辑模型与校验           │
├──────────────────────────────────────────────────────────────┤
│  Services（服务层 / 业务引擎）                                   │
│  ├── Modbus 协议层：ModbusRTUClient / ModbusFrame / Crc16      │
│  ├── 采集引擎：PollingService / SensorCollectService           │
│  ├── 业务引擎：AlarmEngine / ControlEngine                     │
│  ├── 历史引擎：HistorySampler / HistoryStore                   │
│  └── 串口层：SerialPortService                                 │
├──────────────────────────────────────────────────────────────┤
│  Models（领域模型）                                             │
│  SensorPoint / Device / Alarm / SystemConfig / AppSettings     │
└──────────────────────────────────────────────────────────────┘
```

- **组合根（Composition Root）**：`App.xaml.cs` 的 `OnStartup`，集中创建引擎、完成依赖注入与事件接线。
- **依赖方向**：单向向下，上层依赖下层；View 由 `MainWindow.xaml` 中的 `DataTemplate` 按 ViewModel 类型自动匹配。
- **横切能力**：文件日志（`LogWriter`）、全局异常兜底、共享资源字典（`Resources/DeviceToolTips.xaml`）、LiveCharts 全局中文字体。

## 2. 启动与装配

`App.OnStartup` 是唯一的装配点，按以下顺序执行：

1. **注册全局异常处理**：`DispatcherUnhandledException`、`TaskScheduler.UnobservedTaskException`、`AppDomain.UnhandledException`。
2. **初始化日志**：写入 exe 同目录 `logs\yyyyMMdd.log`，启动记录立即刷盘。
3. **加载共享 UI 资源**：合并 `Resources/DeviceToolTips.xaml`；配置 LiveCharts 全局中文字体。
4. **加载配置**：`SystemConfig.LoadOrCreate()` 读取 `config.json`；文件不存在或损坏时生成示例配置。
5. **构建依赖链**：

   ```
   SerialPortService ──► ModbusRTUClient ──► PollingService ──► SensorCollectService
                                                                 AlarmEngine
                                                                 ControlEngine（依赖 ModbusRTUClient）
   HistoryStore ──► HistorySampler（依赖 config.Sensors）
   ```

6. **注册数据源**：`collect.AddSensors(config.Sensors)`、`control.AddDevices(config.Devices)`。
7. **事件接线**：

   ```
   collect.sensorValueUpdated ──► Dispatcher.InvokeAsync(alarm.Check)  // 报警集合需在 UI 线程操作
                              └──► control.Check(sensor)               // 控制写寄存器为阻塞 IO，留在轮询线程
   port.ErrorOccurred         ──► 日志
   polling.connectionLost     ──► 日志（同时由 CommViewModel 订阅以刷新界面）
   polling.connectionRestored ──► 日志
   control.writeFailed        ──► 日志
   ```

8. **创建主窗口**：把全部引擎注入 `MainWindowViewModel`，设置 `DataContext` 并显示。

> `PollingService.Start()` / `HistorySampler.Start()` 不在启动阶段调用，由「通信设置」页在用户点击连接后触发。

**退出顺序**（`OnExit`）：先 `MainWindowViewModel.Dispose()`（停定时器、退订事件、级联释放页面 VM），再 `PollingService.Stop()`，然后 `SerialPortService.Dispose()`，最后关闭日志。

## 3. 数据模型（Models/ModBus）

| 类 | 职责 | 关键设计 |
|----|------|----------|
| `SensorPoint` | 一个测点 | 静态配置（名称、从站、寄存器、数据类型、系数、单位、类别、上下限、分组）与运行时状态（当前值、在线、报警、最后更新时间）分离 |
| `Device` | 一个控制设备 | 通过写单寄存器（功能码 06）控制开关；自动控制参数：关联测点、`OnAt` / `OffAt`、`TurnsOnBelow` |
| `Alarm` | 一条报警记录 | 类型（超上限/超下限，通信故障预留）、级别、状态（活跃/已确认）、触发值与阈值 |
| `SystemConfig` | 系统配置 | `Sensors` + `Devices`，负责 JSON 读写、原子保存与损坏文件备份 |
| `AppSettings` | 应用级设置 | 配置目录、通信参数、最近导出路径；持久化到 exe 同目录 `settings.json` |

### 关键约定

- **运行时状态字段** 使用 `[ObservableProperty]` + `[property: JsonIgnore]`：既支持 WPF 绑定，又不会写入配置文件。
- **`SensorPoint.ToPhysical(ushort raw)`**：按 `DataType` 处理符号位后乘 `Scale` 得到物理值。
- **在线（新鲜度）判定**：`IsOnline` 只表示“最近一次读取成功”；真正的在线用 `IsFresh()`（`IsOnline && TickCount64 - LastUpdateTick <= StaleMs`，`StaleMs = 3000ms`）。监控卡片与底栏在线数统一使用该口径。
- **数据类型**：仅 `UInt16` / `Int16`，每个测点占 1 个保持寄存器。旧配置中的 `Float32` 由 `ModbusDataTypeConverter` 容错回退为 `UInt16`，避免整份配置反序列化失败。
- `SensorPoint.Clone()` / `Device.Clone()` 提供静态配置复制能力，配置页使用独立的编辑模型，不依赖 `Clone()`。

## 4. 串口层与 Modbus 协议层

### 4.1 SerialPortService（串口）

- 实现 `ISerialPortService`，封装 `System.IO.Ports.SerialPort`。
- **读线程**：`Open()` 创建连接实例并启动后台 `ReadLoop`，持续 `Read` 后通过 `DataReceived` 抛出字节数组。
  - `ReadTimeout = 200ms`、`WriteTimeout = 2000ms`、`ReadBufferSize = 8192`。
  - 读线程持有**本次连接**的 `SerialPort` 实例引用，不读取字段，重连后新旧线程不会争抢同一句柄。
- **关闭**：`Close()` 先置 `IsOpen = false` 并取消令牌，让读循环自然退出；按“线程是否存活”决定 `Join`，超时才 `Dispose` 端口强制打断，再等待一次。线程未退出时保留引用并上报，避免出现两个读线程。
- **故障处理**：读线程异常退出时自动置 `IsOpen = false`、释放端口并通过 `ErrorOccurred` 上报；保证下次重连不会遇到“端口被占用”。
- **写入**：`Send()` 将写超时、端口拔出、被抢占等异常统一包装为 `SerialPortFailureException`，上层只需处理一种异常类型。
- 接口抽象 `ISerialPortService` 便于替换为模拟实现。

### 4.2 ModbusRTUClient（Modbus 客户端）

- 依赖 `ISerialPortService`，通过 `DataReceived` 把数据推入内部缓冲区。
- **收发事务 `TransactCore`**：
  - `SemaphoreSlim _questGate`（容量 1）串行化请求，保证同一时刻只有一个事务在途。
  - 发送前清空接收缓冲、重置信号，发送后按 `timeoutMs`（默认 500ms）等待。
  - 先收满 5 字节头做校验（从站地址、功能码、异常码），再收完整帧。
  - 校验顺序：从站地址 → 功能码 → CRC16。
- 支持读线圈 / 离散输入 / 保持寄存器 / 输入寄存器、写单/多线圈、写单/多寄存器，以及广播写（从站 0，无回复）。
- 广播写同样经过 `_questGate`，并在发送后等待 `guardMs`（默认 20ms）帧间隔，避免插入“请求-应答”之间打乱收发流。

### 4.3 ModbusFrame / Crc16 / ModbusTiming

- `ModbusFrame`：纯静态类，负责请求帧构造与响应帧解析，定义功能码常量与读写数量上限。
- `Crc16`：Modbus CRC16（多项式 `0xA001`，反射），预计算查找表加速；`Validate` 校验整帧。
- `ModbusTiming`：按波特率计算字符时间、帧间隔等时序参数（预留工具）。

## 5. 采集链路

```
PollingService（轮询线程）
   │  AddPollItem(PollItem)
   │  valueUpdated / pollFailed 事件
   ▼
SensorCollectService（适配器）
   │  PollItem → [(offset, SensorPoint)]
   ▼
sensorValueUpdated(SensorPoint) ──► AlarmEngine / ControlEngine
```

### 5.1 PollingService

- 实现 `IPollingService`，维护 `List<PollItem>`（加锁保护）。
- `Start(SerialSetting)`：打开串口（失败抛 `SerialPortFailureException`，不启动轮询线程），创建轮询线程；`IsPortOpen` 暴露真实链路状态。
- `Start` / `Stop` 由 `_lifecycleLock` 保护，避免并发调用产生两个轮询线程；`Stop` 在 `Join` 超时时保留线程引用，拒绝重入。
- **轮询循环**：快照 `pollItems` → 逐个检查 `nextPollTick` → 读取 → 触发 `valueUpdated` / `pollFailed`；循环等待时间按最近到期项计算（1~50ms），不做固定空转。
- **自动重连**：`TimeoutException` / `ModbusException` / `SerialPortFailureException` / 其它异常累计失败 3 次后，关闭串口、等待 5 秒（可被 `Stop` 的取消令牌提前唤醒）后重开。
- **链路事件**：重连成功触发 `connectionRestored`；重连失败或串口层故障触发 `connectionLost(reason)`，通信页据此把“已连接”改回断开，避免假在线。

### 5.2 SensorCollectService（适配器）

- 把底层 `PollItem` 映射到领域对象 `SensorPoint`。
- **批量读**：`AddSensors` 按 `SlaveId` 分组、按寄存器地址排序，把跨度 `< MaxRegistersPerRead`（8）的测点合并成一次 `03` 读（例如 0x0000 温度 + 0x0001 湿度 → 一次读 2 个寄存器）；映射结构为 `PollItem → [(offset, SensorPoint)]`。
- 收到 `valueUpdated`：按绑定偏移取值 → `CurrentValue = ToPhysical(raw[offset])` → `IsOnline = true` → 刷新 `LastUpdateTick` → 对每个测点触发 `sensorValueUpdated`。
- 收到 `pollFailed`：该 `PollItem` 下所有测点 `IsOnline = false`。

## 6. 业务引擎

### 6.1 AlarmEngine（报警）

- 维护 `Dictionary<SensorPoint, bool>` 记录测点报警状态，做**边沿检测**：
  - 正常 → 越限：创建 `Alarm` 加入 `ActiveAlarms`，触发 `alarmNotify`。
  - 越限 → 正常：从 `ActiveAlarms` 移到 `HistoryAlarms`，触发 `alarmCleared`。
- `IsOutOfRange`：`!IsOnline` 时直接返回 false（离线不判越限）；否则比较 `CurrentValue > HighLimit || CurrentValue < LowLimit`。
- 报警类型按超上限/超下限判定，级别固定 `Warning`。
- 历史报警保留上限 `MaxHistoryAlarms = 500`，超出时移除最旧记录。
- `Acknowledge(alarm)` / `AcknowledgeAll()`：把活跃报警状态置为 `Acknowledged`，不移动集合。
- 集合为 `ObservableCollection`，报警操作在 UI 线程执行（组合根通过 `Dispatcher.InvokeAsync` 派发）。

### 6.2 ControlEngine（控制）

- 持有 `List<Device>`，通过 `ModbusRTUClient.WriteSingleReg` 写寄存器控制开关。
- **模式切换**：
  - `SetAuto(device)`：切回自动；
  - `SetManualMode(device)`：仅切手动，不写寄存器；
  - `SetManual(device, on)`：切手动并立即下发命令，返回是否写入成功。
- **自动控制**：`Check(sensor)` 遍历 `Mode = Auto` 且 `LinkedSensorName` 匹配的设备，按迟滞逻辑判断：
  - 用 `TurnsOnBelow` 区分升温类（低于阈值开）与降温类（高于阈值开）。
  - 运行中比较 `OffAt`，停止中比较 `OnAt`，避免阈值附近频繁启停。
- **写失败隔离**：`ControlEngine` 内部捕获写寄存器异常，保持 `IsRunning` 原值（下个周期重试），通过 `writeFailed` 事件上报；异常不会冒泡回轮询线程，避免被误判为“读取失败”而触发重连。
- 写寄存器是阻塞 IO，控制页把命令放到后台线程执行。

## 7. 历史数据与趋势

### 7.1 HistorySampler

- 后台线程每 `1000ms` 采样一次（与轮询周期一致），遍历 `config.Sensors` 生成 `HistorySample`（`Time` + `Values[]`）。
- **离线写 NaN**：`IsFresh()` 为 false 时该列写 `double.NaN`，不写 0 或最后一个陈旧值；趋势图据此断线，CSV 对应单元格留空。
- **过滤空样本**：若一次采样所有测点都无效（刚启动、整体掉线、配置为空），整条样本不写入 `HistoryStore`。历史页条数、时间范围与导出 CSV 均不包含这些空时间点。
- 采样线程由「通信设置」页在连接成功后启动，断开时停止。

### 7.2 HistoryStore

- `Queue<HistorySample>` 环形缓冲，`MaxSamples = 86400`（24 小时 × 1 秒），超量自动出队。
- **线程安全**：采样线程 `Add`，UI 线程 `GetAll()` / `Count` / `FirstSampleTime` / `LastSampleTime`，所有集合访问加锁；`GetAll()` 返回数组快照；`historySampleAdded` 事件在锁外触发，避免订阅者回调造成死锁。

### 7.3 TrendViewModel

- 每个测点一条 `LineSeries<ObservablePoint>`，X 轴存 `DateTime.Ticks`，`Axis.Labeler` 校验 NaN / Infinity / Ticks 越界后转为时间字符串。
- **索引映射**：曲线缓冲使用 `HistorySample.Values` 的下标关联测点（`HistorySampler` 按 `config.Sensors` 顺序采样），不使用测点名做键，避免重名测点互相覆盖。
- **点数控制**：每条曲线保留最近 `Window`（默认 1 小时，约 3600 点），另有 `MaxPointsPerSeries = 4000` 硬上限；`Trim` 从头部丢弃过期点，正常每秒只移除 1 个点。
- **断点**：离线采样值 `double.NaN` 转为 `null` 后再交给 LiveCharts；`EnableNullSplitting` 只识别 `null`，NaN 会污染路径计算导致整条线无法绘制。
- **双 Y 轴**：温度系列 `ScalesYAt = 0`（左轴），湿度系列 `ScalesYAt = 1`（右轴），右轴不画横向网格线。
- **样式**：6 色柔和调色板、纯折线（`Fill = null`）、绘图区边框、图例置底、`TooltipPosition = Top`、`ZoomMode = X`、200ms 动画。

## 8. 页面与 ViewModel 映射

`MainWindowViewModel` 构建 7 个导航项，每个 `NavItem` 持有一个页面 ViewModel，`MainWindow.xaml` 用 `DataTemplate` 按类型匹配 View：

| 导航项 | ViewModel | 注入依赖 | View |
|--------|-----------|----------|------|
| 实时监控 | MonitorViewModel | Config, Alarm, Control | MonitorView |
| 趋势曲线 | TrendViewModel | HistoryStore, Config | TrendView |
| 历史数据 | HistoryViewModel | HistoryStore, Config | HistoryView |
| 报警记录 | AlarmViewModel | Alarm | AlarmView |
| 设备控制 | ControlViewModel | Control, Config | ControlView |
| 参数配置 | ConfigViewModel | —（读写 config.json / settings.json） | ConfigView |
| 通信设置 | CommViewModel | Polling, HistorySampler | CommView |

### 线程模型

| 线程 / 定时器 | 职责 | 跨线程处理 |
|---------------|------|-----------|
| UI 线程 | 界面渲染、报警集合操作 | — |
| `PollingService.pollLoop` | 串口读取与模块事务 | 报警切回 UI 线程；控制留在后台线程 |
| `SerialPortService.ReadLoop` | 串口字节读取 | 推入缓冲区，由轮询线程消费 |
| `historySampler` | 每秒采样 | `ObservablePoint` 由 LiveCharts 处理；标量属性由 WPF 自动 marshal |
| `MainWindowViewModel.summaryTimer` | 每 1s 统计在线测点数 | `DispatcherTimer`，运行在 UI 线程 |
| `MonitorViewModel.freshnessTimer` | 每 500ms 刷新卡片在线态 | `DispatcherTimer`，运行在 UI 线程 |

### 生命周期

- `MonitorViewModel` / `TrendViewModel` / `HistoryViewModel` / `CommViewModel` 实现 `IDisposable`，退订全局事件、停止定时器。
- `MainWindowViewModel.Dispose()` 级联释放 `NavItems` 中的页面 VM。
- `App.OnExit` 依次释放 VM、停止轮询、关闭串口、关闭日志。

## 9. 配置编辑

参数配置页（`ConfigViewModel`）**构造时即从磁盘加载**，持有独立的编辑集合 `ObservableCollection<SensorEditModel>` / `ObservableCollection<DeviceEditModel>`，与运行中的全局 `config` 完全隔离。语义为「保存 = 落盘，重启后生效」。

- **编辑模型**：`ViewModels/Editing/` 下的模型继承 `ObservableValidator`。
  - 字段级规则：DataAnnotations（`[Required]` / `[Range]` / `[MaxLength]`）。
  - 跨行规则：测点重名、同从站寄存器冲突、上限 ≤ 下限、设备重名、同从站写地址冲突、迟滞方向错误、关联测点不存在。由 `ValidateAll()` 写入 `ExternalError`，列表行显示 ⚠，悬停查看原因。
- **保存流程**：全量校验 → `SystemConfig.Save()`：写 `config.json.tmp` → `File.Replace` 原子替换 → 原文件保留为 `config.json.bak`。校验不通过时定位到第一条错误且不落盘。
- **加载容错**：`LoadOrCreate()` 解析失败时先把损坏文件改名为 `config.json.corrupt-yyyyMMdd_HHmmss`，再生成示例配置。
- **未保存保护**：`IsDirty` 由集合变更与编辑模型属性变更驱动；关闭主窗口时 `MainWindowViewModel.CanClose()` 弹确认。页面 VM 常驻 `NavItems`，切换页面不会丢失编辑内容。
- **生效方式**：保存成功后顶栏启用「立即重启」，执行 `Process.Start(Environment.ProcessPath)` + `Shutdown()`。
- **编辑控件**：分组与关联测点使用「TextBox 编辑 + 只读下拉辅助」。下拉的 `SelectedItem` 绑定临时属性 `GroupPicker` / `SensorPicker`，选中后写入模型，空值忽略。这样避免可编辑 `ComboBox` 的 `Text` 双向绑定在控件重建（切换页面）时把空字符串回写到模型。
- **数据类型**：仅 `UInt16` / `Int16`，每个测点占 1 个寄存器；旧配置中的 `Float32` 由 `ModbusDataTypeConverter` 回退为 `UInt16`。

**为什么不直接修改运行中的 `config.Sensors`**：

1. 直接 `Clear() + AddRange()` 会使新对象脱离引擎（没有采集/报警/控制），被删除的对象仍被引擎引用。
2. `HistorySampler` 后台线程并发遍历同一个 `List`，原地修改存在线程安全问题。
3. 直接编辑运行对象会污染正在运行的状态。

因此配置的统一生效路径是：编辑 → 保存 → 重启，由组合根按新的 `config.json` 重新接线。

## 10. 界面资源与视觉

- **共享资源字典**：设备控制参数提示模板位于 `Resources/DeviceToolTips.xaml`，由 `App.OnStartup` 通过 `Resources.MergedDictionaries.Add(...)` 合并。实时监控页与设备控制页复用同一个 `DeviceToolTipTemplate`。
  - 注意：当前构建环境下 `App.g.cs` 的 `Main()` 不调用 `InitializeComponent()`，`App.xaml` 中的 `Application.Resources` 不会自动加载，因此共享资源统一放在独立 `ResourceDictionary` 并在启动时显式合并。
- **设备提示内容**：类型、关联测点、当前模式、开启值、关闭值、控制规则；数据来自 `Device` 的计算属性 `ModeText` / `LinkedSensorText` / `ControlRuleText`。
- **趋势页**：浅灰页底 + 白色圆角卡片；页头标题与说明；图例置底；坐标轴使用统一灰色与浅色网格；温度/湿度分别使用左右轴。
- **图表字体**：`App.OnStartup` 中通过 `LiveCharts.Configure(...HasGlobalSKTypeface(...))` 指定中文字体，避免图例与轴名显示成方块。
- **窗口尺寸**：主窗口默认 1200×720，最小 1024×640，保证左右分栏表单不被挤压。
- **通用转换器**：`Converters/UiConverters.cs` 提供枚举中文、上下限空值（`double.MaxValue` / `MinValue`）与“未选中禁用”转换。

## 11. 日志与异常处理

- **日志文件**：exe 同目录 `logs\yyyyMMdd.log`，记录程序启动/退出、串口故障、链路断开/恢复、控制写失败、未处理异常。
- **刷盘策略**：`LogWriter` 按批次刷盘；启动记录与错误日志立即刷盘，避免异常退出时丢失。
- **全局异常兜底**：
  - `DispatcherUnhandledException`：记录完整异常，标记 `Handled = true`，首次弹一次提示框，界面继续运行。
  - `TaskScheduler.UnobservedTaskException`：记录并 `SetObserved()`。
  - `AppDomain.UnhandledException`：记录后立即释放日志，尽量保留现场。

## 12. 可扩展点与已知限制

### 可扩展点

- **接口抽象**：`IPollingService` / `ISerialPortService`（含 `connectionLost` / `connectionRestored` 事件），可替换为模拟实现用于测试。
- **自定义异常**：`Services/MeException/` 下的 `ModbusException`（携带功能码与异常码）与 `SerialPortFailureException`（串口打开 / 写失败 / 端口失效）。
- **串口枚举**：`SerialPortUtil.GetPorts()` 优先用 WMI 获取带描述的串口，失败时回退 `SerialPort.GetPortNames()`。
- **工程化**：`.editorconfig` 约定 UTF-8 BOM、4 空格；`.github/workflows/build.yml` 做 push/PR 编译检查，`release.yml` 负责 `v*` 标签的单文件发布。

### 已知限制

- **配置需重启生效**：`PollingService` 的 `PollItem` 映射、`AlarmEngine` 的报警状态、`ControlEngine` 的设备列表、`HistorySampler` 的测点引用均为启动时接线；页面提供「立即重启」入口，未实现热应用。
- **通信故障报警未启用**：`AlarmType.Communication` 为预留类型；采集层在通信失败时只把测点置离线，不生成报警记录。
- **离线不会自动清除已触发的越限报警**：报警检查由测点值更新驱动；测点先越限后掉线时，该条活跃报警可能保持到重新收到数据或人工确认。
- **无单元测试项目**：协议层（CRC / 帧构造解析）、迟滞逻辑、配置校验目前依赖手工验证。
- `HexUtil` / `ModbusTiming` 为通用工具类，主流程暂未使用。
