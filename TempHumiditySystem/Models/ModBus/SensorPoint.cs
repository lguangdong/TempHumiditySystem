using System;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace TempHumiditySystem.Models.ModBus
{
    /// <summary>测点读取的原始数据类型。</summary>
    public enum ModbusDataType
    {
        UInt16,   // 无符号 16 位（1 个寄存器）
        Int16,    // 有符号 16 位（1 个寄存器）
        Float32,  // 32 位浮点（2 个寄存器）
    }

    /// <summary>测点类别。</summary>
    public enum SensorCategory
    {
        Temperature,   // 温度
        Humidity,      // 湿度
    }

    /// <summary>
    /// 一个温湿度测点：对应传感器的一个寄存器。
    /// 上方字段是"静态配置"；CurrentValue / IsOnline 是"运行时状态"（采集层写入）。
    /// </summary>
    public sealed partial class SensorPoint:ObservableObject
    {
        // ===== 静态配置（来自配置，运行期不变） =====
        public string Name { get; set; } = "";
        public byte SlaveId { get; set; } = 1;
        public ushort RegisterAddress { get; set; }
        public ModbusDataType DataType { get; set; } = ModbusDataType.Int16;
        public double Scale { get; set; } = 1.0;                     // 原始值 × Scale = 物理值
        public string Unit { get; set; } = "";
        public SensorCategory Category { get; set; } = SensorCategory.Temperature;
        public double HighLimit { get; set; } = double.MaxValue;     // 报警上限（默认不报警）
        public double LowLimit { get; set; } = double.MinValue;      // 报警下限

        public string GroupName { get; set; } = "";

        // ===== 运行时状态（采集层每次读取后更新，不入配置文件） =====
        [ObservableProperty]
        [property: JsonIgnore]
        public double _currentValue;  // 当前物理值（已换算）

        [ObservableProperty]
        [property: JsonIgnore]
        public bool _isOnline;       // 最近一次读取是否成功

        [ObservableProperty]
        [property: JsonIgnore]
        public bool _isAlarming;  // 是否正在触发报警

        /// <summary>最近一次成功读取的时刻（Environment.TickCount64），用于判断数据是否"新鲜"。</summary>
        [JsonIgnore]
        public long LastUpdateTick { get; set; }

        /// <summary>默认数据新鲜度阈值(ms)：超过该时长没收到成功读取就视为离线。取轮询周期(1000ms)的 3 倍容差。</summary>
        public const long StaleMs = 3000;

        /// <summary>数据是否新鲜：最近一次读取成功且未超过 staleMs（供监控页卡片与汇总栏共用，保证口径一致）。</summary>
        public bool IsFresh(long staleMs = StaleMs)
            => IsOnline && Environment.TickCount64 - LastUpdateTick <= staleMs;

        /// <summary>原始值 → 物理值（乘系数）。</summary>
        public double ToPhysical(ushort raw)
        {
            double value = DataType switch { ModbusDataType.Int16 => (short)raw, _ => raw };
            return value * Scale;
        }

        /// <summary>拷贝静态配置（不含运行时状态 CurrentValue/IsOnline/IsAlarming），供配置编辑页使用。</summary>
        public SensorPoint Clone() => new SensorPoint
        {
            Name = Name,
            SlaveId = SlaveId,
            RegisterAddress = RegisterAddress,
            DataType = DataType,
            Scale = Scale,
            Unit = Unit,
            Category = Category,
            HighLimit = HighLimit,
            LowLimit = LowLimit,
            GroupName = GroupName,
        };
    }
}
