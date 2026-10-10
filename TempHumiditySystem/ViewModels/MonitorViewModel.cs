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
    /// <summary>一张传感器卡片：一个位置 = 温度测点 + 湿度测点。</summary>
    public sealed class SensorCardViewModel : ObservableObject
    {
        public string GroupName { get; }
        public SensorPoint Temperature { get; }
        public SensorPoint Humidity { get; }

        /// <summary>温度测点是否在线（最近一次读取成功且数据未过期）。</summary>
        public bool TemperatureOnline => IsFresh(Temperature);

        /// <summary>湿度测点是否在线（最近一次读取成功且数据未过期）。</summary>
        public bool HumidityOnline => IsFresh(Humidity);

        /// <summary>温度、湿度都在线，才认为该从站已就绪、能接收数据。</summary>
        public bool IsOnline => TemperatureOnline && HumidityOnline;

        /// <summary>供界面显示的在线文案。</summary>
        public string OnlineText => IsOnline ? "在线" : "离线";

        public SensorCardViewModel(string groupName, SensorPoint temperature, SensorPoint humidity)
        {
            GroupName = groupName;
            Temperature = temperature;
            Humidity = humidity;

            // 采集线程会改 IsOnline，任一变化即刷新卡片状态
            Temperature.PropertyChanged += OnSensorPropertyChanged;
            Humidity.PropertyChanged += OnSensorPropertyChanged;
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

            // 按 GroupName 分组：每个传感器一张卡（温度 + 湿度）
            foreach (var g in config.Sensors.GroupBy(s => s.GroupName))
            {
                var temp = g.FirstOrDefault(s => s.Category == SensorCategory.Temperature);
                var hum = g.FirstOrDefault(s => s.Category == SensorCategory.Humidity);
                if (temp != null && hum != null)
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
