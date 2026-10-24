using System.Diagnostics;
using System.IO;
using System.Windows;
using Lx.Probe.Auth;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Lx.Probe.Resolve;

namespace Lx.UI;

public partial class MainWindow : Window
{
    // 从 bin 目录往上找 Lx.sln，把 minecraft 定位到仓库根，避免"从哪个目录启动"影响结果。
    private readonly string _installRoot = ResolveInstallRoot();

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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
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
