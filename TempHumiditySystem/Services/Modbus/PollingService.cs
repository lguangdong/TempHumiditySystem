using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Services.Interface;
using TempHumiditySystem.Services.MeException;
using TempHumiditySystem.Services.SerialAssistant;

namespace TempHumiditySystem.Services.Modbus
{
    public sealed class PollingService : IPollingService
    {
        private readonly ModbusRTUClient client;
        private readonly ISerialPortService portService;
        private readonly List<PollItem> pollItems = new();
        private readonly object pollItemsLock = new();
        private SerialSetting? serialSetting;

        private Thread? loopThread;
        private CancellationTokenSource? loopCTS;

        /// <summary>保护 Start/Stop 的线程生命周期切换，避免重复启动出两个轮询线程。</summary>
        private readonly object _lifecycleLock = new();

        /// <summary>是否真的有轮询线程在跑（Stop 超时后会保留引用，用 IsAlive 判定更准确）。</summary>
        public bool isRunning => loopThread is { IsAlive: true };

        /// <summary>串口当前是否真实处于打开状态（供上层展示真实链路状态）。</summary>
        public bool IsPortOpen => portService.IsOpen;

        public event Action<PollItem, object>? valueUpdated;
        public event Action<PollItem, string>? pollFailed;

        /// <summary>链路丢失（自动重连失败）时触发，参数为原因。</summary>
        public event Action<string>? connectionLost;

        /// <summary>链路恢复（自动重连成功）时触发。</summary>
        public event Action? connectionRestored;

        //重连
        int reconnectMaxFailCount = 3;
        int reconnectDelayMs = 5000;
        int failCount = 0;

        /// <summary>主动断开中：此时串口层的故障上报不再当作"链路丢失"提示给界面。</summary>
        private volatile bool _stopping;

        public PollingService(ModbusRTUClient client, ISerialPortService portService)
        {
            this.client = client;
            this.portService = portService;
            // 串口硬件层故障（拔线/驱动异常/写失败）立即上报，不必等 3 次轮询超时
            this.portService.ErrorOccurred += OnPortError;
        }

        /// <summary>串口层故障 → 复用链路丢失事件，让通信页立刻把"已连接"改回断开并显示原因。</summary>
        private void OnPortError(string message)
        {
            if (_stopping) return;
            connectionLost?.Invoke(message);
        }

        public void AddPollItem(PollItem item)
        {
            lock (pollItemsLock)
            {
                pollItems.Add(item);
            }
        }

        public void RemovePollItem(PollItem item)
        {
            lock (pollItemsLock)
            {
                pollItems.Remove(item);
            }
        }

        public void Start(SerialSetting setting)
        {
            lock (_lifecycleLock)
            {
                // 上一次连接的轮询线程还没退出（例如 Stop 时 Join 超时）：拒绝重入，
                // 否则会出现两个轮询线程同时读写同一串口
                if (loopThread != null)
                {
                    if (loopThread.IsAlive)
                        throw new SerialPortFailureException("上一次连接的轮询线程尚未退出，请稍后重试");

                    // 线程已退出但引用残留：清理后重开
                    loopCTS?.Dispose();
                    loopThread = null;
                    loopCTS = null;
                }

                serialSetting = setting;
                // 打开失败会抛 SerialPortFailureException，Start 直接中断，不会启动轮询线程
                portService.Open(setting);
                if (!portService.IsOpen)
                    throw new SerialPortFailureException($"串口 {setting.PortName} 未能打开");

                failCount = 0;   // 新连接从 0 开始计失败次数

                var cts = new CancellationTokenSource();
                loopCTS = cts;
                // 捕获 cts 局部变量：避免线程启动瞬间 Stop 把字段置 null 后空引用
                loopThread = new Thread(() => PollLoop(cts.Token))
                {
                    IsBackground = true,
                    Name = "PollingService.pollLoop",
                };
                loopThread.Start();
            }
        }

        public void Stop()
        {
            Thread? thread;
            CancellationTokenSource? cts;

            lock (_lifecycleLock)
            {
                thread = loopThread;
                cts = loopCTS;
                if (thread == null) return;

                _stopping = true;   // 抑制 Close 过程中的故障上报
                cts?.Cancel();
            }

            try
            {
                bool exited = thread.Join(client.timeoutMs + reconnectDelayMs + 500);

                if (exited)
                {
                    lock (_lifecycleLock)
                    {
                        loopThread = null;
                        loopCTS?.Dispose();
                        loopCTS = null;
                    }
                }
                // 未退出时保留 loopThread 引用（isRunning 仍为 true），Start 会拒绝重入，
                // 避免出现两个轮询线程；同时不释放 CTS，防止线程访问已释放对象。

                portService.Close();

                if (!exited)
                    connectionLost?.Invoke("轮询线程未能在超时内退出，串口可能仍被占用");
            }
            finally
            {
                _stopping = false;
            }
        }

        private object ReadItem(PollItem item)
        {
            return item.funcCode switch
            {
                ModbusFrame.FuncReadCoils => client.ReadCoils(item.slaveId, item.addr, item.count),
                ModbusFrame.FuncReadDiscreteInputs => client.ReadDiscreteInput(item.slaveId, item.addr, item.count),
                ModbusFrame.FuncReadInputRegs => client.ReadInputRegs(item.slaveId, item.addr, item.count),
                ModbusFrame.FuncReadHoldingRegs => client.ReadHoldingRegs(item.slaveId, item.addr, item.count),
                _ => throw new NotSupportedException($"轮询读异常功能码，0x{item.funcCode:X2}")
            };
        }

        private void PollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                PollItem[] snapshot;
                lock (pollItemsLock) snapshot = pollItems.ToArray();
                foreach (PollItem item in snapshot)
                {
                    if (ct.IsCancellationRequested) break;
                    //先检查item的轮询周期
                    if (Environment.TickCount64 < item.nextPollTick) continue;
                    item.nextPollTick = Environment.TickCount64 + item.intervalMs;
                    try
                    {
                        object value = ReadItem(item);
                        valueUpdated?.Invoke(item, value);
                        OnSuccess();
                    }
                    catch (TimeoutException ex) { pollFailed?.Invoke(item, ex.Message); OnFail(ct); }
                    catch (ModbusException ex) { pollFailed?.Invoke(item, ex.Message); OnFail(ct); }
                    catch (SerialPortFailureException ex) { pollFailed?.Invoke(item, ex.Message); OnFail(ct); }   // 端口失效同样要重连
                    catch (Exception ex)
                    {
                        pollFailed?.Invoke(item, ex.Message);
                        OnFail(ct);
                    }
                }
                ct.WaitHandle.WaitOne(50);
            }
        }

        private void OnSuccess()
        {
            failCount = 0;
        }

        private void OnFail(CancellationToken ct)
        {
            failCount++;
            if (failCount < reconnectMaxFailCount) return;

            // 先清零：无论后面是否真的尝试重连，都不能让计数停在阈值上
            failCount = 0;
            if (serialSetting == null) return;   // 没有串口参数可重连，交给人工处理

            try
            {
                portService.Close();
                // 用可被 Cancel 唤醒的等待，Stop() 时不必死等满 reconnectDelayMs
                if (ct.WaitHandle.WaitOne(reconnectDelayMs)) return;
                portService.Open(serialSetting);
                connectionRestored?.Invoke();   // 重连成功，通知上层恢复"已连接"
            }
            catch (Exception ex)
            {
                // 重连失败，等下一次重连；通知上层链路已丢失
                connectionLost?.Invoke($"自动重连失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            portService.ErrorOccurred -= OnPortError;
            lock (pollItemsLock) pollItems.Clear();
        }
    }
}
