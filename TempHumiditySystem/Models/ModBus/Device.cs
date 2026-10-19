using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace TempHumiditySystem.Models.ModBus
{
    /// <summary>设备类型。</summary>
    public enum DeviceType
    {
        [Description("加热器")]
        Heater,         // 升温

        [Description("加湿器")]
        Humidifier,     // 增湿

        [Description("除湿机")]
        Dehumidifier,   // 降湿

        [Description("风机")]
        Fan,            // 通风
    }

    /// <summary>控制模式。</summary>
    public enum ControlMode
    {
        Manual,   // 手动
        Auto,     // 自动（阈值控制）
    }

    /// <summary>
    /// 一个控制设备：通过写单个寄存器（功能码 06）控制开关（0=停，1=开）。
    /// </summary>
    public sealed partial class Device: ObservableObject
    {
        // ===== 静态配置 =====
        public string Name { get; set; } = "";
        public DeviceType Type { get; set; } = DeviceType.Heater;
        public byte SlaveId { get; set; } = 1;
        public ushort WriteAddress { get; set; }      // 写寄存器地址（06 功能码）
        [ObservableProperty]
        [property: JsonIgnore]
        public ControlMode _mode = ControlMode.Auto;   // 运行时状态：当前手动/自动

        // 自动控制参数
        public string LinkedSensorName { get; set; } = "";  // 关联测点名
        public double OnAt { get; set; }    // 达到该值 → 开启
        public double OffAt { get; set; }   // 达到该值 → 关闭

        /// <summary>
        /// 开启方向：true = 低于 OnAt 开启（升温设备，如加热器）；
        /// false = 高于 OnAt 开启（降温/除湿设备，如除湿机）。
        /// </summary>
        public bool TurnsOnBelow { get; set; } = true;

        // ===== 运行时状态 =====
        [ObservableProperty]
        [property: JsonIgnore]
        public bool _isRunning;   // 当前是否运行

        [JsonIgnore]
        public string TypeName => Type switch
        {
            DeviceType.Heater => "加热器",
            DeviceType.Humidifier => "加湿器",
            DeviceType.Dehumidifier => "除湿机",
            DeviceType.Fan => "风机",
            _ => Type.ToString(),
        };

        /// <summary>当前控制模式的中文文案（供界面展示）。</summary>
        [JsonIgnore]
        public string ModeText => Mode == ControlMode.Auto ? "自动" : "手动";

        /// <summary>关联测点的展示文案。</summary>
        [JsonIgnore]
        public string LinkedSensorText => string.IsNullOrWhiteSpace(LinkedSensorName) ? "未关联测点" : LinkedSensorName;

        /// <summary>自动控制规则的可读描述（提示气泡用）。</summary>
        [JsonIgnore]
        public string ControlRuleText => TurnsOnBelow
            ? $"测点值 ≤ {OnAt:0.#} 时开启，≥ {OffAt:0.#} 时关闭"
            : $"测点值 ≥ {OnAt:0.#} 时开启，≤ {OffAt:0.#} 时关闭";

        partial void OnModeChanged(ControlMode value) => OnPropertyChanged(nameof(ModeText));

        /// <summary>拷贝静态配置（不含运行时状态 Mode/IsRunning），供配置编辑页使用。</summary>
        public Device Clone() => new Device
        {
            Name = Name,
            Type = Type,
            SlaveId = SlaveId,
            WriteAddress = WriteAddress,
            LinkedSensorName = LinkedSensorName,
            OnAt = OnAt,
            OffAt = OffAt,
            TurnsOnBelow = TurnsOnBelow,
        };
    }
}
