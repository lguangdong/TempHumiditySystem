using System;
using System.Collections.Generic;
using System.Linq;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Interface;

namespace TempHumiditySystem.Services.Modbus
{
    /// <summary>
    /// 采集适配器：把 <see cref="SensorPoint"/> 注册到轮询服务，并把轮询结果回写到测点。
    /// 同一从站、地址相近的测点会合并成一次批量读，减少串口往返次数。
    /// </summary>
    public sealed class SensorCollectService : IDisposable
    {
        /// <summary>传感器轮询周期(ms)。</summary>
        public const int PollIntervalMs = 1000;

        /// <summary>单次批量读最多的寄存器数量：跨度太大既浪费带宽，也容易触发从站异常。</summary>
        private const int MaxRegistersPerRead = 8;

        private readonly IPollingService pollingService;
        private readonly Dictionary<PollItem, List<PollBinding>> map = new();

        public event Action<SensorPoint>? sensorValueUpdated;  //通知报警系统检查

        public SensorCollectService(IPollingService pollingService)
        {
            this.pollingService = pollingService;
            this.pollingService.valueUpdated += OnValueUpdated;
            this.pollingService.pollFailed += OnPollFailed;
        }

        public void AddSensor(SensorPoint sensor) => AddSensors(new[] { sensor });

        /// <summary>
        /// 批量注册测点：按从站分组，把寄存器地址跨度小于 <see cref="MaxRegistersPerRead"/> 的测点
        /// 合并为一次读（例如 0x0000 温度 + 0x0001 湿度 → 一次读 2 个寄存器）。
        /// </summary>
        public void AddSensors(IEnumerable<SensorPoint> sensors)
        {
            foreach (var group in sensors.GroupBy(s => s.SlaveId))
            {
                var ordered = group.OrderBy(s => s.RegisterAddress).ToList();
                int index = 0;

                while (index < ordered.Count)
                {
                    int start = index;
                    ushort begin = ordered[start].RegisterAddress;
                    ushort end = begin;
                    index++;

                    // 在跨度上限内尽量合并；超出上限则另起一次读
                    while (index < ordered.Count &&
                           ordered[index].RegisterAddress - begin < MaxRegistersPerRead)
                    {
                        end = ordered[index].RegisterAddress;
                        index++;
                    }

                    var item = new PollItem
                    {
                        slaveId = ordered[start].SlaveId,
                        funcCode = ModbusFrame.FuncReadHoldingRegs,
                        addr = begin,
                        count = (ushort)(end - begin + 1),
                        intervalMs = PollIntervalMs,
                    };

                    var bindings = new List<PollBinding>(index - start);
                    for (int i = start; i < index; i++)
                        bindings.Add(new PollBinding(ordered[i].RegisterAddress - begin, ordered[i]));

                    pollingService.AddPollItem(item);
                    map[item] = bindings;
                }
            }
        }

        private void OnValueUpdated(PollItem item, object value)
        {
            if (!map.TryGetValue(item, out var bindings)) return;
            if (value is not ushort[] raw || raw.Length == 0) return;

            foreach (var binding in bindings)
            {
                if (binding.Offset >= raw.Length) continue;

                var sensor = binding.Sensor;
                sensor.CurrentValue = sensor.ToPhysical(raw[binding.Offset]);
                sensor.IsOnline = true;
                sensor.LastUpdateTick = Environment.TickCount64;
                sensorValueUpdated?.Invoke(sensor);
            }
        }

        private void OnPollFailed(PollItem item, string message)
        {
            if (!map.TryGetValue(item, out var bindings)) return;
            foreach (var binding in bindings)
                binding.Sensor.IsOnline = false;
        }

        public void Dispose()
        {
            pollingService.valueUpdated -= OnValueUpdated;
            pollingService.pollFailed -= OnPollFailed;
            map.Clear();
        }

        /// <summary>一个测点在批量读结果中的位置。</summary>
        private readonly struct PollBinding
        {
            public PollBinding(int offset, SensorPoint sensor)
            {
                Offset = offset;
                Sensor = sensor;
            }

            public int Offset { get; }
            public SensorPoint Sensor { get; }
        }
    }
}
