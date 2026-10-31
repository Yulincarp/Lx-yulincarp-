using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Lx.Probe;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Lx.Probe.Resolve;
using Lx.Probe.Updates;
using Lx.Probe.ViewModels;
using Microsoft.Win32;

namespace Lx.UI;

public partial class SettingsWindow : Window
{
    private sealed record JavaChoice(string? Path, string Display);

    private sealed record SourceChoice(string Key, string Display);

    private sealed record ProxyChoice(string Key, string Display);

    private static readonly SourceChoice[] Sources =
    {
        new(DownloadSourceSettings.Official, "官方（Mojang）"),
        new(DownloadSourceSettings.Bmclapi, "BMCLAPI 镜像（国内推荐）"),
        new(DownloadSourceSettings.Custom, "自定义（兼容 BMCLAPI 路径）"),
    };

    private static readonly ProxyChoice[] Proxies =
    {
        new("system", "系统代理（Windows 设置）"),
        new("none", "直连（不使用代理）"),
        new("manual", "手动 HTTP 代理"),
    };

    private readonly string _installRoot;
    private readonly SettingsViewModel _vm;

    public SettingsWindow(string installRoot)
    {
        InitializeComponent();
        _installRoot = installRoot;
        VersionText.Text = $"v{AppInfo.Version}";
        AboutVersionText.Text = $"版本 v{AppInfo.Version}";
        AboutRuntimeText.Text = RuntimeInformation.FrameworkDescription;
        AboutDataDirText.Text = installRoot;

        LaunchSettings settings = SettingsStore.Load(installRoot);
        _vm = SettingsViewModel.From(settings);
        MinBox.Text = _vm.MinMemoryMb;
        MaxBox.Text = _vm.MaxMemoryMb;
        JvmArgsBox.Text = _vm.JvmArgs;
        WindowWidthBox.Text = _vm.WindowWidth;
        WindowHeightBox.Text = _vm.WindowHeight;
        FullscreenBox.IsChecked = _vm.Fullscreen;
        ConcurrencyBox.Text = _vm.DownloadConcurrency;

        var choices = new List<JavaChoice>
        {
            new(null, "自动（按版本选择，缺失时自动下载）"),
        };

        foreach ((string path, int? major) in JavaRuntime.Discover())
        {
            choices.Add(new JavaChoice(path, $"Java {major?.ToString() ?? "?"} — {path}"));
        }

        JavaBox.ItemsSource = choices;
        JavaBox.DisplayMemberPath = "Display";
        JavaBox.SelectedItem = choices.FirstOrDefault(choice =>
            string.Equals(choice.Path, settings.JavaPath, StringComparison.OrdinalIgnoreCase)) ?? choices[0];

        SourceBox.ItemsSource = Sources;
        SourceBox.DisplayMemberPath = "Display";
        string sourceKey = settings.DownloadSource ?? DownloadSourceSettings.Official;
        SourceBox.SelectedItem = Sources.FirstOrDefault(s => s.Key == sourceKey) ?? Sources[0];
        CustomSourceBox.Text = settings.CustomDownloadBase ?? string.Empty;
        CustomSourceBox.IsEnabled = SelectedSourceKey() == DownloadSourceSettings.Custom;

        ProxyModeBox.ItemsSource = Proxies;
        ProxyModeBox.DisplayMemberPath = "Display";
        string proxyKey = settings.ProxyMode ?? "system";
        ProxyModeBox.SelectedItem = Proxies.FirstOrDefault(p => p.Key == proxyKey) ?? Proxies[0];
        ProxyUrlBox.Text = settings.ProxyUrl ?? string.Empty;
        ProxyUrlBox.IsEnabled = SelectedProxyKey() == "manual";

        string channel = settings.UpdateChannel ?? "stable";
        UpdateChannelBox.SelectedItem = UpdateChannelBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, channel, StringComparison.OrdinalIgnoreCase))
            ?? UpdateChannelBox.Items[0];
        ThemeBox.SelectedIndex = string.Equals(settings.Theme, "dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private string SelectedSourceKey() =>
        (SourceBox.SelectedItem as SourceChoice)?.Key ?? DownloadSourceSettings.Official;

    private string SelectedProxyKey() =>
        (ProxyModeBox.SelectedItem as ProxyChoice)?.Key ?? "system";

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomSourceBox is null)
        {
            return;
        }

        CustomSourceBox.IsEnabled = SelectedSourceKey() == DownloadSourceSettings.Custom;
    }

    private void Proxy_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProxyUrlBox is null)
        {
            return;
        }

        ProxyUrlBox.IsEnabled = SelectedProxyKey() == "manual";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!UpdateService.CanSelfUpdate)
        {
            HintText.Text = "开发版不支持自更新；请用 publish.ps1 生成的发布版测试。";
            return;
        }

        string? manifestUrl = UpdateService.ResolveManifestUrl(_installRoot);
        if (manifestUrl is null)
        {
            HintText.Text = "未配置更新地址：设置环境变量 LX_UPDATE_URL，或把 update.json 放到数据目录。";
            return;
        }

        UpdateButton.IsEnabled = false;
        try
        {
            HintText.Text = "正在检查更新…";
            UpdateInfo? info = await UpdateService.CheckAsync(manifestUrl);
            if (info is null)
            {
                HintText.Text = $"已是最新版本（v{AppInfo.Version}）";
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                $"发现新版本 v{info.Version}（当前 v{AppInfo.Version}）。{Environment.NewLine}{Environment.NewLine}现在下载并更新吗？",
                "检查更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                HintText.Text = "已跳过更新";
                return;
            }

            var progress = new Progress<double>(p => HintText.Text = $"下载中 {p:P0}");
            StagedUpdate staged = await UpdateService.DownloadAsync(info, _installRoot, progress);

            MessageBoxResult apply = MessageBox.Show(
                "更新包已下载并通过校验。现在重启启动器并安装吗？",
                "更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (apply != MessageBoxResult.Yes)
            {
                HintText.Text = "更新包已保留，可稍后再装";
                return;
            }

            UpdateService.StartUpdater(staged, AppContext.BaseDirectory);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            HintText.Text = "更新失败：" + ex.Message;
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "java.exe|java.exe|所有文件|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var choices = (List<JavaChoice>)JavaBox.ItemsSource;
        var choice = new JavaChoice(dialog.FileName, $"自定义 — {dialog.FileName}");
        choices.Insert(1, choice);
        JavaBox.ItemsSource = null;
        JavaBox.ItemsSource = choices;
        JavaBox.SelectedItem = choice;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 控件值 -> ViewModel，校验和构造都在 ViewModel 里（可单测）
        _vm.MinMemoryMb = MinBox.Text;
        _vm.MaxMemoryMb = MaxBox.Text;
        _vm.JvmArgs = JvmArgsBox.Text;
        _vm.WindowWidth = WindowWidthBox.Text;
        _vm.WindowHeight = WindowHeightBox.Text;
        _vm.Fullscreen = FullscreenBox.IsChecked == true;
        _vm.DownloadConcurrency = ConcurrencyBox.Text;
        _vm.JavaPath = (JavaBox.SelectedItem as JavaChoice)?.Path;
        _vm.DownloadSource = SelectedSourceKey();
        _vm.CustomDownloadBase = CustomSourceBox.Text;
        _vm.ProxyMode = SelectedProxyKey();
        _vm.ProxyUrl = ProxyUrlBox.Text;
        _vm.UpdateChannel = (UpdateChannelBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "stable";
        _vm.DarkTheme = ThemeBox.SelectedIndex == 1;

        if (!_vm.TryBuild(out LaunchSettings? settings, out string error) || settings is null)
        {
            HintText.Text = error;
            return;
        }

        SettingsStore.Save(_installRoot, settings);
        Downloader.ConfigureProxy(settings.ProxyMode, settings.ProxyUrl);
        ThemeManager.Apply(settings.Theme);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
