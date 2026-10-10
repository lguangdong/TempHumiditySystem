using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.Modbus
{
    public sealed class HistorySample
    {
        public DateTime Time { get; set; }
        public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();
    }
}
