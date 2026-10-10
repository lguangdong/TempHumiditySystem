using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;

namespace TempHumiditySystem.Services.MeException
{
    public sealed class ModbusException : Exception
    {
        private static string Describe(byte code) => code switch
        {
            0x01 => "非法功能码",
            0x02 => "非法数据地址",
            0x03 => "非法数据值",
            0x04 => "从站设备故障",
            0x05 => "确认（从站处理中）",
            0x06 => "从站忙",
            0x0A => "网关路径不可用",
            0x0B => "目标设备无响应",
            _ => "未知错误",
        };

        public byte SlaveId { get;}
        public byte FunctionCode { get;}
        public byte ErrorCode { get;}

        public ModbusException(byte slaveId, byte functionCode, byte errorCode):
            base($"从站{slaveId} 功能码{functionCode:X2} 异常：{Describe(errorCode)}(错误码0x{errorCode:X2}") { 
            SlaveId = slaveId;
            FunctionCode = functionCode;
            ErrorCode = errorCode;
        }
    }
}
