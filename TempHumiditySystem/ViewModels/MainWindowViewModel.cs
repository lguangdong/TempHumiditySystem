using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;
using TempHumiditySystem.Services.SerialAssistant;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：管理导航菜单 + 当前页面，持有全局引擎供各页面 VM 共享。
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
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

            BuildNavItems();
        }

        private void BuildNavItems()
        {
            // 占位页面：后续替换成真实业务 VM（构造时注入上面对应的引擎）
            NavItems.Add(new NavItem { Title = "实时监控", PageViewModel = new MonitorViewModel(Config, Alarm, Control) });
            NavItems.Add(new NavItem { Title = "趋势曲线", PageViewModel = new TrendViewModel(HistoryStore, Config) });
            NavItems.Add(new NavItem { Title = "历史数据", PageViewModel = new HistoryViewModel(HistoryStore, Config) });
            NavItems.Add(new NavItem { Title = "报警记录", PageViewModel = new AlarmViewModel(Alarm) });
            NavItems.Add(new NavItem { Title = "设备控制", PageViewModel = new ControlViewModel(Control, Config) });
            NavItems.Add(new NavItem { Title = "参数配置", PageViewModel = new ConfigViewModel() });
            NavItems.Add(new NavItem { Title = "通信设置", PageViewModel = new CommViewModel(Polling, HistorySampler) });

            CurrentViewModel = NavItems[0].PageViewModel;
        }

        /// <summary>切换页面：把选中项的 PageViewModel 赋给 CurrentViewModel。</summary>
        [RelayCommand]
        private void Navigate(NavItem item)
        {
            if (item?.PageViewModel != null)
                CurrentViewModel = item.PageViewModel;
        }
    }
}
