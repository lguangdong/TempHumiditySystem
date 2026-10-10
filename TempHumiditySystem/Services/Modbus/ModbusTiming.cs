using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.Modbus
{
    public static class ModbusTiming
    {
        public static double CharTimeMs(int baudRate)
        {
            return 11.0 / baudRate * 1000.0;
        }

        public static double FrameGapMs(int baudRate)
        {
            return baudRate > 19200 ? 1.75 : CharTimeMs(baudRate) * 3.5;
        }

        public static double InterByteGapMs(int baudRate)
        {
            return 1.5 * CharTimeMs(baudRate);
        }
    }
}
