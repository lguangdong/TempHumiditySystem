using System.Windows;

namespace TempHumiditySystem
{
    /// <summary>
    /// 主窗口。DataContext 由 App.OnStartup 组装引擎后注入，这里不再 new。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
