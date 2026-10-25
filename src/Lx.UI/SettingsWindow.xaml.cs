using System.Windows;
using System.Windows.Controls;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Lx.Probe.Resolve;
using Microsoft.Win32;

namespace Lx.UI;

public partial class SettingsWindow : Window
{
    private sealed record JavaChoice(string? Path, string Display);

    private sealed record SourceChoice(string Key, string Display);

    private static readonly SourceChoice[] Sources =
    {
        new(DownloadSourceSettings.Official, "官方（Mojang）"),
        new(DownloadSourceSettings.Bmclapi, "BMCLAPI 镜像（国内推荐）"),
        new(DownloadSourceSettings.Custom, "自定义（兼容 BMCLAPI 路径）"),
    };

    private readonly string _installRoot;

    public SettingsWindow(string installRoot)
    {
        InitializeComponent();
        _installRoot = installRoot;

        LaunchSettings settings = SettingsStore.Load(installRoot);
        MinBox.Text = settings.MinMemoryMb.ToString();
        MaxBox.Text = settings.MaxMemoryMb.ToString();

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
    }

    private string SelectedSourceKey() =>
        (SourceBox.SelectedItem as SourceChoice)?.Key ?? DownloadSourceSettings.Official;

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CustomSourceBox is null)
        {
            return;
        }

        CustomSourceBox.IsEnabled = SelectedSourceKey() == DownloadSourceSettings.Custom;
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

        JavaChoice? java = JavaBox.SelectedItem as JavaChoice;
        SettingsStore.Save(_installRoot, new LaunchSettings(
            MinMemoryMb: min,
            MaxMemoryMb: max,
            JavaPath: java?.Path,
            DownloadSource: sourceKey,
            CustomDownloadBase: customBase));

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
