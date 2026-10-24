using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lx.Probe.Auth;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Lx.Probe.Mods;
using Lx.Probe.Resolve;
using Microsoft.Win32;

namespace Lx.UI;

public partial class MainWindow : Window
{
    // 从 bin 目录往上找 Lx.sln，把 minecraft 定位到仓库根，避免"从哪个目录启动"影响结果。
    private readonly string _installRoot = ResolveInstallRoot();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private static string ResolveInstallRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lx.sln")))
            {
                return Path.Combine(dir.FullName, "minecraft");
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath("minecraft");
    }

    private async Task InitializeAsync()
    {
        try
        {
            VersionManifest manifest = await ManifestService.EnsureManifestAsync(_installRoot);
            List<ManifestVersion> versions = manifest.Versions ?? new List<ManifestVersion>();
            VersionBox.ItemsSource = versions;
            VersionBox.DisplayMemberPath = "Id";
            VersionBox.SelectedItem = versions.FirstOrDefault(v => v.Id == "1.20.4") ?? versions.FirstOrDefault();

            RefreshAccounts();
            RefreshMods();

            Log($"安装目录：{_installRoot}");
            Log($"版本清单：{versions.Count} 个");
        }
        catch (Exception ex)
        {
            Log($"[错误] {ex.Message}");
        }
    }

    private void RefreshAccounts()
    {
        List<Account> accounts = AccountStore.Load(_installRoot);
        AccountBox.ItemsSource = accounts;
        if (accounts.Count > 0)
        {
            AccountBox.SelectedIndex = 0;
        }
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            Log("先输入一个离线名字");
            return;
        }

        Account account = OfflineAuth.Create(name);
        AccountStore.Save(_installRoot, account);
        RefreshAccounts();
        Log($"已添加离线账号：{name}（{account.Uuid}）");
    }

    private void Version_Changed(object sender, RoutedEventArgs e) => RefreshMods();

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => RefreshMods();

    private void RefreshMods()
    {
        if (ModsList is null)
        {
            return;
        }

        try
        {
            string gameDir = CurrentGameDir();
            ModsPathText.Text = ModManager.ModsDir(gameDir);
            ModsList.ItemsSource = ModManager.List(gameDir);
        }
        catch (Exception ex)
        {
            Log($"[错误] {ex.Message}");
        }
    }

    // 当前版本的游戏目录：Fabric 勾上时优先找已装好的 fabric-loader-*-<版本>。
    private string CurrentGameDir()
    {
        string version = (VersionBox.SelectedItem as ManifestVersion)?.Id ?? "1.20.4";
        string versionsDir = Path.Combine(_installRoot, "versions");

        if (FabricCheck.IsChecked == true && Directory.Exists(versionsDir))
        {
            string? fabric = Directory
                .EnumerateDirectories(versionsDir, $"fabric-loader-*-{version}")
                .FirstOrDefault();
            if (fabric is not null)
            {
                return fabric;
            }
        }

        return Path.Combine(versionsDir, version);
    }

    private void ModCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not ModFile mod)
        {
            return;
        }

        ModManager.SetEnabled(CurrentGameDir(), mod.FileName, box.IsChecked == true);
        RefreshMods();
    }

    private void AddMod_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Fabric mod (*.jar)|*.jar",
            Multiselect = true,
            Title = "选择 mod jar",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string gameDir = CurrentGameDir();
        Directory.CreateDirectory(ModManager.ModsDir(gameDir));

        foreach (string file in dialog.FileNames)
        {
            try
            {
                ModManager.Add(gameDir, file);
                Log($"已添加 mod：{Path.GetFileName(file)}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                Log($"[错误] {ex.Message}");
            }
        }

        RefreshMods();
    }

    private void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (ModsList.SelectedItem is not ModFile mod)
        {
            Log("先在列表里选中一个 mod");
            return;
        }

        if (ModManager.Remove(CurrentGameDir(), mod.FileName))
        {
            Log($"已删除 mod：{mod.FileName}");
            RefreshMods();
        }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (AccountBox.SelectedItem is not Account account)
        {
            Log("先选一个账号（或添加一个离线账号）");
            return;
        }

        if (VersionBox.SelectedItem is not ManifestVersion version || version.Id is null)
        {
            Log("先选一个版本");
            return;
        }

        LaunchButton.IsEnabled = false;
        StatusText.Text = "准备中…";
        try
        {
            var options = new LaunchOptions(
                _installRoot,
                version.Id,
                FabricCheck.IsChecked == true ? "fabric" : null,
                null,
                null,
                account);

            LaunchResult result = await LaunchPipeline.RunAsync(
                options,
                message => Dispatcher.Invoke(() => Log(message)));

            Log($"命令行：{result.CommandLine.Length} 字符");
            StartGame(result.CommandLine);
            StatusText.Text = "已启动";
            RefreshMods();
        }
        catch (Exception ex)
        {
            Log($"[错误] {ex.Message}");
            StatusText.Text = "失败";
        }
        finally
        {
            LaunchButton.IsEnabled = true;
        }
    }

    private void StartGame(string commandLine)
    {
        var info = new ProcessStartInfo("java")
        {
            Arguments = commandLine["java ".Length..],
            UseShellExecute = false,
        };

        Process? process = Process.Start(info);
        Log(process is null ? "启动失败：进程没有创建" : $"已启动 java（PID {process.Id}）");
        process?.Dispose();
    }

    private void Log(string message)
    {
        LogBox.AppendText(message + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
