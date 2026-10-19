using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>历史数据页：显示数据条数 + 导出 CSV。</summary>
    public partial class HistoryViewModel : ObservableObject
    {
        private readonly HistoryStore _store;
        private readonly IReadOnlyList<SensorPoint> _sensors;

        [ObservableProperty] private int _sampleCount;

        public HistoryViewModel(HistoryStore store, SystemConfig config)
        {
            _store = store;
            _sensors = config.Sensors;
            SampleCount = store.Count;
            store.historySampleAdded += _ => SampleCount = store.Count;   // 标量属性，WPF 自动跨线程
        }

        [RelayCommand]
        private void Export()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = $"历史数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };
            if (dialog.ShowDialog() != true) return;

            File.WriteAllText(dialog.FileName, BuildCsv(), new UTF8Encoding(true));  // UTF-8 BOM，Excel 中文不乱码
        }

        private string BuildCsv()
        {
            var sb = new StringBuilder();

            // 表头：时间 + 各测点名
            sb.Append("时间");
            foreach (var s in _sensors) sb.Append(',').Append(s.Name);
            sb.AppendLine();

            // 数据行
            foreach (var sample in _store.GetAll())
            {
                sb.Append(sample.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                foreach (var v in sample.Values)
                {
                    // NaN 表示该时刻该测点无有效数据（离线/未就绪）：留空单元格，不写成 "NaN"
                    sb.Append(',').Append(double.IsNaN(v) ? "" : v.ToString("F1"));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
