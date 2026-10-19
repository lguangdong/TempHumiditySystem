using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.SerialAssistant
{
    public sealed class LogWriter: IDisposable
    {
        private readonly object _lock = new object();
        private StreamWriter? _writer;
        private int _pending = 0;
    
        public void Start(string path)
        {
            lock(_lock)
            {
                _writer?.Dispose();
                _writer = new StreamWriter(path, append: true, Encoding.UTF8)
                {
                    AutoFlush = false,
                };
                _pending = 0;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }

        public void Write(string message) {
            lock (_lock)
            {
                if (_writer == null) return;
                _writer.WriteLine($"[{DateTime.Now:F}] {message}");
                _pending++;
                if (_pending >= 20)
                {
                    _writer.Flush();
                    _pending = 0;
                }
            }
        }

        /// <summary>立即把缓冲写入磁盘（崩溃兜底/退出前调用）。</summary>
        public void Flush()
        {
            lock (_lock)
            {
                _writer?.Flush();
                _pending = 0;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
