using System.Windows.Controls;
using System.Windows.Input;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using TempHumiditySystem.ViewModels;

namespace TempHumiditySystem.Views
{
    public partial class TrendView : UserControl
    {
        public TrendView()
        {
            InitializeComponent();

            // 悬浮气泡的文字默认用 Skia 的默认字体，中文（测点名）会渲染成方块；
            // 这里换成与坐标轴相同的中文字体，并给气泡一个浅色底，保证任何背景下都看得清。
            var typeface = SKFontManager.Default.MatchCharacter('汉') ?? SKTypeface.Default;

            Chart.TooltipTextPaint = new SolidColorPaint(SKColor.Parse("#111827")) { SKTypeface = typeface };
            Chart.TooltipTextSize = 12;
            Chart.TooltipBackgroundPaint = new SolidColorPaint(SKColor.Parse("#FFFFFF").WithAlpha(242));

            Chart.MouseMove += OnChartMouseMove;
        }

        /// <summary>
        /// 在图上按住左键拖动 = 平移查看历史。平移会改掉横轴范围，而"跟随最新"每条采样都会把轴钉回去，
        /// 所以拖动一开始就主动关掉跟随（右侧「跟随最新数据」勾选框会同步取消），
        /// 松开后视图停在拖到的位置；重新勾选即回到最新。
        /// </summary>
        private void OnChartMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (DataContext is TrendViewModel { IsFollowing: true } trend) trend.IsFollowing = false;
        }
    }
}
