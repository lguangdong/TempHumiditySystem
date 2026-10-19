using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TempHumiditySystem.Models.ModBus;
using TempHumiditySystem.Services.Modbus;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 趋势曲线：每个测点一条曲线，X 轴为时间，靠 LiveCharts2 数据绑定自动刷新。
    /// 为防长时间运行后点数无限增长，曲线只保留最近 <see cref="Window"/> 的数据，
    /// 并设有 <see cref="MaxPointsPerSeries"/> 硬上限兜底。
    /// </summary>
    public partial class TrendViewModel : ObservableObject, IDisposable
    {
        /// <summary>单条曲线保留的硬上限（点数）。1 秒采样、1 小时窗口约 3600 点。</summary>
        public const int MaxPointsPerSeries = 4000;

        /// <summary>曲线配色：按测点顺序循环取色。</summary>
        private static readonly SKColor[] Palette =
        {
            SKColor.Parse("#2563EB"),
            SKColor.Parse("#EF4444"),
            SKColor.Parse("#10B981"),
            SKColor.Parse("#F59E0B"),
            SKColor.Parse("#8B5CF6"),
            SKColor.Parse("#06B6D4"),
        };

        /// <summary>图表用的中文字体（与全局字体配置保持一致，避免轴标签回退成方块）。</summary>
        private static readonly SKTypeface ChartTypeface =
            SKFontManager.Default.MatchCharacter('汉') ?? SKTypeface.Default;

        private readonly List<SeriesBuffer> _buffers = new();
        private readonly HistoryStore _store;

        /// <summary>曲线保留的时间窗口。1 秒采样下默认 1 小时（约 3600 点/曲线），兼顾细腻度与流畅度。</summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);

        /// <summary>界面提示文案。</summary>
        public string WindowText =>
            $"曲线仅显示最近 {Window.TotalHours:0.#} 小时（采样间隔 1 秒，历史数据保留 24 小时）";

        // LiveCharts2 绑定用
        public ObservableCollection<ISeries> Series { get; } = new();
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

        /// <summary>绘图区边框，让曲线和坐标轴看起来更收敛。</summary>
        public DrawMarginFrame DrawMarginFrame { get; } = new()
        {
            Stroke = new SolidColorPaint(SKColor.Parse("#E5E7EB")) { StrokeThickness = 1 },
        };

        public TrendViewModel(HistoryStore store, SystemConfig config)
        {
            _store = store;

            var labelsPaint = TextPaint("#6B7280");
            var separatorsPaint = new SolidColorPaint(SKColor.Parse("#F1F5F9")) { StrokeThickness = 1 };

            // 为每个测点建一条曲线；用索引映射到 HistorySample.Values（HistorySampler 按 config.Sensors 顺序采样）。
            // 不用测点名做键——重名测点会互相覆盖，导致某条曲线永远收不到数据。
            for (int i = 0; i < config.Sensors.Count; i++)
            {
                var sensor = config.Sensors[i];
                var values = new ObservableCollection<ObservablePoint>();
                _buffers.Add(new SeriesBuffer(i, values));

                Series.Add(new LineSeries<ObservablePoint>
                {
                    Name = sensor.Name,
                    Values = values,
                    GeometrySize = 0,        // 不画数据点，只画线
                    LineSmoothness = 0,      // 直线，保证读数准确
                    Fill = null,             // 不要半透明面积
                    EnableNullSplitting = true, // null 视为断点：离线时段曲线断开，而不是连成一条直线
                    Stroke = new SolidColorPaint(Palette[i % Palette.Length]) { StrokeThickness = 2 },
                    // 温度用左轴、湿度用右轴：两者量纲差一个数量级，共用一个轴会把温度压成直线
                    ScalesYAt = sensor.Category == SensorCategory.Humidity ? 1 : 0,
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
                    LabelsPaint = labelsPaint,
                    SeparatorsPaint = separatorsPaint,
                    TextSize = 12,
                    Name = "时间",
                    NamePaint = labelsPaint,
                    NameTextSize = 12,
                }
            };

            YAxes = new[]
            {
                new Axis
                {
                    Name = "温度",
                    LabelsPaint = labelsPaint,
                    SeparatorsPaint = separatorsPaint,
                    TextSize = 12,
                    NamePaint = labelsPaint,
                    NameTextSize = 12,
                },
                new Axis
                {
                    Name = "湿度",
                    Position = AxisPosition.End,
                    LabelsPaint = labelsPaint,
                    SeparatorsPaint = null,   // 两套横向网格线会重叠，右轴只画标签
                    TextSize = 12,
                    NamePaint = labelsPaint,
                    NameTextSize = 12,
                },
            };

            // 先用已有历史回填（VM 若在开始采样之后才创建，也能立刻看到曲线）
            SeedFromStore(store);

            // 采样新增 → 往各曲线追加点
            store.historySampleAdded += OnSampleAdded;
        }

        private static SolidColorPaint TextPaint(string hex)
            => new(SKColor.Parse(hex)) { SKTypeface = ChartTypeface };

        /// <summary>用历史存储里的数据回填曲线，回填量不超过硬上限。</summary>
        private void SeedFromStore(HistoryStore store)
        {
            var samples = store.GetAll();
            int start = Math.Max(0, samples.Count - MaxPointsPerSeries);
            for (int i = start; i < samples.Count; i++)
                Append(samples[i], trim: false);

            // 回填后统一按时间窗口裁剪一次
            Trim(DateTime.Now.Ticks);
        }

        private void OnSampleAdded(HistorySample sample)
        {
            // LiveCharts2 内部能处理跨线程的集合更新（实时图表示例即后台线程更新）
            Append(sample, trim: true);
        }

        private void Append(HistorySample sample, bool trim)
        {
            foreach (var buffer in _buffers)
            {
                if (buffer.SampleIndex >= sample.Values.Count) continue;

                // 离线采样值是 NaN：必须转成 null 再交给 LiveCharts。
                // EnableNullSplitting 只认 null，NaN 会进入路径计算把整条线的几何坐标污染成 NaN，导致线画不出来。
                double value = sample.Values[buffer.SampleIndex];
                buffer.Values.Add(new ObservablePoint(sample.Time.Ticks, double.IsNaN(value) ? null : value));
            }

            if (trim) Trim(sample.Time.Ticks);
        }

        /// <summary>
        /// 按时间窗口裁剪：从头丢弃过期点，再按点数上限兜底。
        /// 1 秒采样、1 小时窗口下正常每次只移除 1 个点，开销可忽略。
        /// </summary>
        private void Trim(long latestTicks)
        {
            long cutoff = latestTicks - Window.Ticks;

            foreach (var buffer in _buffers)
            {
                var values = buffer.Values;

                while (values.Count > 0 && (values[0].X ?? 0) < cutoff)
                    values.RemoveAt(0);

                while (values.Count > MaxPointsPerSeries)
                    values.RemoveAt(0);
            }
        }

        public void Dispose() => _store.historySampleAdded -= OnSampleAdded;

        /// <summary>一条曲线的缓冲区：SampleIndex 指向 HistorySample.Values 的下标。</summary>
        private sealed class SeriesBuffer
        {
            public SeriesBuffer(int sampleIndex, ObservableCollection<ObservablePoint> values)
            {
                SampleIndex = sampleIndex;
                Values = values;
            }

            public int SampleIndex { get; }
            public ObservableCollection<ObservablePoint> Values { get; }
        }
    }
}
