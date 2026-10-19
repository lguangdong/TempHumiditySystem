using System;
using System.IO.Ports;
using System.Threading;
using TempHumiditySystem.Services.Interface;
using TempHumiditySystem.Services.MeException;

namespace TempHumiditySystem.Services.SerialAssistant
{
    /// <summary>
    /// 串口读写服务：一个后台读线程 + 同步写接口。
    /// </summary>
    public sealed class SerialPortService : ISerialPortService
    {
        private const int ReadTimeoutMs = 200;
        private const int WriteTimeoutMs = 2000;
        private const int CloseJoinTimeoutMs = 1500;   // 读线程退出等待上限，必须大于 ReadTimeoutMs

        /// <summary>保护 _port / _readThread / _readCts / _isOpen 的状态切换。</summary>
        private readonly object _sync = new();

        private SerialPort? _port;                      // 当前连接；null = 未打开
        private Thread? _readThread;
        private CancellationTokenSource? _readCts;
        private volatile bool _isOpen;                  

        public bool IsOpen => _isOpen;

        public event Action<byte[]>? DataReceived;
        public event Action<string>? ErrorOccurred;

        public void Open(SerialSetting s)
        {
            SerialPortFailureException? failure = null;
            string errorMessage = "";

            lock (_sync)
            {
                if (_isOpen) return;

                // 上一次连接的读线程还没退干净（例如刚故障退出）：拒绝重开，避免两个读线程并存
                if (_readThread is { IsAlive: true })
                    throw new SerialPortFailureException("上一次连接尚未完全关闭，请稍后重试");

                var port = new SerialPort(s.PortName, s.BaudRate, s.Parity, s.DataBits, s.StopBits)
                {
                    Handshake = s.Handshake,
                    ReadTimeout = ReadTimeoutMs,
                    WriteTimeout = WriteTimeoutMs,
                    ReadBufferSize = 8192,
                };

                try
                {
                    port.Open();
                    port.DiscardInBuffer();
                }
                catch (Exception ex)
                {
                    errorMessage = $"串口 {s.PortName} 打开失败：{ex.Message}";
                    failure = new SerialPortFailureException(errorMessage, ex);
                    SafeDispose(port);
                }

                if (failure == null)
                {
                    var cts = new CancellationTokenSource();
                    var thread = new Thread(() => ReadLoop(port, cts.Token))
                    {
                        IsBackground = true,
                        Name = $"SerialPortService.ReadLoop({s.PortName})",
                    };

                    // 先发布状态，再启动线程，避免线程一上来就读到 null
                    _port = port;
                    _readCts = cts;
                    _readThread = thread;
                    _isOpen = true;
                    thread.Start();
                }
            }

            // 事件与异常都放到锁外触发，避免订阅者回调造成死锁
            if (failure != null)
            {
                RaiseError(errorMessage);
                throw failure;
            }
        }

        /// <summary>
        /// 读循环。port 是本次连接的局部引用，Close / 重连都不会影响本线程正在使用的实例。
        /// </summary>
        private void ReadLoop(SerialPort port, CancellationToken ct)
        {
            var buffer = new byte[4096];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n;
                    try
                    {
                        n = port.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        continue;   // 正常读超时：回到循环顶部检查取消标志
                    }

                    if (n <= 0 || ct.IsCancellationRequested) continue;
                    DataReceived?.Invoke(buffer.AsSpan(0, n).ToArray());
                }
            }
            catch (Exception ex)
            {
                // 主动关闭（ct 已取消）引起的异常属于正常路径，静默退出
                if (ct.IsCancellationRequested) return;
                OnReadLoopFaulted(port, ex);
            }
        }

        /// <summary>
        /// 读线程因硬件故障（拔线、驱动异常等）退出：把本连接标记失效并释放端口。
        /// 必须释放端口，否则下次 Open 会因句柄未关闭而报"拒绝访问"。
        /// </summary>
        private void OnReadLoopFaulted(SerialPort port, Exception ex)
        {
            lock (_sync)
            {
                // 已被 Close 或新的 Open 接手，忽略这次故障上报
                if (!ReferenceEquals(_port, port)) return;

                _isOpen = false;
                _port = null;
                _readThread = null;
                _readCts?.Dispose();
                _readCts = null;
            }

            SafeDispose(port);
            RaiseError($"串口读线程异常退出：{ex.Message}");
        }

        public int Send(byte[] data)
        {
            if (data.Length == 0) return 0;

            var port = _port;
            if (!_isOpen || port == null)
                throw new SerialPortFailureException("串口未打开，无法发送数据");

            try
            {
                port.Write(data, 0, data.Length);
                return data.Length;
            }
            catch (TimeoutException ex)
            {
                throw new SerialPortFailureException(
                    $"串口写入超时（{WriteTimeoutMs}ms），从站可能未准备好", ex);
            }
            catch (Exception ex)
            {
                // 端口被拔出 / 被其它进程抢占 / 已释放：立即标记链路失效，别让后续请求继续空转
                MarkBroken(port, $"串口写入失败：{ex.Message}");
                throw new SerialPortFailureException($"串口写入失败：{ex.Message}", ex);
            }
        }

        /// <summary>写失败时把链路标记为不可用（不 Join 线程，读线程会自行因读异常退出）。</summary>
        private void MarkBroken(SerialPort port, string reason)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_port, port)) return;
                _isOpen = false;
            }
            RaiseError(reason);
        }

        public void Close()
        {
            Thread? thread;
            SerialPort? port;
            CancellationTokenSource? cts;

            lock (_sync)
            {
                thread = _readThread;
                port = _port;
                cts = _readCts;

                _isOpen = false;        // 让读循环在下一个 200ms 超时点退出
                _readThread = null;
                _port = null;
                _readCts = null;
            }

            if (thread == null && port == null) return;

            cts?.Cancel();
            bool exited = true;
            if (thread != null && thread != Thread.CurrentThread)
            {
                exited = thread.Join(CloseJoinTimeoutMs);
                if (!exited)
                {
                    // 读线程卡在 Read 上（驱动不响应）：强制 Dispose 打断阻塞，再等一次
                    SafeDispose(port);
                    exited = thread.Join(CloseJoinTimeoutMs);
                    if (!exited)
                        RaiseError("串口读线程未能在超时内退出（端口已强制释放）");
                }
            }

            // 线程还活着时不能释放 CTS，否则它访问 ct 会抛 ObjectDisposedException 并导致进程崩溃
            if (exited) cts?.Dispose();
            SafeDispose(port);
        }

        public void Dispose() => Close();

        /// <summary>幂等地关闭并释放端口；任何异常都吞掉（对象可能已释放）。</summary>
        private static void SafeDispose(SerialPort? port)
        {
            if (port == null) return;
            try
            {
                if (port.IsOpen) port.Close();
            }
            catch { /* 关闭失败忽略 */ }
            try { port.Dispose(); } catch { /* 已释放忽略 */ }
        }

        private void RaiseError(string message) => ErrorOccurred?.Invoke(message);
    }
}
