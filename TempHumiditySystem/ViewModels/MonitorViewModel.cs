using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>一张传感器卡片：一个位置 = 温度测点 + 湿度测点（允许只有其中一个，缺的那一路不显示）。</summary>
    public sealed class SensorCardViewModel : ObservableObject
    {
        public string GroupName { get; }
        public SensorPoint? Temperature { get; }
        public SensorPoint? Humidity { get; }

        /// <summary>该分组里是否配了温度 / 湿度测点（缺的一路整块隐藏，而不是让整张卡片消失）。</summary>
        public bool HasTemperature => Temperature != null;
        public bool HasHumidity => Humidity != null;

        /// <summary>温度测点是否在线（最近一次读取成功且数据未过期）。</summary>
        public bool TemperatureOnline => Temperature is { } t && IsFresh(t);

        /// <summary>湿度测点是否在线（最近一次读取成功且数据未过期）。</summary>
        public bool HumidityOnline => Humidity is { } h && IsFresh(h);

        /// <summary>已配置的测点全部在线才算在线；只配了温度就只看温度。</summary>
        public bool IsOnline
        {
            get
            {
                if (Temperature is { } t && !IsFresh(t)) return false;
                if (Humidity is { } h && !IsFresh(h)) return false;
                return HasTemperature || HasHumidity;
            }
        }

        /// <summary>供界面显示的在线文案。</summary>
        public string OnlineText => IsOnline ? "在线" : "离线";

        public SensorCardViewModel(string groupName, SensorPoint? temperature, SensorPoint? humidity)
        {
            GroupName = groupName;
            Temperature = temperature;
            Humidity = humidity;

            // 采集线程会改 IsOnline，任一变化即刷新卡片状态
            if (Temperature != null) Temperature.PropertyChanged += OnSensorPropertyChanged;
            if (Humidity != null) Humidity.PropertyChanged += OnSensorPropertyChanged;
        }

        // 复用 SensorPoint 上的新鲜度判定，保证与底部汇总栏"在线测点数"口径一致
        private static bool IsFresh(SensorPoint s) => s.IsFresh();

        private void OnSensorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SensorPoint.IsOnline))
                Refresh();
        }

        /// <summary>重算并通知在线状态。除事件驱动外，也由定时器周期调用，以处理"长时间无数据"。</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(TemperatureOnline));
            OnPropertyChanged(nameof(HumidityOnline));
            OnPropertyChanged(nameof(IsOnline));
            OnPropertyChanged(nameof(OnlineText));
        }
    }

    /// <summary>
    /// 实时监控页：测点卡片 + 设备状态 + 报警摘要。
    /// </summary>
    public partial class MonitorViewModel : ObservableObject, IDisposable
    {
        public ObservableCollection<SensorCardViewModel> Cards { get; } = new();

        /// <summary>设备列表（只读状态显示）。</summary>
        public IReadOnlyList<Device> Devices { get; }

        /// <summary>活跃报警（共享 AlarmEngine 的集合，报警自动更新）。</summary>
        public ObservableCollection<Alarm> ActiveAlarms { get; }

        private readonly DispatcherTimer _freshnessTimer;

        public MonitorViewModel(SystemConfig config, AlarmEngine alarm, ControlEngine control)
        {
            Devices = config.Devices;
            ActiveAlarms = alarm.ActiveAlarms;

            // 按 GroupName 分组：每个分组一张卡（温度 + 湿度，缺哪路就只显示另一路）。
            // 以前要求"温度、湿度都必须有"才建卡，分组里两类测点配错时整张卡片会静默消失。
            foreach (var g in config.Sensors.GroupBy(s => s.GroupName))
            {
                var temp = g.FirstOrDefault(s => s.Category == SensorCategory.Temperature);
                var hum = g.FirstOrDefault(s => s.Category == SensorCategory.Humidity);
                if (temp == null && hum == null) continue;

                Cards.Add(new SensorCardViewModel(g.Key, temp, hum));
            }

            // 周期重算在线状态：串口断开 / 轮询线程退出时收不到任何事件，
            // 靠"数据新鲜度"超时也能在界面上体现"离线"，避免谎报在线。
            _freshnessTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _freshnessTimer.Tick += (_, _) =>
            {
                foreach (var card in Cards) card.Refresh();
            };
            _freshnessTimer.Start();
        }

        public void Dispose() => _freshnessTimer.Stop();
    }
}
