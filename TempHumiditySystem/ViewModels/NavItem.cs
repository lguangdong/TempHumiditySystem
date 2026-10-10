namespace TempHumiditySystem.ViewModels
{
    /// <summary>
    /// 导航菜单项：标题 + 对应的页面 ViewModel。
    /// 点击菜单时，把 PageViewModel 赋给 CurrentViewModel，ContentControl 自动匹配 View。
    /// </summary>
    public sealed class NavItem
    {
        public string Title { get; init; } = "";
        public object? PageViewModel { get; init; }
    }
}
