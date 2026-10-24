using System.Windows;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Microsoft.Win32;

namespace Lx.UI;

public partial class SettingsWindow : Window
{
    private sealed record JavaChoice(string? Path, string Display);

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

        JavaChoice? java = JavaBox.SelectedItem as JavaChoice;
        SettingsStore.Save(_installRoot, new LaunchSettings(min, max, java?.Path));
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
