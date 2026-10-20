using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TempHumiditySystem.Models;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.ViewModels.Editing;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 参数配置页：编辑测点/设备并保存到 config.json。
    /// 页面持有独立的编辑集合（与运行中的 <see cref="SystemConfig"/> 完全隔离），
    /// 语义为「保存 = 落盘，重启后生效」。
    /// </summary>
    public partial class ConfigViewModel : ObservableObject
    {
        /// <summary>加载/批量重置期间置 true，避免把程序自己填充数据当成"用户修改"。</summary>
        private bool _suppressDirty;

        /// <summary>测点编辑集合。</summary>
        public ObservableCollection<SensorEditModel> Sensors { get; } = new();

        /// <summary>设备编辑集合。</summary>
        public ObservableCollection<DeviceEditModel> Devices { get; } = new();

        /// <summary>设备「关联测点」下拉数据源。</summary>
        public ObservableCollection<string> SensorNames { get; } = new();

        /// <summary>测点「分组」下拉数据源。</summary>
        public ObservableCollection<string> SensorGroups { get; } = new();

        /// <summary>
        /// 测点「单位」下拉数据源：预设单位 + 配置里已出现的自定义单位。
        /// 只追加不清空——清空/重建会让绑定的 ComboBox 把 SelectedItem 的 null 回写进 <see cref="SensorEditModel.Unit"/>。
        /// </summary>
        public ObservableCollection<string> Units { get; } = new();

        /// <summary>预设单位（温度 + 湿度两类测点常用的写法）。</summary>
        private static readonly string[] PresetUnits = { "℃", "°F", "K", "%RH", "%", "g/m³" };

        [ObservableProperty] private SensorEditModel? _selectedSensor;
        [ObservableProperty] private DeviceEditModel? _selectedDevice;

        /// <summary>
        /// 分组快捷选择：只作为下拉框的选中项，不直接绑定 <see cref="SensorEditModel.GroupName"/>。
        /// 可编辑 ComboBox 的 Text 双向绑定在控件重建（切页）时会把空值回写到源，
        /// 导致"第一行分组被清空"，因此改为「TextBox 编辑 + 只读下拉辅助」。
        /// </summary>
        [ObservableProperty] private string? _groupPicker;

        /// <summary>关联测点快捷选择（同 <see cref="GroupPicker"/>，避免同一类回写问题）。</summary>
        [ObservableProperty] private string? _sensorPicker;

        /// <summary>单位下拉的选中项：与 <see cref="SensorEditModel.Unit"/> 同步，但不直接双向绑定模型字段。</summary>
        [ObservableProperty] private string? _unitPicker;

        partial void OnUnitPickerChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && SelectedSensor != null)
                SelectedSensor.Unit = value;
        }

        partial void OnGroupPickerChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && SelectedSensor != null)
                SelectedSensor.GroupName = value;
        }

        partial void OnSensorPickerChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && SelectedDevice != null)
                SelectedDevice.LinkedSensorName = value;
        }

        // 切换选中记录时清空下拉的选中项，避免上一台设备/测点的选择残留
        partial void OnSelectedSensorChanged(SensorEditModel? value)
        {
            GroupPicker = null;

            // 切行时把该测点当前的单位同步到下拉（先补齐下拉项，历史配置里的自定义单位才不会丢选中项）
            EnsureUnits();
            UnitPicker = value?.Unit;
        }

        partial void OnSelectedDeviceChanged(DeviceEditModel? value) => SensorPicker = null;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private string _configPath = SystemConfig.ResolvePath();

        /// <summary>是否存在未保存的修改（关闭窗口时据此拦截）。</summary>
        [ObservableProperty] private bool _isDirty;

        /// <summary>保存成功后置 true，界面显示「立即重启」入口。</summary>
        [ObservableProperty] private bool _canRestart;

        // ===== 下拉选项 =====
        public SensorCategory[] Categories { get; } = Enum.GetValues<SensorCategory>();
        public ModbusDataType[] DataTypes { get; } = Enum.GetValues<ModbusDataType>();
        public DeviceType[] DeviceTypes { get; } = Enum.GetValues<DeviceType>();

        public ConfigViewModel()
        {
            Sensors.CollectionChanged += OnSensorsChanged;
            Devices.CollectionChanged += OnDevicesChanged;

            EnsureUnits();

            // 打开页面即加载：既保证"看得见配置"，也避免用户在空表上点"保存"把配置清空
            ReloadCore(showStatus: false);
        }

        // ================= 变更追踪 =================

        private void MarkDirty()
        {
            if (_suppressDirty) return;
            IsDirty = true;
        }

        private void OnSensorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (SensorEditModel item in e.OldItems) item.PropertyChanged -= OnSensorPropertyChanged;
            if (e.NewItems != null)
                foreach (SensorEditModel item in e.NewItems) item.PropertyChanged += OnSensorPropertyChanged;

            RefreshSensorOptions();
            EnsureUnits();
            MarkDirty();
        }

        private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (DeviceEditModel item in e.OldItems) item.PropertyChanged -= OnDevicePropertyChanged;
            if (e.NewItems != null)
                foreach (DeviceEditModel item in e.NewItems) item.PropertyChanged += OnDevicePropertyChanged;

            MarkDirty();
        }

        private void OnSensorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 校验结果本身不算用户修改，否则每次校验都会把页面标脏
            if (IsValidationProperty(e.PropertyName)) return;

            if (e.PropertyName is nameof(SensorEditModel.Name) or nameof(SensorEditModel.GroupName))
                RefreshSensorOptions();

            MarkDirty();
        }

        private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (IsValidationProperty(e.PropertyName)) return;
            MarkDirty();
        }

        /// <summary>校验结果相关的属性（跨行错误、是否有错、错误合并文本）变化不算用户修改。</summary>
        private static bool IsValidationProperty(string? propertyName)
            => propertyName is nameof(EditModelBase.ExternalError)
                or nameof(EditModelBase.HasError)
                or nameof(EditModelBase.ErrorText);

        /// <summary>重建测点名 / 分组下拉数据源（去重、保持顺序）。</summary>
        private void RefreshSensorOptions()
        {
            var names = Sensors.Select(s => s.Name.Trim())
                               .Where(n => n.Length > 0)
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .ToList();
            var groups = Sensors.Select(s => s.GroupName.Trim())
                                .Where(g => g.Length > 0)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

            ReplaceAll(SensorNames, names);
            ReplaceAll(SensorGroups, groups);
        }

        private static void ReplaceAll(ObservableCollection<string> target, IReadOnlyList<string> values)
        {
            if (target.SequenceEqual(values, StringComparer.OrdinalIgnoreCase)) return;

            target.Clear();
            foreach (var value in values) target.Add(value);
        }

        /// <summary>
        /// 补齐单位下拉项：预设单位 + 配置中已使用的自定义单位（历史配置可能是手输的，不能丢）。
        /// 只追加、从不移除，避免 ComboBox 因选中项消失而把 null 回写到 Unit。
        /// </summary>
        private void EnsureUnits()
        {
            foreach (var unit in PresetUnits) AddUnit(unit);
            foreach (var sensor in Sensors) AddUnit(sensor.Unit?.Trim() ?? "");
        }

        private void AddUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return;
            if (Units.Any(u => string.Equals(u, unit, StringComparison.OrdinalIgnoreCase))) return;

            Units.Add(unit);
        }

        // ================= 测点增删 =================

        [RelayCommand]
        private void AddSensor()
        {
            var last = Sensors.LastOrDefault();
            var model = new SensorEditModel
            {
                Name = NextName("新测点", Sensors.Select(s => s.Name)),
                GroupName = string.IsNullOrWhiteSpace(last?.GroupName) ? "新分组" : last!.GroupName,
                SlaveId = last?.SlaveId ?? 1,
                RegisterAddress = (ushort)Math.Min(65535, Sensors.Count == 0 ? 0 : Sensors.Max(s => (int)s.RegisterAddress) + 1),
                Category = SensorCategory.Temperature,
                Unit = "℃",
                Scale = 0.1,
                HighLimit = 35,
                LowLimit = 0,
            };

            Sensors.Add(model);
            SelectedSensor = model;
            StatusText = "已添加测点，保存并重启后生效";
        }

        [RelayCommand]
        private void DeleteSensor()
        {
            if (SelectedSensor == null) return;
            if (!Confirm($"确定删除测点「{SelectedSensor.Name}」吗？")) return;

            int index = Sensors.IndexOf(SelectedSensor);
            Sensors.Remove(SelectedSensor);
            SelectedSensor = Sensors.Count == 0 ? null : Sensors[Math.Clamp(index, 0, Sensors.Count - 1)];
            StatusText = "已删除测点，保存并重启后生效";
        }

        // ================= 设备增删 =================

        [RelayCommand]
        private void AddDevice()
        {
            var model = new DeviceEditModel
            {
                Name = NextName("新设备", Devices.Select(d => d.Name)),
                Type = DeviceType.Heater,
                SlaveId = Sensors.FirstOrDefault()?.SlaveId ?? 1,
                LinkedSensorName = Sensors.FirstOrDefault()?.Name ?? "",
                TurnsOnBelow = true,
                OnAt = 25,
                OffAt = 35,
            };

            Devices.Add(model);
            SelectedDevice = model;
            StatusText = "已添加设备，保存并重启后生效";
        }

        [RelayCommand]
        private void DeleteDevice()
        {
            if (SelectedDevice == null) return;
            if (!Confirm($"确定删除设备「{SelectedDevice.Name}」吗？")) return;

            int index = Devices.IndexOf(SelectedDevice);
            Devices.Remove(SelectedDevice);
            SelectedDevice = Devices.Count == 0 ? null : Devices[Math.Clamp(index, 0, Devices.Count - 1)];
            StatusText = "已删除设备，保存并重启后生效";
        }

        /// <summary>生成不重名的默认名称：新测点 / 新测点2 / 新测点3 …</summary>
        private static string NextName(string baseName, IEnumerable<string> existing)
        {
            var used = new HashSet<string>(existing.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(baseName)) return baseName;

            for (int i = 2; i < 10000; i++)
            {
                var candidate = baseName + i;
                if (!used.Contains(candidate)) return candidate;
            }
            return baseName + Guid.NewGuid().ToString("N")[..4];
        }

        // ================= 加载 / 保存 =================

        [RelayCommand]
        private void Reload()
        {
            if (IsDirty && !Confirm("当前有未保存的修改，重新加载会丢弃这些修改。是否继续？")) return;
            ReloadCore(showStatus: true);
        }

        private void ReloadCore(bool showStatus)
        {
            var config = SystemConfig.LoadOrCreate();
            var path = SystemConfig.ResolvePath();

            _suppressDirty = true;
            try
            {
                // Clear() 触发的是 Reset 事件（OldItems 为空），必须先手动退订，避免事件泄漏
                foreach (var item in Sensors) item.PropertyChanged -= OnSensorPropertyChanged;
                foreach (var item in Devices) item.PropertyChanged -= OnDevicePropertyChanged;

                Sensors.Clear();
                Devices.Clear();

                foreach (var sensor in config.Sensors) Sensors.Add(SensorEditModel.FromModel(sensor));
                foreach (var device in config.Devices) Devices.Add(DeviceEditModel.FromModel(device));

                SelectedSensor = Sensors.FirstOrDefault();
                SelectedDevice = Devices.FirstOrDefault();
            }
            finally
            {
                _suppressDirty = false;
            }

            RefreshSensorOptions();
            EnsureUnits();
            ConfigPath = path;
            IsDirty = false;
            CanRestart = false;

            StatusText = showStatus
                ? $"已从 {path} 重新加载（{Sensors.Count} 个测点 / {Devices.Count} 台设备）"
                : $"已加载 {path}（{Sensors.Count} 个测点 / {Devices.Count} 台设备）";
        }

        [RelayCommand]
        private void Save()
        {
            if (!ValidateAll()) return;

            var snapshot = new SystemConfig();
            foreach (var sensor in Sensors) snapshot.Sensors.Add(sensor.ToModel());
            foreach (var device in Devices) snapshot.Devices.Add(device.ToModel());

            var path = SystemConfig.ResolvePath();
            try
            {
                snapshot.Save(path);   // 内部为「写 .tmp → 原子替换 → 保留 .bak」
                ConfigPath = path;
                IsDirty = false;
                CanRestart = true;
                StatusText = $"已保存到 {path}（重启后生效；上一份已备份为 config.json.bak）";
            }
            catch (Exception ex)
            {
                StatusText = $"保存失败：{ex.Message}";
            }
        }

        // ================= 校验 =================

        /// <summary>全量校验；返回 false 时已把错误写到各编辑模型上，并定位到第一条错误。</summary>
        private bool ValidateAll()
        {
            foreach (var item in Sensors) item.ClearExternalError();
            foreach (var item in Devices) item.ClearExternalError();

            // 1) 字段级校验（DataAnnotations）
            foreach (var item in Sensors) item.ValidateFields();
            foreach (var item in Devices) item.ValidateFields();

            // 2) 跨行校验：测点
            foreach (var group in Sensors.GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var item in group) item.AppendExternalError($"测点名「{item.Name.Trim()}」重复");

            foreach (var group in Sensors.GroupBy(s => (s.SlaveId, s.RegisterAddress)).Where(g => g.Count() > 1))
                foreach (var item in group) item.AppendExternalError($"从站 {item.SlaveId} 的寄存器 {item.RegisterAddress} 与其它测点冲突");

            foreach (var item in Sensors.Where(s => s.HighLimit <= s.LowLimit))
                item.AppendExternalError("报警上限必须大于下限");

            // 3) 跨行校验：设备
            foreach (var group in Devices.GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var item in group) item.AppendExternalError($"设备名「{item.Name.Trim()}」重复");

            foreach (var group in Devices.GroupBy(d => (d.SlaveId, d.WriteAddress)).Where(g => g.Count() > 1))
                foreach (var item in group) item.AppendExternalError($"从站 {item.SlaveId} 的写地址 {item.WriteAddress} 与其它设备冲突");

            var sensorNames = new HashSet<string>(Sensors.Select(s => s.Name.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var item in Devices)
            {
                if (!string.IsNullOrWhiteSpace(item.LinkedSensorName) && !sensorNames.Contains(item.LinkedSensorName.Trim()))
                    item.AppendExternalError($"关联测点「{item.LinkedSensorName.Trim()}」不存在");

                bool hysteresisOk = item.TurnsOnBelow ? item.OnAt < item.OffAt : item.OnAt > item.OffAt;
                if (!hysteresisOk)
                    item.AppendExternalError(item.TurnsOnBelow
                        ? "升温设备需满足：开启值 < 关闭值"
                        : "降温设备需满足：开启值 > 关闭值");
            }

            // 4) 汇总与定位
            int errorCount = Sensors.Count(s => s.HasError) + Devices.Count(d => d.HasError);
            if (errorCount == 0) return true;

            var firstSensor = Sensors.FirstOrDefault(s => s.HasError);
            if (firstSensor != null)
            {
                SelectedSensor = firstSensor;
            }
            else
            {
                var firstDevice = Devices.FirstOrDefault(d => d.HasError);
                if (firstDevice != null) SelectedDevice = firstDevice;
            }

            StatusText = $"有 {errorCount} 项配置有误（标 ⚠ 的行），请修正后再保存";
            return false;
        }

        // ================= 目录 / 重启 =================

        [RelayCommand]
        private void BrowseFolder()
        {
            if (IsDirty && !Confirm("切换目录会重新加载配置，当前未保存的修改将丢弃。是否继续？")) return;

            var dialog = new OpenFolderDialog { Title = "选择配置文件的保存目录" };
            var currentDir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(currentDir) && Directory.Exists(currentDir))
                dialog.InitialDirectory = currentDir;

            if (dialog.ShowDialog() != true) return;
            var dir = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(dir)) return;

            // 记住选择（写 settings.json；失败则仅本次运行内存生效）
            AppSettings.Current.ConfigDirectory = dir;
            bool persisted = AppSettings.Current.Save();
            string tip = persisted ? "" : "（设置未能写入磁盘，下次启动需重新选择）";

            var target = SystemConfig.ResolvePath();
            if (File.Exists(target))
            {
                ReloadCore(showStatus: false);
                StatusText = $"已切换配置目录，并从 {target} 重新加载{tip}";
            }
            else
            {
                ConfigPath = target;
                StatusText = $"已切换配置目录：{dir}{tip}；该目录暂无配置文件，点「保存」将用当前编辑内容生成";
            }
        }

        [RelayCommand]
        private void OpenConfigFolder()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }

        [RelayCommand]
        private void Restart()
        {
            if (IsDirty && !Confirm("还有未保存的修改，重启会丢失这些修改。仍要重启吗？")) return;

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                StatusText = "无法定位程序路径，请手动重启程序";
                return;
            }

            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }

        private static bool Confirm(string message)
            => MessageBox.Show(message, "参数配置", MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;
    }
}
