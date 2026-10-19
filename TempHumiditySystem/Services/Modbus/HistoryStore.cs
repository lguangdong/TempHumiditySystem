using System;
using System.Collections.Generic;

namespace TempHumiditySystem.Services.Modbus
{
    /// <summary>
    /// 历史采样存储：环形队列，保留最近 <see cref="MaxSamples"/> 条（24h × 每 5s）。
    /// 采样线程写、UI 线程读，所有集合访问都用锁保护；事件在锁外触发，
    /// 避免订阅者（趋势图等）的回调造成死锁。
    /// </summary>
    public sealed class HistoryStore
    {
        private readonly object _sync = new();
        private readonly Queue<HistorySample> _historySamples = new();
        private const int MaxSamples = 17280; //24h 每5s采样一次

        public event Action<HistorySample>? historySampleAdded;

        public void Add(HistorySample sample)
        {
            lock (_sync)
            {
                _historySamples.Enqueue(sample);
                while (_historySamples.Count > MaxSamples)
                {
                    _historySamples.Dequeue();
                }
            }

            // 事件放到锁外：订阅者可能做耗时操作，不应阻塞采样写入
            historySampleAdded?.Invoke(sample);
        }

        /// <summary>取全部数据的快照（已复制，可安全地在其它线程枚举）。</summary>
        public IReadOnlyList<HistorySample> GetAll()
        {
            lock (_sync)
            {
                return _historySamples.ToArray();
            }
        }

        public int Count
        {
            get
            {
                lock (_sync) return _historySamples.Count;
            }
        }
    }
}
