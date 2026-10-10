using System.Windows;
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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
    }
}
