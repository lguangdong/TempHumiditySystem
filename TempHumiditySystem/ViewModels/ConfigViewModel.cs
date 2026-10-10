using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempHumiditySystem.Models.ModBus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 参数配置页：编辑测点表 + 设备表，保存到 config.json。
    /// 约定：保存后重启程序生效（运行时引擎在启动时按配置接线）。
    /// </summary>
    public partial class ConfigViewModel : ObservableObject
    {
        /// <summary>测点编辑集合。</summary>
        public ObservableCollection<SensorPoint> Sensors { get; }

        /// <summary>设备编辑集合。</summary>
        public ObservableCollection<Device> Devices { get; }

        [ObservableProperty] private string _statusText = "";

        [ObservableProperty] private SensorPoint? _selectedSensor;
        [ObservableProperty] private Device? _selectedDevice;

        // ===== 枚举下拉选项（供 DataGrid 的 ComboBox 列绑定） =====
        public SensorCategory[] Categories { get; } = { SensorCategory.Temperature, SensorCategory.Humidity };
        public ModbusDataType[] DataTypes { get; } = Enum.GetValues<ModbusDataType>();
        public DeviceType[] DeviceTypes { get; } = Enum.GetValues<DeviceType>();
        public ConfigViewModel()
        {
            Sensors = new ObservableCollection<SensorPoint>();
            Devices = new ObservableCollection<Device>();
        }

        // ===== 测点增删 =====
        [RelayCommand]
        private void AddSensor()
        {
            Sensors.Add(new SensorPoint
            {
                Name = "新测点",
                GroupName = "新分组",
                SlaveId = 1,
                DataType = ModbusDataType.Int16,
                Scale = 0.1,
                Unit = "℃",
                Category = SensorCategory.Temperature,
                HighLimit = 30,
                LowLimit = 0,
            });
        }

        [RelayCommand]
        private void DeleteSensor()
        {
            if (SelectedSensor != null)
                Sensors.Remove(SelectedSensor);
        }

        // ===== 设备增删 =====
        [RelayCommand]
        private void AddDevice()
        {
            Devices.Add(new Device { Name = "新设备" });
        }

        [RelayCommand]
        private void DeleteDevice()
        {
            if (SelectedDevice != null)
                Devices.Remove(SelectedDevice);
        }

        // ===== 保存 / 重新加载 =====
        [RelayCommand]
        private void Save()
        {
            // 只把编辑副本落盘，不碰运行中的全局配置。
            var snapshot = new SystemConfig();
            snapshot.Sensors.AddRange(Sensors);
            snapshot.Devices.AddRange(Devices);
            snapshot.Save();
            StatusText = $"已保存到 {SystemConfig.DefaultPath}（重启后生效）";
        }

        [RelayCommand]
        private void Reload()
        {
            var loaded = SystemConfig.LoadOrCreate();

            // 只刷新编辑副本，不写回运行中的全局配置（重启后生效）
            Sensors.Clear();
            foreach (var s in loaded.Sensors) Sensors.Add(s);
            Devices.Clear();
            foreach (var d in loaded.Devices) Devices.Add(d);

            StatusText = "已从配置文件重新加载";
        }
    }
}
