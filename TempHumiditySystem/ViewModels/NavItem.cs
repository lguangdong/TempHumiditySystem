using CommunityToolkit.Mvvm.ComponentModel;

namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 导航菜单项：标题 + 对应的页面 ViewModel + 选中态。
    /// 点击菜单时，把 PageViewModel 赋给 CurrentViewModel，ContentControl 自动匹配 View。
    /// </summary>
    public sealed partial class NavItem : ObservableObject
    {
        public string Title { get; init; } = "";
        public object? PageViewModel { get; init; }

        /// <summary>是否为当前选中项（供左侧菜单高亮显示）。</summary>
        [ObservableProperty]
        private bool _isSelected;
    }
}
