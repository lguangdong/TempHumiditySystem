using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace TempHumiditySystem.Models.ModBus
{
    /// <summary>设备类型。</summary>
    public enum DeviceType
    {
        Heater,         // 加热器（升温）
        Humidifier,     // 加湿器（增湿）
        Dehumidifier,   // 除湿机（降湿）
        Fan,            // 风机（通风）
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
