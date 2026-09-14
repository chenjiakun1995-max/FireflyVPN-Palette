using System.Windows.Controls;

namespace v2rayN.Views;

internal static class FavoriteDialogs
{
    public static string? EditAlias(string alias)
    {
        var input = new TextBox { Text = alias, MaxLength = 80, MinWidth = 430 };
        return Show("编辑收藏备注", "备注最多 80 个字；清空后恢复显示原始名称。", input,
            () => input.Text, "保存");
    }

    public static string? Review(FavoriteReviewRequest request)
    {
        var choices = new ComboBox { ItemsSource = request.Candidates, DisplayMemberPath = nameof(FavoriteCandidate.DisplayText),
            SelectedIndex = 0, MinWidth = 430 };
        return Show("更新收藏：" + request.Title,
            "原配置已找不到。选择要关联的新配置，确认后保留你的备注。此操作只更新收藏，双击节点才会连接。",
            choices, () => (choices.SelectedItem as FavoriteCandidate)?.IndexId, "确认关联");
    }

    private static string? Show(string title, string description, Control input, Func<string?> value, string acceptText)
    {
        string? result = null;
        var window = new Window { Title = title, Width = 520, ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height, Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 70, Margin = new Thickness(0, 0, 10, 0) };
        var save = new Button { Content = acceptText, IsDefault = true, MinWidth = 70 };
        save.Click += (_, _) => { result = value(); window.DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
        window.Content = panel;
        window.Loaded += (_, _) => { input.Focus(); if (input is TextBox text) text.SelectAll(); };
        return window.ShowDialog() == true ? result : null;
    }
}
