using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.Modbus
{
    public sealed class HistoryStore
    {
        private readonly Queue<HistorySample> _historySamples = new();
        private const int MaxSamples = 17280; //24h 没5s采样一次

        public event Action<HistorySample>? historySampleAdded;

        public void Add(HistorySample sample)
        {
            _historySamples.Enqueue(sample);
            while(_historySamples.Count > MaxSamples) {
                _historySamples.Dequeue();
            }
            historySampleAdded?.Invoke(sample);
        }

        public IReadOnlyList<HistorySample> GetAll() => _historySamples.ToArray();
        public int Count => _historySamples.Count;
    }
}
