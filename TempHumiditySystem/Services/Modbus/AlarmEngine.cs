using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Models.ModBus;

namespace TempHumiditySystem.Services.Modbus
{
    public sealed class AlarmEngine
    {
        /// <summary>历史报警保留上限：长期运行时不至于无限增长。</summary>
        public const int MaxHistoryAlarms = 500;

        private readonly Dictionary<SensorPoint, bool> _alarmState = new();
        public ObservableCollection<Alarm> ActiveAlarms { get; } = new();
        public ObservableCollection<Alarm> HistoryAlarms { get; } = new();

        public event Action<Alarm>? alarmNotify;
        public event Action<Alarm>? alarmCleared;

        public void Check(SensorPoint sensor)
        {
            bool oldS = _alarmState.TryGetValue(sensor, out var value) && value;
            bool newS = IsOutOfRange(sensor);
            if(oldS && !newS)
            {
                //从异常中恢复
                sensor.IsAlarming = false;
                //从获取列表到历史列表
                var alarm = ActiveAlarms.FirstOrDefault(s => s.Source == sensor.Name);
                if (alarm != null)
                {
                    ActiveAlarms.Remove(alarm);
                    HistoryAlarms.Add(alarm);
                    while (HistoryAlarms.Count > MaxHistoryAlarms)
                        HistoryAlarms.RemoveAt(0);
                    alarmCleared?.Invoke(alarm);
                }
            } else if(!oldS && newS)
            {
                //触发报警
                sensor.IsAlarming = true;
                var alarm = CreateAlarm(sensor);
                ActiveAlarms.Add(alarm);
                alarmNotify?.Invoke(alarm);
            }
            _alarmState[sensor] = newS;
        }

        private Alarm CreateAlarm(SensorPoint s)
        {
            bool isUp = s.CurrentValue > s.HighLimit;
            return new Alarm()
            {
                Source = s.Name,
                Time = DateTime.Now,
                Type = isUp ? AlarmType.HighLimit : AlarmType.LowLimit,
                Level = AlarmLevel.Warning,
                Status = AlarmStatus.Active,
                Value = s.CurrentValue,
                Limit = isUp ? s.HighLimit : s.LowLimit,
            };
        }

        private bool IsOutOfRange(SensorPoint s)
        {
            if (!s.IsOnline) return false;
            return s.CurrentValue > s.HighLimit || s.CurrentValue < s.LowLimit;
        }

        public void Acknowledge(Alarm a)
        {
            if (a.Status == AlarmStatus.Active)
            {
                a.Status = AlarmStatus.Acknowledged;
            }
        }

        public void AcknowledgeAll()
        {
            foreach (var item in ActiveAlarms)
            {
                Acknowledge(item);
            }
        }
    }
}
