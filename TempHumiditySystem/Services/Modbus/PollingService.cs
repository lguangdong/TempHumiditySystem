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

        public bool isRunning => loopThread != null;

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

        public PollingService(ModbusRTUClient client, ISerialPortService portService)
        {
            this.client = client;
            this.portService = portService;
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
            if (isRunning) return;
            serialSetting = setting;
            // 打开失败会抛 SerialPortFailureException，Start 直接中断，不会启动轮询线程
            portService.Open(setting);
            if (!portService.IsOpen)
                throw new SerialPortFailureException($"串口 {setting.PortName} 未能打开");

            loopCTS = new CancellationTokenSource();
            loopThread = new Thread(() =>
            {
                PollLoop(loopCTS.Token);
            })
            { IsBackground = true, Name = "PollingService.pollLoop" };
            loopThread.Start();
        }

        public void Stop()
        {
            if (!isRunning) return;
            loopCTS!.Cancel();
            loopThread!.Join(client.timeoutMs + reconnectDelayMs + 500);
            loopThread = null;
            loopCTS.Dispose();
            loopCTS = null;
            portService.Close();
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
                    catch (SerialPortFailureException ex) { pollFailed?.Invoke(item, ex.Message); }  //不需要重连
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
            if(failCount >= reconnectMaxFailCount)
            {
                if (serialSetting == null) return;
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
                failCount = 0;
            }
        }

        public void Dispose()
        {
            Stop();
            lock (pollItemsLock) pollItems.Clear();
        }
    }
}
