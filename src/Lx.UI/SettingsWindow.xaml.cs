using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Lx.Probe;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Lx.Probe.Resolve;
using Lx.Probe.Updates;
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

    public SettingsWindow(string installRoot)
    {
        InitializeComponent();
        _installRoot = installRoot;
        VersionText.Text = $"v{AppInfo.Version}";
        AboutVersionText.Text = $"版本 v{AppInfo.Version}";
        AboutRuntimeText.Text = RuntimeInformation.FrameworkDescription;
        AboutDataDirText.Text = installRoot;

        LaunchSettings settings = SettingsStore.Load(installRoot);
        MinBox.Text = settings.MinMemoryMb.ToString();
        MaxBox.Text = settings.MaxMemoryMb.ToString();
        JvmArgsBox.Text = settings.JvmArgs ?? string.Empty;
        WindowWidthBox.Text = settings.WindowWidth.ToString();
        WindowHeightBox.Text = settings.WindowHeight.ToString();
        FullscreenBox.IsChecked = settings.Fullscreen;
        ConcurrencyBox.Text = Math.Clamp(settings.DownloadConcurrency, 1, 16).ToString();

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
        if (!int.TryParse(MinBox.Text.Trim(), out int min) ||
            !int.TryParse(MaxBox.Text.Trim(), out int max))
        {
            HintText.Text = "内存要填数字（单位 MB）";
            return;
        }

        if (min < LaunchSettings.MinAllowedMb || max > LaunchSettings.MaxAllowedMb || min > max)
        {
            HintText.Text = $"内存范围不对：最小 {LaunchSettings.MinAllowedMb}，最大 {LaunchSettings.MaxAllowedMb}，且最小不能大于最大";
            return;
        }

        if (!int.TryParse(WindowWidthBox.Text.Trim(), out int width) ||
            !int.TryParse(WindowHeightBox.Text.Trim(), out int height) ||
            width < LaunchSettings.MinWindowWidth || height < LaunchSettings.MinWindowHeight ||
            width > LaunchSettings.MaxWindowSize || height > LaunchSettings.MaxWindowSize)
        {
            HintText.Text = $"窗口大小不对：宽 ≥ {LaunchSettings.MinWindowWidth}，高 ≥ {LaunchSettings.MinWindowHeight}，最大 {LaunchSettings.MaxWindowSize}";
            return;
        }

        if (!int.TryParse(ConcurrencyBox.Text.Trim(), out int concurrency) || concurrency < 1 || concurrency > 16)
        {
            HintText.Text = "并发数要填 1 ~ 16";
            return;
        }

        string sourceKey = SelectedSourceKey();
        string? customBase = null;
        if (sourceKey == DownloadSourceSettings.Custom)
        {
            customBase = DownloadUrlRewriter.NormalizeBase(CustomSourceBox.Text);
            if (customBase.Length == 0)
            {
                HintText.Text = "自定义下载源要填 http/https 开头的地址";
                return;
            }
        }

        string proxyMode = SelectedProxyKey();
        string? proxyUrl = null;
        if (proxyMode == "manual")
        {
            proxyUrl = ProxyUrlBox.Text.Trim();
            if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out Uri? proxyUri) ||
                (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps))
            {
                HintText.Text = "手动代理要填 http:// 或 https:// 地址";
                return;
            }
        }

        JavaChoice? java = JavaBox.SelectedItem as JavaChoice;
        string channel = (UpdateChannelBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "stable";
        string theme = ThemeBox.SelectedIndex == 1 ? "dark" : "light";

        SettingsStore.Save(_installRoot, new LaunchSettings(
            MinMemoryMb: min,
            MaxMemoryMb: max,
            JavaPath: java?.Path,
            DownloadSource: sourceKey,
            CustomDownloadBase: customBase,
            JvmArgs: string.IsNullOrWhiteSpace(JvmArgsBox.Text) ? null : JvmArgsBox.Text.Trim(),
            WindowWidth: width,
            WindowHeight: height,
            Fullscreen: FullscreenBox.IsChecked == true,
            DownloadConcurrency: concurrency,
            ProxyMode: proxyMode,
            ProxyUrl: proxyUrl,
            UpdateChannel: channel,
            Theme: theme));

        Downloader.ConfigureProxy(proxyMode, proxyUrl);
        ThemeManager.Apply(theme);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
