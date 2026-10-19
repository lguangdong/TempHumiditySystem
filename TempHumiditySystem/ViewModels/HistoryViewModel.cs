using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TempHumiditySystem.Models;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>历史数据页：有效样本统计 + 导出 CSV + 打开最近导出目录。</summary>
    public partial class HistoryViewModel : ObservableObject, IDisposable
    {
        private readonly HistoryStore _store;
        private readonly IReadOnlyList<SensorPoint> _sensors;

        /// <summary>有效样本条数（全测点无效的采样不会写入，所以这里不含空样本）。</summary>
        [ObservableProperty] private int _sampleCount;

        /// <summary>当前数据覆盖的时间范围。</summary>
        [ObservableProperty] private string _timeRangeText = "暂无数据";

        /// <summary>导出/打开目录的结果提示。</summary>
        [ObservableProperty] private string _statusText = "";

        /// <summary>最近一次导出的完整路径（持久化到 settings.json）。</summary>
        [ObservableProperty] private string _lastExportPath = "";

        public string LastExportText => string.IsNullOrWhiteSpace(LastExportPath) ? "暂无" : LastExportPath;

        partial void OnLastExportPathChanged(string value) => OnPropertyChanged(nameof(LastExportText));

        public HistoryViewModel(HistoryStore store, SystemConfig config)
        {
            _store = store;
            _sensors = config.Sensors;

            LastExportPath = AppSettings.Current.LastExportPath ?? "";
            SampleCount = store.Count;
            RefreshTimeRange();

            store.historySampleAdded += OnSampleAdded;
        }

        private void OnSampleAdded(HistorySample _)
        {
            SampleCount = _store.Count;
            RefreshTimeRange();
        }

        private void RefreshTimeRange()
        {
            var first = _store.FirstSampleTime;
            var last = _store.LastSampleTime;

            TimeRangeText = first is null || last is null
                ? "暂无数据"
                : $"{first:MM-dd HH:mm:ss} ~ {last:MM-dd HH:mm:ss}";
        }

        [RelayCommand]
        private void Export()
        {
            if (SampleCount == 0)
            {
                StatusText = "暂无有效数据，无法导出";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = $"历史数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                int count = WriteCsv(dialog.FileName);
                LastExportPath = dialog.FileName;

                // 记住导出位置，下次可直接打开目录（设置写入失败不影响导出结果）
                var settings = AppSettings.Current;
                settings.LastExportPath = dialog.FileName;
                settings.Save();

                StatusText = $"已导出 {count} 条有效记录到 {dialog.FileName}";
            }
            catch (Exception ex)
            {
                // 目标目录只读、文件被 Excel 占用等：给出提示而不是直接把程序打崩
                StatusText = $"导出失败：{ex.Message}";
            }
        }

        /// <summary>打开最近导出所在的目录；文件还在时直接在资源管理器中选中它。</summary>
        [RelayCommand]
        private void OpenExportFolder()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(LastExportPath) && File.Exists(LastExportPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LastExportPath}\"")
                    {
                        UseShellExecute = true,
                    });
                    return;
                }

                var dir = string.IsNullOrWhiteSpace(LastExportPath)
                    ? null
                    : Path.GetDirectoryName(LastExportPath);

                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    StatusText = "尚未导出过文件，已打开「文档」目录";
                }

                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText = $"打开目录失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 流式写 CSV：1 秒采样、24 小时约 8.6 万行，一次性拼大字符串既占内存又容易长时间卡 UI。
        /// UTF-8 BOM 保证 Excel 打开中文不乱码。
        /// </summary>
        private int WriteCsv(string path)
        {
            using var writer = new StreamWriter(path, append: false, new UTF8Encoding(true));

            // 表头：时间 + 各测点名
            writer.Write("时间");
            foreach (var s in _sensors) writer.Write("," + Escape(s.Name));
            writer.WriteLine();

            var samples = _store.GetAll();
            foreach (var sample in samples)
            {
                writer.Write(sample.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                foreach (var v in sample.Values)
                {
                    // 局部离线时该列仍为空；整条全空的时间点已在采样端被过滤，不会出现在导出里
                    writer.Write(',');
                    if (!double.IsNaN(v)) writer.Write(v.ToString("F1"));
                }
                writer.WriteLine();
            }

            return samples.Count;
        }

        /// <summary>CSV 字段转义：含逗号/引号/换行时用双引号包裹，内部引号翻倍。</summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            bool needQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            return needQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public void Dispose() => _store.historySampleAdded -= OnSampleAdded;
    }
}
