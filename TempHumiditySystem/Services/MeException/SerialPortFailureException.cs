using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.MeException
{
    public sealed class SerialPortFailureException:Exception
    {
        public SerialPortFailureException(string message): base(message) { }
        public SerialPortFailureException(string message, Exception inner): base(message, inner) { }
    }
}
