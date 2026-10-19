using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TempHumiditySystem.Models;
using TempHumiditySystem.Services.Modbus;
using TempHumiditySystem.Services.SerialAssistant;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 通信设置页：串口配置 + 连接/断开，连接后启动轮询和历史采样。
    /// </summary>
    public partial class CommViewModel : ObservableObject, IDisposable
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
        /// <summary>可用串口列表；支持手动刷新，不必重启程序。</summary>
        public ObservableCollection<string> Ports { get; } = new();

        public int[] BaudRates { get; } = { 4800, 9600, 19200, 38400, 57600, 115200 };
        public Parity[] Parities { get; } = { Parity.None, Parity.Even, Parity.Odd };
        public StopBits[] StopBitsOptions { get; } = { StopBits.One, StopBits.Two };

        public CommViewModel(PollingService polling, HistorySampler historySampler)
        {
            _polling = polling;
            _historySampler = historySampler;

            // 真实链路状态：自动重连失败/成功时同步到 UI，而不是只看命令是否执行成功
            _polling.connectionLost += OnConnectionLost;
            _polling.connectionRestored += OnConnectionRestored;

            LoadPorts();
            ApplySavedSettings();
        }

        /// <summary>重新枚举串口（插拔 USB 转串口后使用）。</summary>
        private void LoadPorts()
        {
            var current = PortName;

            Ports.Clear();
            foreach (var name in SerialPortUtil.GetPorts().Select(p => p.PortName).Distinct())
                Ports.Add(name);

            // 已保存的串口可能暂时不存在（设备没插）：仍保留为可选项，避免 ComboBox 文本被清空
            if (!string.IsNullOrWhiteSpace(current) && !Ports.Contains(current))
                Ports.Add(current);
        }

        [RelayCommand]
        private void RefreshPorts()
        {
            LoadPorts();
            StatusText = $"已刷新串口列表（{Ports.Count} 个）";
        }

        /// <summary>启动时恢复上次使用的通信参数。</summary>
        private void ApplySavedSettings()
        {
            var settings = AppSettings.Current;

            if (!string.IsNullOrWhiteSpace(settings.CommPortName))
                PortName = settings.CommPortName;

            if (settings.CommBaudRate is int baudRate)
                BaudRate = baudRate;

            if (settings.CommParity is Parity parity)
                Parity = parity;

            if (settings.CommStopBits is StopBits stopBits)
                StopBits = stopBits;
        }

        /// <summary>连接成功后记住本次参数，下次启动直接恢复。</summary>
        private void PersistSettings()
        {
            var settings = AppSettings.Current;
            settings.CommPortName = PortName;
            settings.CommBaudRate = BaudRate;
            settings.CommParity = Parity;
            settings.CommStopBits = StopBits;
            settings.Save();   // 失败时静默：仅本次运行有效，不影响通信
        }

        private void OnConnectionLost(string reason)
        {
            // 可能由轮询后台线程触发；标量属性赋值，WPF 会自动 marshal 到 UI 线程
            IsConnected = false;
            StatusText = $"连接已断开：{reason}";
        }

        private void OnConnectionRestored()
        {
            IsConnected = true;
            StatusText = $"已重新连接 {PortName}";
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
                PersistSettings();
            }
            catch (Exception ex)
            {
                StatusText = $"连接失败：{ex.Message}";
            }
        }

        private bool CanConnect() => !IsStopping;

        public void Dispose()
        {
            _polling.connectionLost -= OnConnectionLost;
            _polling.connectionRestored -= OnConnectionRestored;
        }
    }
}
