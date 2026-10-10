using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Interface;

namespace TempHumiditySystem.Services.Modbus
{
    //适配器模式，底层使用PollingService
    public sealed class SensorCollectService: IDisposable
    {
        private IPollingService pollingService;
        private Dictionary<PollItem, SensorPoint> map = new();
        public event Action<SensorPoint>? sensorValueUpdated;  //通知报警系统检查

        public SensorCollectService(IPollingService pollingService)
        {
            this.pollingService = pollingService;
            this.pollingService.valueUpdated += OnValueUpdated;
            this.pollingService.pollFailed += OnPollFailed;
        }

        public void AddSensor(SensorPoint s)
        {
            var item = new PollItem()
            {
                slaveId = s.SlaveId,
                funcCode = ModbusFrame.FuncReadHoldingRegs,
                addr = s.RegisterAddress,
                count = 1,
                intervalMs = 1000,
            };
            pollingService.AddPollItem(item);
            map.Add(item, s);
        }

        public void AddSensors(IEnumerable<SensorPoint> s)
        {
            foreach (var item in s)
            {
                AddSensor(item);
            }
        }

        private void OnValueUpdated(PollItem item, object value)
        {
            if (!map.TryGetValue(item, out var sensorPoint)) return;
            var raw = (ushort[])(value);
            if (raw.Length == 0) return;
            var result = raw[0];
            sensorPoint.CurrentValue = sensorPoint.ToPhysical(result);
            sensorPoint.IsOnline = true;
            sensorValueUpdated?.Invoke(sensorPoint);
        }

        private void OnPollFailed(PollItem item, string message)
        {
            if (!map.TryGetValue(item, out var sensorPoint)) return;
            sensorPoint.IsOnline = false;
        }

        public void Dispose()
        {
            pollingService.valueUpdated -= OnValueUpdated;
            pollingService.pollFailed -= OnPollFailed;
            map.Clear();
        }
    }
}
