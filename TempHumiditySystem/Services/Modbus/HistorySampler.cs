using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Models.ModBus;

namespace TempHumiditySystem.Services.Modbus
{
    public sealed class HistorySampler:IDisposable
    {
        private readonly HistoryStore _store;
        private readonly IReadOnlyList<SensorPoint> _sensors;
        private int _intervalMs = 5000;

        private Thread _thread;
        private CancellationTokenSource _cts;

        public HistorySampler(HistoryStore store,  IReadOnlyList<SensorPoint> sensors)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
        }

        public void Start()
        {
            if (_thread != null) return;
            _cts = new CancellationTokenSource();
            _thread = new Thread(() => Loop(_cts.Token))
            {
                IsBackground = true,
                Name = "historySampler"
            };
            _thread.Start();

        }

        public void Stop()
        {
            if(_thread == null) return;
            _cts.Cancel();
            _thread.Join();
            _thread = null;
            _cts.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                SampleOnce();
                ct.WaitHandle.WaitOne(_intervalMs);  //可被cancel唤醒，这样就会立即退出
            }
        }

        private void SampleOnce()
        {
            double[] values = new double[_sensors.Count];
            for (int i = 0; i < _sensors.Count; i++)
            {
                var sensor = _sensors[i];
                // 离线/数据过期写 NaN：趋势图会自动断线，导出 CSV 时留空。
                // 否则"从未读到值"会被记成 0，掉线后也会把最后一个陈旧值反复记成一条直线。
                values[i] = sensor.IsFresh() ? sensor.CurrentValue : double.NaN;
            }
            _store.Add(new HistorySample()
            {
                Time = DateTime.Now,
                Values = values,
            });
        }
    }
}
