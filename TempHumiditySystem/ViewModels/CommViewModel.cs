using System;
using System.IO.Ports;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempHumiditySystem.Services.Modbus;
using TempHumiditySystem.Services.SerialAssistant;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 通信设置页：串口配置 + 连接/断开，连接后启动轮询和历史采样。
    /// </summary>
    public partial class CommViewModel : ObservableObject
    {
        private readonly PollingService _polling;
        private readonly HistorySampler _historySampler;

        // ===== 串口配置 =====
        [ObservableProperty] private string _portName = "COM3";
        [ObservableProperty] private int _baudRate = 9600;
        [ObservableProperty] private Parity _parity = Parity.None;
        [ObservableProperty] private StopBits _stopBits = StopBits.One;

        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isStopping;
        [ObservableProperty] private string _statusText = "未连接";

        partial void OnIsStoppingChanged(bool value) => ConnectCommand.NotifyCanExecuteChanged();

        // ===== 下拉选项 =====
        public string[] Ports { get; } = SerialPortUtil.GetPorts().Select(p => p.PortName).ToArray();
        public int[] BaudRates { get; } = { 4800, 9600, 19200, 38400, 57600, 115200 };
        public Parity[] Parities { get; } = { Parity.None, Parity.Even, Parity.Odd };
        public StopBits[] StopBitsOptions { get; } = { StopBits.One, StopBits.Two };

        public CommViewModel(PollingService polling, HistorySampler historySampler)
        {
            _polling = polling;
            _historySampler = historySampler;
        }

        [RelayCommand(CanExecute = nameof(CanConnect))]
        private async Task ConnectAsync()
        {
            if (IsConnected)
            {
                // 关键：先切状态，让 UI 立刻重绘出「断开中…」
                IsStopping = true;
                StatusText = "正在断开，请稍候…";

                try
                {
                    // 把阻塞的 Stop 丢到后台线程，UI 线程不再被 Join 冻结
                    // 先停历史采样，再停轮询（顺序不能反）
                    await Task.Run(() =>
                    {
                        Thread.Sleep(2000);
                        _historySampler.Stop();
                        _polling.Stop();
                    });
                }
                finally
                {
                    IsConnected = false;
                    IsStopping = false;
                    StatusText = "已断开";
                }
                return;
            }

            try
            {
                _polling.Start(new SerialSetting
                {
                    PortName = PortName,
                    BaudRate = BaudRate,
                    Parity = Parity,
                    StopBits = StopBits,
                });
                _historySampler.Start();
                IsConnected = true;
                StatusText = $"已连接 {PortName}";
            }
            catch (Exception ex)
            {
                StatusText = $"连接失败：{ex.Message}";
            }
        }

        private bool CanConnect() => !IsStopping;
    }
}
