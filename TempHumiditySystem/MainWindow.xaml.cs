using System.ComponentModel;
using System.Windows;
using TempHumiditySystem.ViewModels;

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

        protected override void OnClosing(CancelEventArgs e)
        {
            // 参数配置页有未保存修改时，交给 VM 弹确认框
            if (DataContext is MainWindowViewModel vm && !vm.CanClose())
                e.Cancel = true;

            base.OnClosing(e);
        }
    }
}
