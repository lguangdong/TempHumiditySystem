using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TempHumiditySystem.Models;
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

        /// <summary>config.json 当前所在目录（界面展示 + 文件夹选择器初始位置）。</summary>
        [ObservableProperty] private string _configDirectory = SystemConfig.CurrentDirectory;

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

        // ===== 目录选择 =====
        /// <summary>
        /// 选择 config.json 的存放目录并记住该选择（写入 exe 同目录 settings.json）。
        /// 选定后若新目录已存在配置文件则直接加载；否则保留当前编辑内容，由"保存"在新位置生成。
        /// </summary>
        [RelayCommand]
        private void BrowseFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择配置文件的保存目录",
            };
            if (!string.IsNullOrWhiteSpace(ConfigDirectory) && Directory.Exists(ConfigDirectory))
                dialog.InitialDirectory = ConfigDirectory;

            if (dialog.ShowDialog() != true)
                return;

            var dir = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(dir))
                return;

            // 记住选择（写 settings.json；失败则仅本次运行内存生效）
            AppSettings.Current.ConfigDirectory = dir;
            var persisted = AppSettings.Current.Save();

            ConfigDirectory = dir;
            var target = SystemConfig.ResolvePath();
            var tip = persisted ? "" : "（设置未能写入磁盘，下次启动需重新选择）";

            if (File.Exists(target))
            {
                Reload();
                StatusText = $"已切换配置目录，并从 {target} 重新加载{tip}";
            }
            else
            {
                StatusText = $"已切换配置目录：{dir}{tip}；该目录暂无配置文件，点\"保存\"即可生成";
            }
        }

        // ===== 保存 / 重新加载 =====
        [RelayCommand]
        private void Save()
        {
            // 只把编辑副本落盘，不碰运行中的全局配置。
            var snapshot = new SystemConfig();
            snapshot.Sensors.AddRange(Sensors);
            snapshot.Devices.AddRange(Devices);
            var path = SystemConfig.ResolvePath();
            snapshot.Save(path);
            ConfigDirectory = Path.GetDirectoryName(path) ?? ConfigDirectory;
            StatusText = $"已保存到 {path}（重启后生效）";
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

            var path = SystemConfig.ResolvePath();
            ConfigDirectory = Path.GetDirectoryName(path) ?? ConfigDirectory;
            StatusText = $"已从 {path} 重新加载";
        }
    }
}
