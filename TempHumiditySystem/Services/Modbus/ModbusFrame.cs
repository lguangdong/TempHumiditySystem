using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Services.MeException;

namespace TempHumiditySystem.Services.Modbus
{
    //数据帧的构造和解析
    public static class ModbusFrame
    {

        //功能码
        public const byte FuncReadCoils = 0x01;
        public const byte FuncReadDiscreteInputs = 0x02;
        public const byte FuncReadHoldingRegs = 0x03;
        public const byte FuncReadInputRegs = 0x04;
        public const byte FuncWriteSingleCoil = 0x05;
        public const byte FuncWriteSingleReg = 0x06;
        public const byte FuncWriteMultipleCoils = 0x0F;
        public const byte FuncWriteMultipleRegs = 0x10;

        public const int MaxReadCoils = 2000;
        public const int MaxReadRegs = 125;
        public const int MaxWriteMultipleCoils = 1968;
        public const int MaxWriteMultipleRegs = 123;

        private static byte[] AppendCrc(ReadOnlySpan<byte> payload)
        {
            var crc = Crc16.Compute(payload);
            var frame = new byte[payload.Length + 2];
            payload.CopyTo(frame);
            frame[^2] = (byte)(crc & 0xFF);
            frame[^1] = (byte)(crc >> 8);
            return frame;
        }

        private static void ValidateHeader(byte[] frame, byte slave, byte exceptedFunCode)
        {
            if (!Crc16.Validate(frame)) throw new InvalidOperationException("响应crc校验失败");
            if (frame[0] != slave) throw new InvalidOperationException("响应从站地址不匹配");
            //异常
            if (frame[1] == (exceptedFunCode | 0x80)) throw new ModbusException(slave, exceptedFunCode, frame[2]);
            if (frame[1] != exceptedFunCode) throw new InvalidOperationException("响应功能码不匹配");
        }

        //请求帧构造

        //读线圈，读离散输入
        public static byte[] BuildReadBits(byte slave, byte funcCode, ushort startAddr, ushort count)
        {
            if (funcCode != FuncReadCoils && funcCode != FuncReadDiscreteInputs) throw new ArgumentException("funcCode must be 01 or 02", nameof(funcCode));
            if (count < 1 || count > MaxReadCoils) throw new ArgumentOutOfRangeException(nameof(count), $"count must in 1~{MaxReadCoils} range");
            return AppendCrc(new byte[] {slave, funcCode,
                (byte)(startAddr >> 8), (byte)(startAddr & 0xFF),
                (byte)(count >> 8), (byte)(count & 0xFF)});
        }

        public static byte[] BuildReadCoils(byte slave, ushort startAddr, ushort count)
        {
            return BuildReadBits(slave, FuncReadCoils, startAddr, count);
        }

        public static byte[] BuildReadDiscreteInputs(byte slave, ushort startAddr, ushort count)
        {
            return BuildReadBits(slave, FuncReadDiscreteInputs, startAddr, count);
        }

        //读两个寄存器
        public static byte[] BuildReadRegs(byte slave, byte funcCode, ushort startAddr, ushort count)
        {
            if (funcCode != FuncReadInputRegs && funcCode != FuncReadHoldingRegs) throw new ArgumentException("funcCode must be 03 or 04", nameof(funcCode));
            if (count < 1 || count > MaxReadRegs) throw new ArgumentOutOfRangeException(nameof(count), $"count must in 1~{MaxReadRegs} range");
            return AppendCrc(new byte[] {slave, funcCode,
                (byte)(startAddr >> 8), (byte)(startAddr & 0xFF),
                (byte)(count >> 8), (byte)(count & 0xFF)});
        }

        public static byte[] BuildReadInputRegs(byte slave, ushort startAddr, ushort count)
        {
            return BuildReadRegs(slave, FuncReadInputRegs, startAddr, count);
        }

        public static byte[] BuildReadHoldingRegs(byte slave, ushort startAddr, ushort count)
        {
            return BuildReadRegs(slave, FuncReadHoldingRegs, startAddr, count);
        }

        //写单个线圈
        public static byte[] BuildWriteSingleCoil(byte slave, ushort addr, bool on)
        {
            return AppendCrc(new byte[] {slave, FuncWriteSingleCoil,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            on ? (byte)(0xFF) : (byte)(0x00), (byte)(0x00)});
        }

        //写单个寄存器
        public static byte[] BuildWriteSingleReg(byte slave, ushort addr, ushort value)
        {
            return AppendCrc(new byte[] {slave, FuncWriteSingleReg,
            (byte)(addr >> 8), (byte)(addr & 0xFF),
            (byte)(value >> 8), (byte)(value & 0xFF)});
        }

        //写多个线圈
        public static byte[] BuildWriteMultipleCoils(byte slave, ushort addr, bool[] values)
        {
            if (values.Length < 1 || values.Length > MaxWriteMultipleCoils) throw new ArgumentOutOfRangeException(nameof(values));
            int valueByteCount = (values.Length + 7) / 8;
            int valueRealByteCount = values.Length;
            byte[] payload = new byte[valueByteCount + 7];
            payload[0] = slave;
            payload[1] = FuncWriteMultipleCoils;
            payload[2] = (byte)(addr >> 8);
            payload[3] = (byte)(addr & 0xFF);
            payload[4] = (byte)(valueRealByteCount >> 8);
            payload[5] = (byte)(valueRealByteCount & 0xFF);
            payload[6] = (byte)(valueByteCount);   //填充到数据帧里的数据位数据字节数量

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i]) payload[7 + i / 8] |= (byte)(1 << (i % 8));   //bit位反向填充
            }
            return AppendCrc(payload);
        }

        //写多个寄存器
        public static byte[] BuildWriteMultipleRegs(byte slave, ushort addr, ushort[] values)
        {
            if (values.Length is < 1 or > MaxWriteMultipleRegs)
                throw new ArgumentOutOfRangeException(nameof(values), $"数量必须在 1~{MaxWriteMultipleRegs} 之间");

            int byteCount = values.Length * 2;
            var payload = new byte[7 + byteCount];
            payload[0] = slave;
            payload[1] = FuncWriteMultipleRegs;
            payload[2] = (byte)(addr >> 8);
            payload[3] = (byte)(addr & 0xFF);
            payload[4] = (byte)(values.Length >> 8);
            payload[5] = (byte)(values.Length & 0xFF);
            payload[6] = (byte)byteCount;
            for (int i = 0; i < values.Length; i++)
            {
                payload[7 + i * 2] = (byte)(values[i] >> 8);         // 高字节在前
                payload[8 + i * 2] = (byte)(values[i] & 0xFF);
            }

            return AppendCrc(payload);
        }

        //数据帧解析
        public static ushort[] ParseReadRegsResponse(byte[] frame, byte slave, byte funCode, ushort exceptedCount)
        {
            ValidateHeader(frame, slave, funCode);
            int byteCount = frame[2];
            if (byteCount != exceptedCount * 2) throw new FormatException("响应字节数不符");
            var values = new ushort[exceptedCount];
            for(int i = 0; i < exceptedCount; i++)
            {
                values[i] = (ushort)(frame[3 + i * 2] << 8 | frame[4 + i * 2]);
            }

            return values;
        }

        public static bool[] ParseReadBitsResponse(byte[] frame, byte slave, byte funCode, ushort exceptedCount)
        {
            ValidateHeader(frame, slave, funCode);
            int byteCount = frame[2];
            if (byteCount != (exceptedCount + 7) / 8) throw new FormatException("响应字节数不符");
            var values = new bool[exceptedCount];
            for (int i = 0; i < exceptedCount; i++)
            {
                values[i] = (frame[3 + i / 8] & (1 << (i % 8))) != 0;
            }
            return values;
        }
    }
}
