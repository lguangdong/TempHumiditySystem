using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>一张传感器卡片：一个位置 = 温度测点 + 湿度测点。</summary>
    public sealed class SensorCardViewModel
    {
        public string GroupName { get; }
        public SensorPoint Temperature { get; }
        public SensorPoint Humidity { get; }

        public SensorCardViewModel(string groupName, SensorPoint temperature, SensorPoint humidity)
        {
            GroupName = groupName;
            Temperature = temperature;
            Humidity = humidity;
        }
    }

    /// <summary>
    /// 实时监控页：测点卡片 + 设备状态 + 报警摘要。
    /// </summary>
    public partial class MonitorViewModel : ObservableObject
    {
        public ObservableCollection<SensorCardViewModel> Cards { get; } = new();

        /// <summary>设备列表（只读状态显示）。</summary>
        public IReadOnlyList<Device> Devices { get; }

        /// <summary>活跃报警（共享 AlarmEngine 的集合，报警自动更新）。</summary>
        public ObservableCollection<Alarm> ActiveAlarms { get; }

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
        }
    }
}
