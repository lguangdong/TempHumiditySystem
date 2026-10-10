using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TempHumiditySystem.Models.ModBus
{
    /// <summary>报警类型。</summary>
    public enum AlarmType
    {
        HighLimit,      // 超上限
        LowLimit,       // 超下限
        Communication,  // 通信故障
    }

    /// <summary>报警级别。</summary>
    public enum AlarmLevel
    {
        Info,       // 一般
        Warning,    // 重要
        Critical,   // 紧急
    }

    /// <summary>报警状态。</summary>
    public enum AlarmStatus
    {
        Active,        // 活跃（未确认）
        Acknowledged,  // 已确认
    }

    /// <summary>一条报警记录。</summary>
    public sealed partial class Alarm:ObservableObject
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Source { get; set; } = "";   // 来源（测点名 / 设备名）
        public AlarmType Type { get; set; }
        public AlarmLevel Level { get; set; } = AlarmLevel.Info;
        [ObservableProperty] private AlarmStatus _status = AlarmStatus.Active;
        public double Value { get; set; }          // 触发时的值
        public double Limit { get; set; }          // 阈值

        public override string ToString()
        {
            string type = Type switch
            {
                AlarmType.HighLimit => "超上限",
                AlarmType.LowLimit => "超下限",
                AlarmType.Communication => "通信故障",
                _ => Type.ToString(),
            };
            return $"{Time:HH:mm:ss} {Source} {type}（当前 {Value}，阈值 {Limit}）";
        }
    }
}
