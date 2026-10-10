using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.SerialAssistant
{
    public sealed class SerialPortInfo
    {
        public string PortName = "";
        public string Description = "";

        public string Display => string.IsNullOrEmpty(Description) || Description == PortName ? PortName : $"{PortName}-{Description}";
    }
}
