using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Avalonia;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Helper;
using ServiceLib.Manager;
using ServiceLib.Models.Entities;
using ServiceLib.Models.Dto;
using ServiceLib.Services;
using ServiceLib.ViewModels;
using v2rayN.Desktop.Views;

namespace Palette.UiSmoke;

public sealed class SmokeApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        // This harness never instantiates the production App/MainWindow/CoreManager.
        // Its database stays beside the harness, separate from the installed VPN.
        Environment.SetEnvironmentVariable(Global.LocalAppData, "0");
        Check(Utils.StartupPath().StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase), "isolated data path");
        // Only reset named fixture files under this harness's verified output directory.
        foreach (var name in new[] { "guiNConfig.json", "guiNDB.db", "guiNDB.db-wal", "guiNDB.db-shm",
            "palette-favorites.db", "palette-favorites.db-wal", "palette-favorites.db-shm" })
            File.Delete(Path.Combine(AppContext.BaseDirectory, "guiConfigs", name));
        // Production also initializes SQLite before installing the UI synchronization context.
        Check(AppManager.Instance.InitApp(), "initialize isolated database");
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<SmokeApp>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseReactiveUI(_ => { }).SetupWithLifetime(lifetime);
        using var done = new CancellationTokenSource();
        var exitCode = 0;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await RunAsync(lifetime); Console.WriteLine("PASS: favorites UI and subscription lifecycle; no proxy core started."); }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { done.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        return exitCode;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    private static async Task RunAsync(ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var config = AppManager.Instance.Config;
        config.GuiItem.EnableLog = false;
        config.UiItem.EnableDragDropSort = true;
        config.UiItem.DoubleClick2Activate = true;
        var sub = new SubItem { Id = Guid.NewGuid().ToString("N"), Remarks = "测试节点", Memo = "firefly-worker:smoke-" + Guid.NewGuid().ToString("N") };
        await SQLiteHelper.Instance.InsertAsync(sub);
        var original = new ProfileItem { IndexId = Guid.NewGuid().ToString("N"), Subid = sub.Id,
            Remarks = "美国1高负载临时", Address = "192.0.2.1", Port = 12246, ConfigType = EConfigType.Hysteria2,
            Password = "synthetic-credential", Network = "tcp" };
        var second = JsonUtils.DeepCopy(original); second.IndexId = Guid.NewGuid().ToString("N"); second.Port = 12798;
        second.Remarks = "美国1中负载临时";
        await SQLiteHelper.Instance.InsertAllAsync(new[] { original, second });
        config.SubIndexId = sub.Id;
        config.IndexId = original.IndexId;
        var vm = new ProfilesViewModel();
        await vm.EnsureInitializedAsync();
        var reloadCount = 0;
        using var reloadSubscription = vm.ReloadRequested.AsObservable().Subscribe(_ => reloadCount++);
        using var refreshSubscription = vm.RefreshServersRequested.AsObservable().Subscribe(async _ => await vm.RefreshServersBiz());
        var view = new ProfilesView { ViewModel = vm, DataContext = vm };
        var window = new Window { Title = "FireflyVPN-Palette · 收藏测试", Width = 800, Height = 220,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Content = view };
        lifetime.MainWindow = window;
        window.Show();
        await vm.RefreshServersBiz();
        await SettleAsync();
        var grid = view.FindControl<DataGrid>("lstProfiles")!;
        foreach (var column in grid.Columns) column.IsVisible = column.Tag?.ToString() is "Remarks" or "DelayVal";

        Button Star() => grid.GetVisualDescendants().OfType<Button>().First(b =>
            b.Bounds.Width > 0 && b.DataContext is ProfileItemModel p && p.IndexId == original.IndexId && b.Content?.ToString() is "☆" or "★");
        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        }

        await SettleAsync();
        Click(Star());
        await SettleAsync();
        await WaitAsync(() => vm.ProfileItems.Any(p => p.IndexId == original.IndexId && p.IsFavorite));
        Check(reloadCount == 0 && config.IndexId == original.IndexId, "star click does not connect or drag the row");
        var backupStar = grid.GetVisualDescendants().OfType<Button>().First(b => b.Bounds.Width > 0 &&
            b.DataContext is ProfileItemModel p && p.IndexId == second.IndexId && b.Content?.ToString() == "☆");
        Click(backupStar);
        await WaitAsync(() => vm.ProfileItems.Any(p => p.IndexId == second.IndexId && p.IsFavorite));
        await SettleAsync();
        Check(reloadCount == 0 && config.IndexId == original.IndexId, "starring another node keeps the current connection");
        backupStar = grid.GetVisualDescendants().OfType<Button>().First(b => b.Bounds.Width > 0 &&
            b.DataContext is ProfileItemModel p && p.IndexId == second.IndexId && b.Content?.ToString() == "★");
        vm.SelectedProfile = vm.ProfileItems.Single(p => p.IndexId == second.IndexId);
        backupStar.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await SettleAsync();
        Check(reloadCount == 0 && config.IndexId == original.IndexId, "Enter on a star never activates its node");
        var backupRow = vm.ProfileItems.Single(p => p.IndexId == second.IndexId);
        if (backupRow.IsFavorite) { await vm.ToggleFavoriteAsync(backupRow); await SettleAsync(); }
        var favorite = vm.ProfileItems.Single(p => p.IndexId == original.IndexId);
        vm.SelectedProfile = favorite;
        using var aliasExecution = vm.EditFavoriteAliasCmd.Execute().Subscribe(_ => { });
        await WaitAsync(() => window.OwnedWindows.Count > 0);
        var dialog = window.OwnedWindows.Last();
        var input = dialog.GetVisualDescendants().OfType<TextBox>().Single();
        input.Text = "家里主用";
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "保存")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => vm.ProfileItems.Any(p => p.FavoriteAlias == "家里主用"));
        Check(config.IndexId == original.IndexId && reloadCount == 0, "editing alias keeps active connection");

        var toggle = view.FindControl<ToggleButton>("btnFavoritesOnly")!;
        Click(toggle);
        await WaitAsync(() => vm.FavoritesOnly && vm.ProfileItems.Count == 1);
        Check(config.IndexId == original.IndexId && reloadCount == 0, "favorites filter does not change connection");
        await SettleAsync();
        var text = grid.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).ToList();
        var alias = text.First(t => t.Text == "家里主用");
        var name = text.First(t => t.Text == original.Remarks);
        var aliasPosition = alias.TranslatePoint(default, window)!.Value;
        var namePosition = name.TranslatePoint(default, window)!.Value;
        Check(namePosition.X > aliasPosition.X && Math.Abs(aliasPosition.Y - namePosition.Y) < 8,
            "original name follows alias on one line");
        Check(name.FontSize < alias.FontSize && name.Opacity < 1, "original name uses smaller subdued text");
        var artifactDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/favorites"));
        Directory.CreateDirectory(artifactDir);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(artifactDir, "favorites-wide.png"), new PngBitmapEncoderOptions());
        window.Width = 480;
        await SettleAsync();
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(artifactDir, "favorites-narrow.png"), new PngBitmapEncoderOptions());

        // Import a same-name replacement with a different port and authentication.
        var newId = "e9d9ac9a-ae0f-4ad0-9bdb-c8432e110f54";
        var count = await ConfigHandler.AddBatchServers(config,
            $"vless://{newId}@192.0.2.1:12798?encryption=none#{Uri.EscapeDataString(original.Remarks)}", sub.Id, true);
        Check(count == 1, "import changed subscription");
        await vm.RefreshServersBiz();
        Check(vm.ProfileItems.Single().FavoriteStatus.IsNotEmpty(), "same-name replacement requires review");
        Check((await ConfigHandler.EnsureDefaultServer(config))!.Port == original.Port,
            "subscription update retains the approved active configuration");
        var beforeFailedImport = (await AppManager.Instance.ProfileItems(sub.Id))!.Single();
        await ConfigHandler.AddBatchServers(config, "this is not a subscription", sub.Id, true);
        Check((await AppManager.Instance.ProfileItems(sub.Id))!.Single().IndexId == beforeFailedImport.IndexId,
            "failed subscription import restores the previous list");
        vm.SelectedProfile = vm.ProfileItems.Single();
        using var reviewExecution = vm.ReviewFavoriteCmd.Execute().Subscribe(_ => { });
        await WaitAsync(() => window.OwnedWindows.Count > 0);
        var reviewDialog = window.OwnedWindows.Last();
        reviewDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "确认关联")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitAsync(() => vm.ProfileItems.Single().IndexId == beforeFailedImport.IndexId);
        Check(vm.ProfileItems.Single().FavoriteAlias == "家里主用", "review preserves the custom alias");
        Check((await ConfigHandler.EnsureDefaultServer(config))!.Port == original.Port,
            "accepting a bookmark replacement does not switch the running connection");
        Check(reloadCount == 0, "no connection requests during bookmark operations");
        window.Close();
    }

    private static async Task SettleAsync()
    {
        await Task.Delay(120);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(30);
        Check(condition(), "async UI operation completes");
    }
}
