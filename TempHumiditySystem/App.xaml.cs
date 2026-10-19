using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;
using TempHumiditySystem.Services.SerialAssistant;
using TempHumiditySystem.ViewModels;

namespace TempHumiditySystem
{
    /// <summary>
    /// 应用入口 = 组合根：在这里组装所有全局引擎，接线（订阅），再创建主窗口。
    /// </summary>
    public partial class App : Application
    {
        // 退出时需要显式释放的资源（串口不关会导致下次启动/换程序时端口被占用）
        private SerialPortService? _port;
        private PollingService? _polling;
        private LogWriter? _log;
        private bool _errorDialogShown;   // 异常弹窗只弹一次，避免连环弹窗卡死界面

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 全局异常兜底：先挂事件，保证初始化阶段的异常也能落盘
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            StartLog();

            // ===== 1. 组装全局引擎（每个都是全局唯一实例） =====
            var config = SystemConfig.LoadOrCreate();

            var port = new SerialPortService();                          // 串口
            var client = new ModbusRTUClient(port);                      // Modbus 客户端
            var polling = new PollingService(client, port);              // 轮询
            var collect = new SensorCollectService(polling);             // 采集（适配器）
            var alarm = new AlarmEngine();                               // 报警
            var control = new ControlEngine(client);                     // 控制
            var historyStore = new HistoryStore();                       // 历史存储
            var historySampler = new HistorySampler(historyStore, config.Sensors); // 历史采样

            _port = port;
            _polling = polling;

            // 串口硬件层故障 / 链路断开与恢复：落盘留痕（UI 侧由 CommViewModel 订阅 connectionLost 刷新状态）
            port.ErrorOccurred += msg => WriteLog($"[串口] {msg}", isError: true);
            polling.connectionLost += msg => WriteLog($"[链路] 断开：{msg}", isError: true);
            polling.connectionRestored += () => WriteLog("[链路] 已恢复");
            control.writeFailed += (device, msg) => WriteLog($"[控制] {device.Name} 写入失败：{msg}", isError: true);

            // ===== 2. 配置：注册测点和设备 =====
            collect.AddSensors(config.Sensors);
            control.AddDevices(config.Devices);

            // ===== 3. 接线：值更新 → 报警 + 控制 =====
            collect.sensorValueUpdated += sensor =>
            {
                // 报警引擎操作 ObservableCollection，必须切回 UI 线程
                Dispatcher.InvokeAsync(() => alarm.Check(sensor));
                // 控制引擎写命令（阻塞 IO），留在后台线程（轮询线程）
                control.Check(sensor);
            };

            // ===== 4. 创建主窗口，注入引擎 =====
            var mainViewModel = new MainWindowViewModel(
                config, port, polling, collect, alarm, control, historyStore, historySampler);

            var window = new MainWindow { DataContext = mainViewModel };
            MainWindow = window;
            window.Show();

            // 注意：PollingService / HistorySampler 的 Start 由"通信设置"页在连接后调用
        }

        /// <summary>初始化文件日志（exe 同目录 logs\yyyyMMdd.log）；不可写时静默降级为无日志。</summary>
        private void StartLog()
        {
            _log = new LogWriter();
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                _log.Start(Path.Combine(dir, $"{DateTime.Now:yyyyMMdd}.log"));
                _log.Write("==== 程序启动 ====");
            }
            catch
            {
                // 安装目录只读等情况：日志失败不影响主流程
                _log.Dispose();
                _log = null;
            }
        }

        private void WriteLog(string message, bool isError = false)
        {
            var line = isError ? $"[错误] {message}" : message;
            _log?.Write(line);
            System.Diagnostics.Debug.WriteLine(line);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteLog($"UI 线程未处理异常：{e.Exception}", isError: true);

            // 单个页面异常不应把整套采集系统一起带走
            e.Handled = true;

            if (_errorDialogShown) return;
            _errorDialogShown = true;
            MessageBox.Show(
                $"程序遇到未处理的异常，已记录到日志。\n\n{e.Exception.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteLog($"后台任务未观察异常：{e.Exception}", isError: true);
            e.SetObserved();   // 标记已观察，避免进程被终结
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteLog($"未捕获异常（进程即将结束）：{e.ExceptionObject}", isError: true);
            _log?.Dispose();   // 立即落盘
            _log = null;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            WriteLog("==== 程序退出 ====");
            _polling?.Stop();      // 先停轮询线程，再关串口
            _port?.Dispose();
            _log?.Dispose();
            base.OnExit(e);
        }
    }
}
