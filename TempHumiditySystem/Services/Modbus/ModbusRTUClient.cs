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
    public sealed class ModbusRTUClient:IDisposable
    {
        private readonly ISerialPortService _serialPortService;
        private readonly List<byte> _rBuffer = new();
        private readonly object _rbLock = new();


        private readonly AutoResetEvent _dataSignal = new(false);
        private readonly SemaphoreSlim _questGate = new(1, 1);

        public int timeoutMs { get; set; } = 500;
        public int guardMs { get; set; } = 20;

        public ModbusRTUClient(ISerialPortService serialPortService)
        {
            if(serialPortService == null) throw new ArgumentNullException(nameof(serialPortService));
            _serialPortService = serialPortService;
            _serialPortService.DataReceived += OnDataReceived;
        }

        private void OnDataReceived(byte[] data)
        {
            if (data.Length == 0) return;
            lock(_rbLock)
            {
                _rBuffer.AddRange(data);
            }
            _dataSignal.Set();
        }

        private int ClearRBuffer()
        {
            lock (_rbLock)
            {
                int n = _rBuffer.Count;
                _rBuffer.Clear();
                return n;
            }
        }

        private bool WaitDataLoop(int minBytes, long deadlineMs)
        {
            while(true)
            {
                lock (_rbLock)
                {
                    if (_rBuffer.Count >= minBytes)
                        return true;
                }
                //有没有超时
                int left = (int)(deadlineMs - Environment.TickCount64);
                if (left <= 0) return false;
                //继续等，直到通知或超时
                _dataSignal.WaitOne(Math.Min(left, 20));
            }

        }

        private byte[] TransactCore(byte slaveId, byte[] requestFrame, int exceptedBytes)
        {
            _questGate.Wait();
            try
            {
                if (!_serialPortService.IsOpen) throw new InvalidOperationException("串口未打开");
                ClearRBuffer();
                _dataSignal.Reset();
                _serialPortService.Send(requestFrame);
                long deadline = Environment.TickCount64 + timeoutMs;

                //接收
                //先收满5字节，做初步校验
                if(!WaitDataLoop(5, deadline))
                {
                    Thread.Sleep(guardMs);
                    ClearRBuffer();
                    throw new TimeoutException("发送请求后，接收超时");
                }
                byte[] head;
                lock (_rbLock) head = _rBuffer.GetRange(0, 5).ToArray();
                if (head[0] != slaveId) throw new InvalidOperationException("响应的从站地址不匹配");
                if ((head[1] & 0x7F) != requestFrame[1]) throw new InvalidOperationException("响应的功能码不匹配");
                if ((head[1] & 0x80) != 0) throw new ModbusException(slaveId, (byte)(head[1] & 0x7F), head[2]);

                //一切正常，收完整数据帧
                if(!WaitDataLoop(exceptedBytes, deadline))
                {
                    int get;
                    lock (_rbLock) get = _rBuffer.Count;
                    throw new TimeoutException($"接收完整帧超时，完整：{exceptedBytes}，收到：{get}");
                }
                byte[] frame;
                lock(_rbLock)
                {
                    frame=_rBuffer.GetRange(0, exceptedBytes).ToArray();
                    _rBuffer.RemoveRange(0, exceptedBytes);
                }
                if (!Crc16.Validate(frame)) throw new InvalidOperationException("响应的crc校验失败");
                return frame;
            }
            catch(Exception e)
            {
                throw;
            }
            finally
            {
                _questGate.Release();
            }

        }

        public void Dispose()
        {
            _serialPortService.DataReceived -= OnDataReceived;
            _dataSignal.Dispose();
            _questGate.Dispose();

        }

        public bool[] ReadCoils(byte slaveId, ushort addr, ushort count)
        {
            byte[] frame = ModbusFrame.BuildReadCoils(slaveId, addr, count);
            byte[] response = TransactCore(slaveId, frame, 5 + (count + 7) / 8);
            return ModbusFrame.ParseReadBitsResponse(response, slaveId, ModbusFrame.FuncReadCoils, count);
        }

        public bool[] ReadDiscreteInput(byte slaveId, ushort addr, ushort count)
        {
            byte[] frame = ModbusFrame.BuildReadDiscreteInputs(slaveId, addr, count);
            byte[] response = TransactCore(slaveId, frame, 5 + (count + 7) / 8);
            return ModbusFrame.ParseReadBitsResponse(response, slaveId, ModbusFrame.FuncReadDiscreteInputs, count);
        }

        public ushort[] ReadHoldingRegs(byte slaveId, ushort addr, ushort count)
        {
            byte[] frame = ModbusFrame.BuildReadHoldingRegs(slaveId, addr, count);
            byte[] response = TransactCore(slaveId, frame, 5 + count * 2);
            return ModbusFrame.ParseReadRegsResponse(response, slaveId, ModbusFrame.FuncReadHoldingRegs, count);
        }

        public ushort[] ReadInputRegs(byte slaveId, ushort addr, ushort count)
        {
            byte[] frame = ModbusFrame.BuildReadInputRegs(slaveId, addr, count);
            byte[] response = TransactCore(slaveId, frame, 5 + count * 2);
            return ModbusFrame.ParseReadRegsResponse(response, slaveId, ModbusFrame.FuncReadInputRegs, count);
        }

        public void WriteSigleCoil(byte slaveId, ushort addr, bool on)
        {
            byte[] frame = ModbusFrame.BuildWriteSingleCoil(slaveId, addr, on);
            byte[] response = TransactCore(slaveId, frame, 8);
        }

        public void WriteMultipleCoils(byte slaveId, ushort addr, bool[] values)
        {
            byte[] frame = ModbusFrame.BuildWriteMultipleCoils(slaveId, addr, values);
            byte[] response = TransactCore(slaveId, frame, 8);
        }

        public void WriteSingleReg(byte slaveId, ushort addr, ushort value)
        {
            byte[] request = ModbusFrame.BuildWriteSingleReg(slaveId, addr, value);
            byte[] response = TransactCore(slaveId, request, 8);   // 06 正常响应固定 8 字节
        }

        public void WriteMultipleRegs(byte slaveId, ushort addr, ushort[] values)
        {
            byte[] request = ModbusFrame.BuildWriteMultipleRegs(slaveId, addr, values);
            byte[] response = TransactCore(slaveId, request, 8);
        }

        //广播写，从站地址为0，不用回复
        public void BroadcastWriteSingleReg(ushort addr, ushort value)
        {
            byte[] request = ModbusFrame.BuildWriteSingleReg(0, addr, value);

            // 必须与普通事务共用同一把门：否则广播帧可能插进"请求-应答"之间，打乱整个收发流
            _questGate.Wait();
            try
            {
                if (!_serialPortService.IsOpen) throw new InvalidOperationException("串口未打开");
                ClearRBuffer();
                _serialPortService.Send(request);
                // 广播没有回复，留出帧间隔，避免与下一帧粘连
                Thread.Sleep(guardMs);
            }
            finally
            {
                _questGate.Release();
            }
        }
    }
}
