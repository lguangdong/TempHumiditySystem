using System;
using System.Collections.Generic;
using TempHumiditySystem.Models.ModBus;

namespace TempHumiditySystem.Services.Modbus
{
    /// <summary>
    /// 设备控制引擎：自动模式按测点值做迟滞判断，手动模式由界面直接下发开关。
    /// 写寄存器是阻塞 IO 且可能失败，所有异常都在引擎内部消化并通过 <see cref="writeFailed"/> 上报，
    /// 绝不允许冒泡到轮询线程（否则会被当成"该测点读取失败"，误触发链路重连）。
    /// </summary>
    public sealed class ControlEngine
    {
        private readonly ModbusRTUClient _client;
        private readonly List<Device> _devices = new();

        /// <summary>写寄存器失败（设备, 原因）。由组合根接线到日志。</summary>
        public event Action<Device, string>? writeFailed;

        public ControlEngine(ModbusRTUClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// 下发开关命令。成功返回 true；失败时保持 <see cref="Device.IsRunning"/> 原值并上报事件，
        /// 下一个轮询周期会重新判断，避免一次写失败就与真实状态脱节。
        /// </summary>
        private bool TrySetDevice(Device device, bool on)
        {
            try
            {
                _client.WriteSingleReg(device.SlaveId, device.WriteAddress, on ? (ushort)1 : (ushort)0);
                device.IsRunning = on;
                return true;
            }
            catch (Exception ex)
            {
                writeFailed?.Invoke(device, ex.Message);
                return false;
            }
        }

        //手动设置：切手动并立即下发命令，返回是否写入成功
        public bool SetManual(Device d, bool on)
        {
            d.Mode = ControlMode.Manual;
            return TrySetDevice(d, on);
        }

        //设置为自动模式
        public void SetAuto(Device d) => d.Mode = ControlMode.Auto;

        //设置为手动模式（仅切模式，不改动设备开关状态）
        public void SetManualMode(Device d) => d.Mode = ControlMode.Manual;

        public void AddDevices(IEnumerable<Device> devices) => _devices.AddRange(devices);

        private void Evaluate(Device d, SensorPoint s)
        {
            bool shouldBeOn;
            if (d.IsRunning)
            {
                //是否需要停止，比较offAt
                shouldBeOn = d.TurnsOnBelow ? s.CurrentValue < d.OffAt  //升温设备：高于 OffAt 关闭
                                            : s.CurrentValue > d.OffAt; //降温设备：低于 OffAt 关闭
            }
            else
            {
                //是否需要开启，比较onAt
                shouldBeOn = d.TurnsOnBelow ? s.CurrentValue <= d.OnAt
                                            : s.CurrentValue >= d.OnAt;
            }

            if (shouldBeOn != d.IsRunning) TrySetDevice(d, shouldBeOn);
        }

        //自动控制功能
        public void Check(SensorPoint s)
        {
            if (!s.IsOnline) return;
            foreach (var item in _devices)
            {
                if (item.Mode != ControlMode.Auto) continue;
                if (item.LinkedSensorName != s.Name) continue;

                // 单台设备写失败不影响其它设备，也不影响本次轮询结果
                try { Evaluate(item, s); }
                catch (Exception ex) { writeFailed?.Invoke(item, ex.Message); }
            }
        }
    }
}
