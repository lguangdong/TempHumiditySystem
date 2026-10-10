using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.SerialAssistant
{
    public sealed class SerialSetting
    {
        public string PortName { get; set; } = "";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public Parity Parity { get; set; } = Parity.None;
        public StopBits StopBits { get; set; } = StopBits.One;
        public Handshake Handshake { get; set; } = Handshake.None;
    }
}
