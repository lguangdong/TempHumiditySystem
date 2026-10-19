using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;
using TempHumiditySystem.Services.SerialAssistant;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：管理导航菜单 + 当前页面 + 顶部/底部汇总栏，
    /// 并持有全局引擎供各页面 VM 共享。
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject, IDisposable
    {
        // ===== 全局引擎（各页面 VM 从这里拿，共享同一套实例） =====
        public SystemConfig Config { get; }
        public SerialPortService Port { get; }
        public PollingService Polling { get; }
        public SensorCollectService Collect { get; }
        public AlarmEngine Alarm { get; }
        public ControlEngine Control { get; }
        public HistoryStore HistoryStore { get; }
        public HistorySampler HistorySampler { get; }

        /// <summary>左侧导航菜单项。</summary>
        public ObservableCollection<NavItem> NavItems { get; } = new();

        /// <summary>当前显示的页面 ViewModel。</summary>
        [ObservableProperty]
        private object? _currentViewModel;

        // ===== 顶部汇总条 =====
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private string _connectionText = "未连接";
        [ObservableProperty] private int _activeAlarmCount;

        // ===== 底部汇总条 =====
        [ObservableProperty] private string _serialConfigText = "串口：COM3 @9600";
        [ObservableProperty] private string _pollIntervalText = "轮询周期：1s";
        [ObservableProperty] private int _onlineSensorCount;

        public string Version { get; } = "v0.1 骨架";

        private readonly CommViewModel _comm;
        private readonly DispatcherTimer _summaryTimer;

        public MainWindowViewModel(
            SystemConfig config,
            SerialPortService port,
            PollingService polling,
            SensorCollectService collect,
            AlarmEngine alarm,
            ControlEngine control,
            HistoryStore historyStore,
            HistorySampler historySampler)
        {
            Config = config;
            Port = port;
            Polling = polling;
            Collect = collect;
            Alarm = alarm;
            Control = control;
            HistoryStore = historyStore;
            HistorySampler = historySampler;

            // 先在构造函数里建好通信页 VM，汇总栏要订阅它的连接状态
            _comm = new CommViewModel(Polling, HistorySampler);
            BuildNavItems();

            // ===== 汇总栏接线 =====
            _comm.PropertyChanged += OnCommPropertyChanged;
            IsConnected = _comm.IsConnected;
            SyncConnectionText();
            SyncSerialConfigText();

            // 报警数：跟随活跃报警集合的变化
            Alarm.ActiveAlarms.CollectionChanged += OnActiveAlarmsChanged;
            ActiveAlarmCount = Alarm.ActiveAlarms.Count;

            // 轮询周期：从采集服务的常量读，避免和实际值脱节
            PollIntervalText = $"轮询周期：{SensorCollectService.PollIntervalMs / 1000.0:0.#}s";

            // 在线测点数：每 1s 用"数据新鲜度"重算，口径与监控页卡片一致
            _summaryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _summaryTimer.Tick += (_, _) => OnlineSensorCount = Config.Sensors.Count(s => s.IsFresh());
            _summaryTimer.Start();
        }

        private void OnCommPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CommViewModel.IsConnected):
                    IsConnected = _comm.IsConnected;
                    SyncConnectionText();
                    break;
                case nameof(CommViewModel.PortName):
                case nameof(CommViewModel.BaudRate):
                    SyncSerialConfigText();
                    break;
            }
        }

        private void SyncConnectionText()
        {
            ConnectionText = IsConnected ? "已连接" : "未连接";
        }

        /// <summary>底栏展示串口参数，与顶栏的连接状态互补，不再重复表达同一状态。</summary>
        private void SyncSerialConfigText()
        {
            SerialConfigText = $"串口：{_comm.PortName} @{_comm.BaudRate}";
        }

        private void OnActiveAlarmsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            => ActiveAlarmCount = Alarm.ActiveAlarms.Count;

        private void BuildNavItems()
        {
            // 占位页面：后续替换成真实业务 VM（构造时注入上面对应的引擎）
            NavItems.Add(new NavItem { Title = "实时监控", PageViewModel = new MonitorViewModel(Config, Alarm, Control) });
            NavItems.Add(new NavItem { Title = "趋势曲线", PageViewModel = new TrendViewModel(HistoryStore, Config) });
            NavItems.Add(new NavItem { Title = "历史数据", PageViewModel = new HistoryViewModel(HistoryStore, Config) });
            NavItems.Add(new NavItem { Title = "报警记录", PageViewModel = new AlarmViewModel(Alarm) });
            NavItems.Add(new NavItem { Title = "设备控制", PageViewModel = new ControlViewModel(Control, Config) });
            NavItems.Add(new NavItem { Title = "参数配置", PageViewModel = new ConfigViewModel() });
            NavItems.Add(new NavItem { Title = "通信设置", PageViewModel = _comm });

            CurrentViewModel = NavItems[0].PageViewModel;
            NavItems[0].IsSelected = true;   // 初始选中第一项，菜单高亮与内容区一致
        }

        /// <summary>切换页面：把选中项的 PageViewModel 赋给 CurrentViewModel，并维护菜单选中态。</summary>
        [RelayCommand]
        private void Navigate(NavItem item)
        {
            if (item == null) return;
            foreach (var nav in NavItems)
                nav.IsSelected = ReferenceEquals(nav, item);
            if (item.PageViewModel != null)
                CurrentViewModel = item.PageViewModel;
        }

        /// <summary>
        /// 关闭窗口前的未保存检查；返回 false 表示用户取消关闭。
        /// 页面 VM 常驻于 NavItems，切换页面不会丢失编辑内容，所以只在退出时拦截。
        /// </summary>
        public bool CanClose()
        {
            var configPage = NavItems.Select(n => n.PageViewModel).OfType<ConfigViewModel>().FirstOrDefault();
            if (configPage is not { IsDirty: true }) return true;

            var result = MessageBox.Show(
                "参数配置页有未保存的修改，退出后这些修改将丢失。仍要退出吗？",
                "未保存的修改",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        public void Dispose()
        {
            _summaryTimer.Stop();
            _comm.PropertyChanged -= OnCommPropertyChanged;
            Alarm.ActiveAlarms.CollectionChanged -= OnActiveAlarmsChanged;

            // 级联释放页面 VM：Monitor 的定时器、Comm/Trend/History 的事件订阅
            foreach (var nav in NavItems)
            {
                if (nav.PageViewModel is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }
}
