using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// 绘图区只画最近 <see cref="Window"/>（5 分钟）的固定宽度窗口，密度恒定；
    /// 缓冲区额外保留 <see cref="Retention"/>（1 小时）供回看，
    /// 并设有 <see cref="MaxPointsPerSeries"/> 硬上限兜底。
    /// </summary>
    public partial class TrendViewModel : ObservableObject, IDisposable
    {
        /// <summary>单条曲线保留的硬上限（点数）。1 秒采样下对应 66 分钟。</summary>
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

        /// <summary>
        /// 绘图窗口宽度：固定 3 分钟滚动窗口。
        /// 1 秒采样约 180 点铺满一屏（约 2.8 像素/点），曲线密度始终恒定，不会越跑越糊。
        /// </summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>缓冲区保留时长：比绘图窗口长，关掉「跟随最新数据」后还能往左拖回看 1 小时。</summary>
        public TimeSpan Retention { get; } = TimeSpan.FromHours(1);

        /// <summary>界面提示文案。</summary>
        public string WindowText =>
            $"曲线显示最近 {Window.TotalMinutes:0} 分钟的滑动窗口（采样间隔 1 秒，约 180 点/条；可回溯 1 小时）";

        // LiveCharts2 绑定用
        public ObservableCollection<ISeries> Series { get; } = new();
        public Axis[] XAxes { get; }
        public Axis[] YAxes { get; }

        /// <summary>
        /// 页面右侧的曲线显示开关（每个测点一条）：测点多了以后全部绘制会糊成一片，
        /// 这里让用户只勾选关心的几条。隐藏的曲线仍在后台累积数据，重新勾选即可看到完整窗口。
        /// </summary>
        public ObservableCollection<CurveSwitch> Curves { get; } = new();

        /// <summary>所有曲线（与 <see cref="Curves"/> 同序）；<see cref="Series"/> 只装当前勾选的。</summary>
        private readonly List<ISeries> _allSeries = new();

        /// <summary>每条曲线使用哪条 Y 轴（0 = 温度左侧，1 = 湿度右侧），与 <see cref="_allSeries"/> 同序。</summary>
        private readonly List<int> _seriesAxes = new();

        /// <summary>批量勾选/取消时暂停逐条同步，避免重复重建曲线集合。</summary>
        private bool _suspendSync;

        /// <summary>最近一次采样的时刻（Ticks），用于"回到最新"时立刻定位窗口。</summary>
        private long _latestTicks;

        /// <summary>
        /// 是否让横轴跟随最新数据（默认开）。
        /// 勾选时每条采样都把横轴钉到「最近一个窗口」，新数据永远可见；
        /// 在图上按住左键拖动会由视图层自动取消勾选（<c>TrendView.xaml.cs</c>），
        /// 于是拖动＝暂停跟随，重新勾选即立刻跳回最新。
        /// </summary>
        [ObservableProperty] private bool _isFollowing = true;

        partial void OnIsFollowingChanged(bool value)
        {
            if (value) SnapToLatest();
        }

        /// <summary>顶部提示：当前显示几条 / 共几条。</summary>
        public string VisibleText
        {
            get
            {
                int visible = Curves.Count(c => c.IsVisible);
                return visible == Curves.Count
                    ? $"共 {Curves.Count} 条曲线"
                    : $"显示 {visible} / {Curves.Count} 条曲线";
            }
        }

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

                var color = Palette[i % Palette.Length];
                bool usesHumidityAxis = sensor.Category == SensorCategory.Humidity;
                var series = new LineSeries<ObservablePoint>
                {
                    Name = sensor.Name,
                    Values = values,
                    // 数据点仍然"看不见"，但保留几何尺寸：LiveCharts 是按每个点的命中范围（HoverArea）找悬停点的，
                    // 之前 GeometrySize = 0 会让命中范围小到几乎为零 → 悬浮气泡时有时无。
                    GeometrySize = 8,
                    GeometryFill = new SolidColorPaint(SKColors.Transparent),
                    GeometryStroke = null,
                    IsHoverable = true,
                    LineSmoothness = 0,      // 直线，保证读数准确
                    Fill = null,             // 不要半透明面积
                    EnableNullSplitting = true, // null 视为断点：离线时段曲线断开，而不是连成一条直线
                    Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                    // 温度用左轴、湿度用右轴：两者量纲差一个数量级，共用一个轴会把温度压成直线
                    ScalesYAt = usesHumidityAxis ? 1 : 0,
                };
                _seriesAxes.Add(usesHumidityAxis ? 1 : 0);

                _allSeries.Add(series);
                Series.Add(series);          // 默认全部显示，与改造前行为一致

                var curve = new CurveSwitch(sensor.Name, sensor.Unit, color);
                curve.PropertyChanged += OnCurveSwitchChanged;
                Curves.Add(curve);
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

            // 每来一条数据就把横轴钉回「最近一个窗口」，保证任何时候都看得到最新数据
            ApplyFollowWindow(sample.Time.Ticks);
        }

        // ================= 跟随最新数据 =================

        /// <summary>
        /// 把横轴固定到「最近一个窗口」：右端永远是最后一笔数据。
        /// 数据还没攒满一个窗口时（刚启动、刚连接），左端取最早的数据，
        /// 这样曲线铺满整张图，而不是被压到右边一小段。
        /// </summary>
        private void ApplyFollowWindow(long latestTicks)
        {
            _latestTicks = latestTicks;
            if (!IsFollowing) return;

            var axis = XAxes[0];

            long min = latestTicks - Window.Ticks;

            // 数据不足一个窗口：按实际数据范围显示
            long earliest = EarliestTicks();
            if (earliest > 0 && earliest > min) min = earliest;

            // 只有一两个点时给个最小跨度，避免轴范围为 0 画不出线
            long minSpan = TimeSpan.FromSeconds(5).Ticks;
            if (latestTicks - min < minSpan) min = latestTicks - minSpan;

            axis.MinLimit = min;
            axis.MaxLimit = latestTicks;
        }

        /// <summary>当前曲线里最早的数据时刻（没有数据时返回 0）。</summary>
        private long EarliestTicks()
        {
            foreach (var buffer in _buffers)
                if (buffer.Values.Count > 0)
                    return (long)(buffer.Values[0].X ?? 0);

            return 0;
        }

        /// <summary>立刻回到最新数据（重新勾选「跟随最新数据」时使用）。</summary>
        public void SnapToLatest()
        {
            var latest = _latestTicks > 0 ? _latestTicks : DateTime.Now.Ticks;
            ApplyFollowWindow(latest);
        }

        /// <summary>
        /// 按时间窗口裁剪：从头丢弃过期点，再按点数上限兜底。
        /// 1 秒采样、1 小时窗口下正常每次只移除 1 个点，开销可忽略。
        /// </summary>
        private void Trim(long latestTicks)
        {
            long cutoff = latestTicks - Retention.Ticks;

            foreach (var buffer in _buffers)
            {
                var values = buffer.Values;

                while (values.Count > 0 && (values[0].X ?? 0) < cutoff)
                    values.RemoveAt(0);

                while (values.Count > MaxPointsPerSeries)
                    values.RemoveAt(0);
            }
        }

        // ================= 曲线显示开关 =================

        private void OnCurveSwitchChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CurveSwitch.IsVisible) || _suspendSync) return;
            SyncSeries();
        }

        /// <summary>
        /// 按勾选状态增删 <see cref="Series"/>：只做增量（隐藏就移除、勾上就插回原位置），
        /// 不清空重建，避免每次勾选都让整张图重画/重放动画。
        /// </summary>
        private void SyncSeries()
        {
            for (int i = 0; i < _allSeries.Count; i++)
            {
                var series = _allSeries[i];
                bool shouldShow = Curves[i].IsVisible;
                int index = Series.IndexOf(series);

                if (shouldShow && index < 0)
                {
                    // 计算它在测点顺序中的插入位置：前面已显示的曲线条数
                    int insertAt = 0;
                    for (int j = 0; j < i; j++)
                        if (Series.Contains(_allSeries[j])) insertAt++;

                    Series.Insert(insertAt, series);
                }
                else if (!shouldShow && index >= 0)
                {
                    Series.RemoveAt(index);
                }
            }

            UpdateAxesVisibility();
            OnPropertyChanged(nameof(VisibleText));
        }

        /// <summary>没有任何曲线使用的 Y 轴直接隐藏，免得空轴上留着一串 0~10 的刻度干扰读数。</summary>
        private void UpdateAxesVisibility()
        {
            bool anyTemperature = false, anyHumidity = false;

            for (int i = 0; i < _allSeries.Count; i++)
            {
                if (!Curves[i].IsVisible) continue;

                if (_seriesAxes[i] == 1) anyHumidity = true;
                else anyTemperature = true;
            }

            // 全部取消勾选时保留两条轴（空图也要有刻度框），否则按实际使用情况显示
            bool nothingVisible = !anyTemperature && !anyHumidity;
            YAxes[0].IsVisible = nothingVisible || anyTemperature;
            YAxes[1].IsVisible = nothingVisible || anyHumidity;
        }

        /// <summary>全选：一次性勾上所有曲线。</summary>
        [RelayCommand]
        private void ShowAll() => SetAll(visible: true);

        /// <summary>全不选：清空图表，便于逐条勾选对比。</summary>
        [RelayCommand]
        private void HideAll() => SetAll(visible: false);

        private void SetAll(bool visible)
        {
            _suspendSync = true;
            try
            {
                foreach (var curve in Curves) curve.IsVisible = visible;
            }
            finally
            {
                _suspendSync = false;
            }

            SyncSeries();
        }

        public void Dispose()
        {
            _store.historySampleAdded -= OnSampleAdded;
            foreach (var curve in Curves) curve.PropertyChanged -= OnCurveSwitchChanged;
        }

        /// <summary>
        /// 右侧列表里的一条曲线：名称 + 单位 + 配色 + 显示开关。
        /// 只描述"画不画"，不持有数据（数据仍在各曲线的 buffer 里，隐藏期间继续累积）。
        /// </summary>
        public sealed class CurveSwitch : ObservableObject
        {
            private bool _isVisible = true;

            public CurveSwitch(string name, string unit, SKColor color)
            {
                Name = name;
                Unit = unit;
                ColorHex = color.ToString();
                Brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
                Brush.Freeze();
            }

            public string Name { get; }
            public string Unit { get; }
            public string ColorHex { get; }

            /// <summary>列表前的色块，与曲线颜色一致（Freeze 后跨线程使用安全）。</summary>
            public SolidColorBrush Brush { get; }

            public bool IsVisible
            {
                get => _isVisible;
                set => SetProperty(ref _isVisible, value);
            }

            /// <summary>列表文案：带单位，同名的温度/湿度测点也能分清。</summary>
            public string Display => string.IsNullOrWhiteSpace(Unit) ? Name : $"{Name}（{Unit}）";
        }

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
