using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>设备控制页：手动开关 + 模式切换。</summary>
    public partial class ControlViewModel : ObservableObject
    {
        private readonly ControlEngine _control;

        public IReadOnlyList<Device> Devices { get; }

        public ControlViewModel(ControlEngine control, SystemConfig config)
        {
            _control = control;
            Devices = config.Devices;
        }

        /// <summary>手动开关（放后台线程，写命令阻塞）。</summary>
        [RelayCommand]
        private async Task ToggleDeviceAsync(Device device)
        {
            bool target = !device.IsRunning;
            await Task.Run(() => _control.SetManual(device, target));
        }

        /// <summary>切回自动（纯内存操作，UI 线程安全）。</summary>
        [RelayCommand]
        private void SetAuto(Device device) => _control.SetAuto(device);

        /// <summary>切到手动（纯内存操作，不会写寄存器；之后可点"开/关"手动控制）。</summary>
        [RelayCommand]
        private void SetManualMode(Device device) => _control.SetManualMode(device);
    }
}
