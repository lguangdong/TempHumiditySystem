using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using TempHumiditySystem.Services.Interface;

namespace TempHumiditySystem.Services.SerialAssistant
{
    public sealed class SerialPortService : ISerialPortService
    {
        private const int ReadTimeoutMs = 200;
        private SerialPort? _port = null;
        private Thread? _readLoopThread = null;
        public bool IsOpen => _port?.IsOpen == true;

        public event Action<byte[]>? DataReceived = null;
        public event Action<string>? ErrorOccurred = null;

        public void Open(SerialSetting s)
        {
            if (IsOpen) return;
            _port = new SerialPort(s.PortName, s.BaudRate, s.Parity, s.DataBits, s.StopBits)
            {
                Handshake = s.Handshake,
                ReadTimeout = ReadTimeoutMs,
                WriteTimeout = 2000,
                ReadBufferSize = 8192,
            };
            try
            {
                _port.Open();
                _port.DiscardInBuffer();
            }
            catch (Exception ex)
            {

                ErrorOccurred?.Invoke(ex.Message);
                return;
            }
       

            _readLoopThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "SerialPortService.ReadLoop",
            };
            _readLoopThread.Start();
        }

        private void ReadLoop()
        {
            var buffer = new byte[4096];
            while (true)
            {
                try
                {
                    int n = _port!.Read(buffer, 0, buffer.Length);
                    if (n > 0)
                    {
                        DataReceived?.Invoke(buffer.AsSpan(0, n).ToArray());
                    }
                }
                catch (TimeoutException) { } //正常超时
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(ex.Message);
                    break;
                }
 
            }
        }

        public int Send(byte[] data)
        {
            if(!IsOpen) return 0;
            _port!.Write(data, 0, data.Length);
            return data.Length;
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (IsOpen) _port?.Close();
            if(!_readLoopThread!.Join(ReadTimeoutMs+100))
            {
                //读线程未正常退出，需要想办法处理
                ErrorOccurred?.Invoke("读线程未正常退出");
            }
            _readLoopThread = null;
        }
        public void Dispose()
        {
            Close();
            _port?.Dispose();
            _port = null;
        }
    }
}
