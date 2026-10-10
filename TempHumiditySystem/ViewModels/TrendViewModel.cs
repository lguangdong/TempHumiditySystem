using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>趋势曲线：每个测点一条曲线，X 轴为时间，靠 LiveCharts2 数据绑定自动刷新。</summary>
    public partial class TrendViewModel : ObservableObject
    {
        private readonly IReadOnlyList<SensorPoint> _sensors;
        private readonly Dictionary<string, ObservableCollection<ObservablePoint>> _valuesMap = new();

        // LiveCharts2 绑定用
        public ObservableCollection<ISeries> Series { get; } = new();
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

        public TrendViewModel(HistoryStore store, SystemConfig config)
        {
            _sensors = config.Sensors;

            // 为每个测点建一条曲线
            for (int i = 0; i < _sensors.Count; i++)
            {
                var sensor = _sensors[i];
                var values = new ObservableCollection<ObservablePoint>();
                _valuesMap[sensor.Name] = values;

                Series.Add(new LineSeries<ObservablePoint>
                {
                    Name = sensor.Name,
                    Values = values,
                    GeometrySize = 0,        // 不画数据点，只画线
                    LineSmoothness = 0,      // 直线
                });
            }

            // X 轴：时间。X 存 DateTime.Ticks，Labeler 转回时间字符串
            XAxes = new[]
            {
                new Axis
                {
                    Labeler = value =>
                    {
                        // 无数据或轴初始化时 LiveCharts2 可能传入 NaN/Infinity/越界值，需先校验
                        if (double.IsNaN(value) || double.IsInfinity(value))
                            return string.Empty;
                        if (value < DateTime.MinValue.Ticks || value > DateTime.MaxValue.Ticks)
                            return string.Empty;
                        return new DateTime((long)value).ToString("HH:mm:ss");
                    },
                }
            };
            YAxes = new[] { new Axis { Name = "值" } };

            // 采样新增 → 往各曲线追加点
            store.historySampleAdded += OnSampleAdded;
        }

        private void OnSampleAdded(HistorySample sample)
        {
            // LiveCharts2 内部能处理跨线程的集合更新（实时图表示例即后台线程更新）
            for (int i = 0; i < _sensors.Count; i++)
            {
                if (_valuesMap.TryGetValue(_sensors[i].Name, out var values))
                    values.Add(new ObservablePoint(sample.Time.Ticks, sample.Values[i]));
            }
        }
    }
}
