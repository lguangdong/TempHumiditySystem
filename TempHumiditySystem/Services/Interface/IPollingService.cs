using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Services.Modbus;
using TempHumiditySystem.Services.SerialAssistant;

namespace TempHumiditySystem.Services.Interface
{
    public interface IPollingService:IDisposable
    {
        public void AddPollItem(PollItem item);
        public void RemovePollItem(PollItem item);
        public void Start(SerialSetting setting);
        public void Stop();

        public event Action<PollItem, object> valueUpdated;
        public event Action<PollItem, string> pollFailed;
        public event Action<string> connectionLost;
        public event Action? connectionRestored;
    }
}
