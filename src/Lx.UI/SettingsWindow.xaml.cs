using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Lx.Probe;
using Lx.Probe.Launch;
using Lx.Probe.Localization;
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
        new(DownloadSourceSettings.Official, L10n.T("S187")),
        new(DownloadSourceSettings.Bmclapi, L10n.T("S188")),
        new(DownloadSourceSettings.Custom, L10n.T("S189")),
    };

    private static readonly ProxyChoice[] Proxies =
    {
        new("system", L10n.T("S190")),
        new("none", L10n.T("S191")),
        new("manual", L10n.T("S192")),
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
        TelemetryBox.IsChecked = _vm.TelemetryEnabled;
        CrashUrlBox.Text = _vm.CrashReportUrl;

        var choices = new List<JavaChoice>
        {
            new(null, L10n.T("S186")),
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
        ThemeBox.SelectedItem = ThemeBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, _vm.Theme, StringComparison.OrdinalIgnoreCase))
            ?? ThemeBox.Items[0];

        LanguageBox.SelectedItem = LanguageBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, _vm.Language, StringComparison.OrdinalIgnoreCase))
            ?? LanguageBox.Items[0];
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
            HintText.Text = L10n.T("S210");
            return;
        }

        string? manifestUrl = UpdateService.ResolveManifestUrl(_installRoot);
        if (manifestUrl is null)
        {
            HintText.Text = L10n.T("S222");
            return;
        }

        UpdateButton.IsEnabled = false;
        try
        {
            HintText.Text = L10n.T("S233");
            UpdateInfo? info = await UpdateService.CheckAsync(manifestUrl);
            if (info is null)
            {
                HintText.Text = L10n.T("S257", AppInfo.Version);
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                L10n.T("S319", info.Version, AppInfo.Version),
                L10n.T("S309"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                HintText.Text = L10n.T("S231");
                return;
            }

            var progress = new Progress<double>(p => HintText.Text = L10n.T("S249", p));

            // 有 files.json 就先试差量：只下变化的文件，失败再退回整包。
            StagedUpdate staged;
            if (!string.IsNullOrWhiteSpace(info.FilesUrl))
            {
                try
                {
                    DeltaPlan plan = await UpdateService.PlanDeltaAsync(info.FilesUrl, AppContext.BaseDirectory);
                    if (plan.Download.Count == 0)
                    {
                        HintText.Text = L10n.T("S257", AppInfo.Version);
                        return;
                    }

                    staged = await UpdateService.DownloadDeltaAsync(info, AppContext.BaseDirectory, plan, progress);
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException
                                             or InvalidOperationException
                                             or System.IO.IOException)
                {
                    HintText.Text = L10n.T("S233");
                    staged = await UpdateService.DownloadAsync(info, _installRoot, progress);
                }
            }
            else
            {
                staged = await UpdateService.DownloadAsync(info, _installRoot, progress);
            }

            MessageBoxResult apply = MessageBox.Show(
                L10n.T("S294"),
                L10n.T("S303"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (apply != MessageBoxResult.Yes)
            {
                HintText.Text = L10n.T("S206");
                return;
            }

            UpdateService.StartUpdater(staged, AppContext.BaseDirectory);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            HintText.Text = L10n.T("S207") + ex.Message;
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
        _vm.Theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "light";
        _vm.Language = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "zh-CN";
        _vm.TelemetryEnabled = TelemetryBox.IsChecked == true;
        _vm.CrashReportUrl = CrashUrlBox.Text;

        if (!_vm.TryBuild(out LaunchSettings? settings, out string error) || settings is null)
        {
            HintText.Text = error;
            return;
        }

        SettingsStore.Save(_installRoot, settings);
        Lx.Probe.Localization.L10n.SetLanguage(settings.Language);
        Downloader.ConfigureProxy(settings.ProxyMode, settings.ProxyUrl);
        ThemeManager.Apply(settings.Theme);
        DialogResult = true;
        Close();
    }

    private void Privacy_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, Lx.Probe.Diagnostics.CrashReporter.PrivacyNotice, L10n.T("S295"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
