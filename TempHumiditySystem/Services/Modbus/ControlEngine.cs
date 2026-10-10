using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TempHumiditySystem.Models.ModBus;

namespace TempHumiditySystem.Services.Modbus
{
    public sealed class ControlEngine
    {
        private readonly ModbusRTUClient _client;
        private readonly List<Device> _devices = new();
      
        public ControlEngine(ModbusRTUClient client) {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        private void SetDevice(Device device, bool on)
        {
            _client.WriteSingleReg(device.SlaveId, device.WriteAddress, on ? (ushort)1 : (ushort)0);
            device.IsRunning = on;
        }

        //手动设置
        public void SetManual(Device d, bool on)
        {
            d.Mode = ControlMode.Manual;
            SetDevice(d, on);
        }

        //设置为自动模式
        public void SetAuto(Device d)
        {
            d.Mode = ControlMode.Auto;
        }

        public void AddDevices(IEnumerable<Device> devices)
        {
            _devices.AddRange(devices);
        }

        private void Evaluate(Device d, SensorPoint s)
        {
            double curValue = s.CurrentValue;
            bool shouldBeOn;
            if (d.IsRunning)
            {
                //是否需要停止，比较offAt
                shouldBeOn=d.TurnsOnBelow ? s.CurrentValue < d.OffAt //保持开启，升温设备，如果高于offAt则关闭
                    : s.CurrentValue > d.OffAt;  //保持开启，降温设备，如果低于offAt则关闭
            } else
            {
                //开启,同上边的原理
                shouldBeOn=d.TurnsOnBelow ? s.CurrentValue<= d.OnAt
                    : s.CurrentValue >= d.OnAt;
            }

            if(shouldBeOn != d.IsRunning) SetDevice(d, shouldBeOn);
        }

        //自动控制功能
        public void Check(SensorPoint s)
        {
            if (!s.IsOnline) return;
            foreach (var item in _devices)
            {
                if (item.Mode != ControlMode.Auto) continue;
                if(item.LinkedSensorName != s.Name) continue;
                Evaluate(item, s);
            }
        }
    }
}
