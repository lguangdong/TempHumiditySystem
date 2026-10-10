using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TempHumiditySystem.Services.Modbus
{
    public static class Crc16
    {

        //预计算的查找表
        private static readonly ushort[] Table=BuildTable();

        private static ushort[] BuildTable()
        {
            var table = new ushort[256];
            for (int i = 0; i < 256; i++)
            {
                ushort crc = (ushort)i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }
            return table;
        }
        //基本计算，便于理解算法
        public static ushort ComputeBitwise(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
            {
                crc ^= b;                       // 低 8 位异或
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)    // 看最低位
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        public static ushort Compute(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
                crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
            return crc;
        }

        public static bool Validate(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < 4) return false;
            ushort excepted = (ushort)(frame[^2] | (frame[^1] << 8));
            return excepted == Compute(frame[..^2]);
        }
    }
}
