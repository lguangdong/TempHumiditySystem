using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Services.SerialAssistant;

namespace TempHumiditySystem.Services.Interface
{
    public interface ISerialPortService:IDisposable
    {
        bool IsOpen { get; }
        void Open(SerialSetting s);
        void Close();

        int Send(byte[] data);

        event Action<byte[]>? DataReceived;
        event Action<string>? ErrorOccurred;
    }
}
