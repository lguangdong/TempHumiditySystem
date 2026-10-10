using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.Modbus
{
    //用于轮询读请求的item
    public sealed class PollItem
    {
        public byte slaveId { get; set; }
        public byte funcCode { get; set; }  //只读功能码
        public ushort addr { get; set; }
        public ushort count { get; set; }
        public int intervalMs { get; set; }


        public long nextPollTick { get; set; }
    }
}
