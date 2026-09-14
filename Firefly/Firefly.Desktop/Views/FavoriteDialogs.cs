using Avalonia.Layout;
using v2rayN.Desktop.Manager;

namespace v2rayN.Desktop.Views;

internal static class FavoriteDialogs
{
    public static Task<string?> EditAliasAsync(string alias)
    {
        var input = new TextBox { Text = alias, MaxLength = 80, PlaceholderText = "例如：家里主用" };
        return ShowAsync("编辑收藏备注", "备注最多 80 个字；清空后恢复显示原始名称。", input,
            () => input.Text ?? string.Empty, "保存");
    }

    public static Task<string?> ReviewAsync(FavoriteReviewRequest request)
    {
        var choices = new ComboBox
        {
            ItemsSource = request.Candidates.Select(c => c.DisplayText).ToList(),
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        return ShowAsync("更新收藏：" + request.Title,
            "原配置已找不到。选择要关联的新配置，确认后保留你的备注。此操作只更新收藏，双击节点才会连接。",
            choices, () => choices.SelectedIndex >= 0 ? request.Candidates[choices.SelectedIndex].IndexId : null, "确认关联");
    }

    private static async Task<string?> ShowAsync(string title, string description, Control input,
        Func<string?> getValue, string acceptText)
    {
        var window = new Window
        {
            Title = title, Width = 520, CanResize = false, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        var save = new Button { Content = acceptText, IsDefault = true };
        var cancel = new Button { Content = "取消", IsCancel = true };
        save.Click += (_, _) => window.Close(getValue());
        cancel.Click += (_, _) => window.Close(null);
        window.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 16,
            Children =
            {
                new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap }, input,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10, Children = { cancel, save } }
            }
        };
        window.Opened += (_, _) => { input.Focus(); if (input is TextBox text) text.SelectAll(); };
        return await window.ShowDialog<string?>(WindowDialog.TryGetOwnerWindow());
    }
}
