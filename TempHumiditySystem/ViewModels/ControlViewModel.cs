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

        /// <summary>最近一次操作的结果提示（写失败时给出原因，避免"点了没反应"）。</summary>
        [ObservableProperty] private string _statusText = "";

        public ControlViewModel(ControlEngine control, SystemConfig config)
        {
            _control = control;
            Devices = config.Devices;
        }

        /// <summary>手动开关（放后台线程，写命令阻塞）。失败由引擎内部消化并返回 false，这里负责提示。</summary>
        [RelayCommand]
        private async Task ToggleDeviceAsync(Device device)
        {
            if (device == null) return;

            bool target = !device.IsRunning;
            StatusText = $"正在{(target ? "启动" : "停止")} {device.Name}…";

            bool ok = await Task.Run(() => _control.SetManual(device, target));

            StatusText = ok
                ? $"{device.Name} 已{(target ? "启动" : "停止")}"
                : $"{device.Name} 操作失败：串口写入异常，请检查通信状态";
        }

        /// <summary>切回自动（纯内存操作，UI 线程安全）。</summary>
        [RelayCommand]
        private void SetAuto(Device device) => _control.SetAuto(device);

        /// <summary>切到手动（纯内存操作，不会写寄存器；之后可点"开/关"手动控制）。</summary>
        [RelayCommand]
        private void SetManualMode(Device device) => _control.SetManualMode(device);
    }
}
