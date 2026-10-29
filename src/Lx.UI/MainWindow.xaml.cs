using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lx.Probe;
using Lx.Probe.Auth;
using Lx.Probe.Instances;
using Lx.Probe.Loaders;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Lx.Probe.Migration;
using Lx.Probe.Modpacks;
using Lx.Probe.Mods;
using Lx.Probe.Resolve;
using Lx.Probe.Remote;
using Lx.Probe.Updates;
using Lx.UI.ViewModels;
using Microsoft.Win32;
using LxLog = Lx.Probe.Diagnostics.Log;

namespace Lx.UI;

public partial class MainWindow : Window
{
    // 从 bin 目录往上找 Lx.sln，把 minecraft 定位到仓库根，避免"从哪个目录启动"影响结果。
    private readonly string _installRoot = AppPaths.ResolveInstallRoot();

    private readonly ConcurrentQueue<DownloadFailure> _downloadFailures = new();
    private readonly SettingsSummaryViewModel _settingsSummary = new();
    private readonly ResourceViewModel _resourceViewModel = new();
    private bool _suppressInstanceEvents;

    private Process? _gameProcess;
    private DateTime _gameStartedAt;
    private CancellationTokenSource? _launchCts;
    private DateTime _launchStartedAt;
    private bool _closing;
    private bool _modrinthBusy;
    private bool _updatingMods;
    private RemoteControlServer? _remoteServer;

    private sealed record LoaderChoice(string Version, string Display, bool Stable = false);

    private readonly Dictionary<string, List<LoaderChoice>> _loaderVersionCache = new();
    private int _loaderVersionRequest;
    private CancellationTokenSource? _loaderInstallCts;

    private bool IsFabricSelected => LoaderBox.SelectedIndex == 1;

    private bool IsForgeSelected => LoaderBox.SelectedIndex is 2 or 3;

    public MainWindow()
    {
        InitializeComponent();
        SettingsView.DataContext = _settingsSummary;
        ResourceView.DataContext = _resourceViewModel;
        SidebarVersionText.Text = $"v{AppInfo.Version}";
        Closing += MainWindow_Closing;

        if (Application.Current is { } app)
        {
            // 关机/注销时不弹"游戏还在运行"的确认，避免卡住系统。
            app.SessionEnding += (_, _) => _closing = true;
        }

        Loaded += async (_, _) =>
        {
            await InitializeAsync();
            _ = CheckForUpdatesAsync();
        };
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!UpdateService.CanSelfUpdate)
        {
            return;
        }

        string? manifestUrl = UpdateService.ResolveManifestUrl(_installRoot);
        if (manifestUrl is null)
        {
            return;
        }

        try
        {
            UpdateInfo? info = await UpdateService.CheckAsync(manifestUrl);
            if (info is not null)
            {
                Log($"[更新] 发现新版本 v{info.Version}（当前 v{AppInfo.Version}），可在「设置」里更新");
            }
        }
        catch (Exception ex)
        {
            Log($"[更新] 检查失败：{ex.Message}");
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            LaunchSettings settings = SettingsStore.Load(_installRoot);
            Downloader.ConfigureProxy(settings.ProxyMode, settings.ProxyUrl);
            var source = new DownloadSourceSettings(settings.DownloadSource, settings.CustomDownloadBase);
            Func<string, string> rewriteUrl = url => DownloadUrlRewriter.Rewrite(url, source);

            VersionManifest manifest = await ManifestService.EnsureManifestAsync(_installRoot, rewriteUrl);
            List<ManifestVersion> versions = manifest.Versions ?? new List<ManifestVersion>();

            // 把本地装好的（Forge/NeoForge/Fabric）也加进下拉框
            foreach (ManifestVersion local in ManifestService.LocalVersions(_installRoot))
            {
                if (versions.All(v => !string.Equals(v.Id, local.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    versions.Insert(0, local);
                }
            }
            VersionBox.ItemsSource = versions;
            VersionBox.DisplayMemberPath = "Id";
            VersionBox.SelectedItem = versions.FirstOrDefault(v => v.Id == "1.20.4") ?? versions.FirstOrDefault();

            RefreshAccounts();
            RefreshInstances();

            Log($"数据目录：{_installRoot}");
            Log($"下载源：{DownloadUrlRewriter.Describe(source)}");
            Log($"版本清单：{versions.Count} 个");
            Log($"日志：{LxLog.CurrentFile ?? LxLog.DirectoryPath}");
            RefreshSettingsSummary();
        }
        catch (Exception ex)
        {
            LogError("初始化失败", ex);
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

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginWindow(_installRoot) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            RefreshAccounts();
            AccountBox.SelectedItem = AccountStore.FindByName(_installRoot, dialog.Result.Name);
            Log($"已登录：{dialog.Result.Type} {dialog.Result.Name}（角色 {dialog.Result.AvailableRoles?.Count ?? 0} 个）");
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e) => ShowPage(HomeView);

    private void VersionPage_Click(object sender, RoutedEventArgs e) => ShowPage(VersionView);

    private void InstancePage_Click(object sender, RoutedEventArgs e) => ShowPage(InstanceView);

    private void ResourcePage_Click(object sender, RoutedEventArgs e) => ShowPage(ResourceView);

    private void AccountPage_Click(object sender, RoutedEventArgs e) => ShowPage(AccountView);

    private void SettingsPage_Click(object sender, RoutedEventArgs e)
    {
        RefreshSettingsSummary();
        ShowPage(SettingsView);
    }

    private void LogPage_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(LogView);
        LogBox.ScrollToEnd();
    }

    private void ShowPage(Grid page)
    {
        foreach (Grid view in new[] { HomeView, VersionView, InstanceView, ResourceView, AccountView, SettingsView, LogView })
        {
            view.Visibility = ReferenceEquals(view, page) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void HomeInstance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Instance instance })
        {
            InstancesList.SelectedItem = instance;
            ApplyInstance(instance);
        }
    }

    private void Remote_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteServer is { IsRunning: true })
        {
            MessageBox.Show("手机遥控窗口已经打开。", "手机遥控", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var server = new RemoteControlServer(
            new MainWindowRemoteHost(this),
            AppInfo.ProductName,
            AppInfo.Version);
        _remoteServer = server;
        try
        {
            var window = new RemoteWindow(server) { Owner = this };
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_remoteServer, server))
                {
                    _remoteServer = null;
                }
            };
            window.Show();
        }
        catch (Exception ex)
        {
            LogError("打开手机遥控失败", ex);
            _remoteServer = null;
            MessageBox.Show(this, ex.Message, "手机遥控", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Task<string> RemoteLaunchAsync(string instanceName) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (_gameProcess is { HasExited: false })
            {
                return "游戏已经在运行";
            }

            if (_launchCts is not null)
            {
                return "正在准备启动，请稍候";
            }

            Instance? instance = InstanceStore.Load(_installRoot)
                .FirstOrDefault(i => string.Equals(i.Name, instanceName, StringComparison.OrdinalIgnoreCase));
            if (instance is null)
            {
                return $"实例不存在：{instanceName}";
            }

            Instance? uiInstance = InstancesList.Items.OfType<Instance>()
                .FirstOrDefault(i => string.Equals(i.Name, instance.Name, StringComparison.OrdinalIgnoreCase));
            if (uiInstance is null)
            {
                RefreshInstances(instance.Name);
                uiInstance = InstancesList.Items.OfType<Instance>()
                    .FirstOrDefault(i => string.Equals(i.Name, instance.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (uiInstance is null)
            {
                return "实例列表里找不到该实例";
            }

            InstancesList.SelectedItem = uiInstance;
            ApplyInstance(uiInstance);
            Launch_Click(this, new RoutedEventArgs());
            return $"已请求启动：{instance.Name}";
        }).Task;

    private sealed class MainWindowRemoteHost : IRemoteHost
    {
        private readonly MainWindow _owner;

        public MainWindowRemoteHost(MainWindow owner)
        {
            _owner = owner;
        }

        public RemoteSnapshot Snapshot()
        {
            return _owner.Dispatcher.Invoke(() =>
            {
                List<RemoteInstanceInfo> instances = InstanceStore.Load(_owner._installRoot)
                    .Select(i => new RemoteInstanceInfo(i.Name, i.VersionId, i.Loader, i.LoaderVersion, i.AccountName))
                    .ToList();
                return new RemoteSnapshot(
                    _owner.StatusText?.Text ?? string.Empty,
                    _owner._gameProcess is { HasExited: false },
                    _owner._launchCts is not null,
                    instances);
            });
        }

        public string GetLogTail(int lines)
        {
            try
            {
                string logDir = Path.Combine(_owner._installRoot, "logs");
                if (!Directory.Exists(logDir))
                {
                    return string.Empty;
                }

                string? file = Directory.EnumerateFiles(logDir, "launcher-*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (file is null)
                {
                    return string.Empty;
                }

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var tail = new Queue<string>(Math.Max(lines, 1));
                while (reader.ReadLine() is { } line)
                {
                    tail.Enqueue(line);
                    if (tail.Count > lines)
                    {
                        tail.Dequeue();
                    }
                }

                return string.Join(Environment.NewLine, tail);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "日志读取失败：" + ex.Message;
            }
        }

        public Task<string> LaunchAsync(string instanceName) => _owner.RemoteLaunchAsync(instanceName);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_installRoot) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            LaunchSettings settings = SettingsStore.Load(_installRoot);
            var source = new DownloadSourceSettings(settings.DownloadSource, settings.CustomDownloadBase);
            Log($"设置已保存：内存 {settings.MinMemoryMb}-{settings.MaxMemoryMb} MB，Java {(settings.JavaPath ?? "自动")}，下载源 {DownloadUrlRewriter.Describe(source)}");
        }

        RefreshSettingsSummary();
    }

    private async void ChangeDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LX_DATA_DIR")))
        {
            MessageBox.Show(
                "当前启动器由 LX_DATA_DIR 环境变量指定数据目录，设置里的更改会被环境变量覆盖。\n\n请先移除 LX_DATA_DIR，再更改数据目录。",
                "更改数据目录",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_gameProcess is { } running && !running.HasExited)
        {
            Log("游戏运行中，先退出游戏再更改数据目录");
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择新的数据目录（必须为空目录）",
            InitialDirectory = Path.GetDirectoryName(_installRoot) ?? _installRoot,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string target = dialog.FolderName;
        MessageBoxResult confirm = MessageBox.Show(
            $"当前数据目录：{_installRoot}\n\n新数据目录：{target}\n\n" +
            "会完整复制所有数据（包括日志），旧目录保留作为备份，完成后重启生效。现在开始吗？",
            "更改数据目录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        StatusText.Text = "正在迁移数据目录…";
        var progress = new Progress<DataDirectoryMoveProgress>(p =>
        {
            StatusText.Text = $"迁移 {p.FilesCopied} 个文件 / {p.BytesCopied / 1024.0 / 1024.0:F1} MB";
        });

        try
        {
            DataDirectoryMoveProgress result = await DataDirectoryMigrator.MigrateAsync(
                _installRoot,
                target,
                progress);

            DataLocationStore.Write(target);
            _settingsSummary.SetDataDirectory(target);
            StatusText.Text = "数据目录已迁移，重启后生效";
            Log($"数据目录迁移完成：{_installRoot} → {target}（{result.FilesCopied} 个文件，{result.BytesCopied / 1024.0 / 1024.0:F1} MB）");

            MessageBoxResult restart = MessageBox.Show(
                $"迁移完成：{result.FilesCopied} 个文件，{result.BytesCopied / 1024.0 / 1024.0:F1} MB。\n\n" +
                "旧目录仍然保留。现在重启启动器以使用新目录吗？",
                "更改数据目录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (restart == MessageBoxResult.Yes)
            {
                RestartApplication();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "数据目录迁移失败";
            LogError("更改数据目录失败", ex);
        }
    }

    private void RestartApplication()
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("请手动关闭并重新打开启动器。", "更改数据目录",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 1 /nobreak >nul & start \"\" \"{exe}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        Application.Current.Shutdown();
    }

    private async void MigrateOldData_Click(object sender, RoutedEventArgs e)
    {
        if (_gameProcess is { } running && !running.HasExited)
        {
            Log("游戏运行中，先退出游戏再迁移数据");
            return;
        }

        string? guessed = DataMigrationService.GuessOldDataRoot();
        var dialog = new OpenFolderDialog
        {
            Title = "选择旧版 Lx 数据目录（minecraft 或 %APPDATA%\\Lx）",
            InitialDirectory = guessed ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string source = dialog.FolderName;
        if (!DataMigrationService.IsLikelyDataRoot(source))
        {
            MessageBox.Show(
                "这个目录不像 Lx 数据目录。\n\n应包含 accounts.json、versions、libraries 或 assets。",
                "数据迁移",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"把旧数据复制到：{_installRoot}\n\n源目录：{source}\n\n已存在的文件会跳过，不会覆盖。现在开始吗？",
            "从旧版导入数据",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        StatusText.Text = "正在迁移旧数据…";
        var progress = new Progress<DataMigrationProgress>(p =>
        {
            StatusText.Text = $"迁移 {p.FilesCopied} 个文件 / {p.BytesCopied / 1024.0 / 1024.0:F1} MB";
        });

        try
        {
            DataMigrationResult result = await DataMigrationService.MigrateAsync(
                source,
                _installRoot,
                overwrite: false,
                progress);

            string summary = $"迁移完成：复制 {result.FilesCopied} 个文件，跳过 {result.FilesSkipped} 个，共 {result.BytesCopied / 1024.0 / 1024.0:F1} MB。";
            Log(summary);
            foreach (string error in result.Errors.Take(8))
            {
                Log($"[迁移失败] {error}");
            }

            if (result.Errors.Count > 0)
            {
                summary += $"\n\n有 {result.Errors.Count} 个文件失败（可能被占用），详情见日志。";
            }

            summary += "\n\n设置/账号/实例要重启启动器后才会重新加载。";
            MessageBox.Show(summary, "数据迁移", MessageBoxButton.OK,
                result.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            StatusText.Text = "旧数据迁移完成，重启后生效";
        }
        catch (Exception ex)
        {
            StatusText.Text = "数据迁移失败";
            LogError("数据迁移失败", ex);
        }
    }

    private void RefreshSettingsSummary()
    {
        LaunchSettings settings = SettingsStore.Load(_installRoot);
        Downloader.ConfigureProxy(settings.ProxyMode, settings.ProxyUrl);
        _settingsSummary.Refresh(_installRoot);
    }

    private void SaveCurseForgeKey_Click(object sender, RoutedEventArgs e)
    {
        string key = CurseForgeKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            _settingsSummary.SetCurseForgeKeyStatus("先输入 API Key");
            CurseForgeKeyStatus.Foreground = Brushes.OrangeRed;
            return;
        }

        try
        {
            CurseForgeClient.SaveKey(_installRoot, key);
            CurseForgeKeyBox.Clear();
            _settingsSummary.SetCurseForgeKeyStatus("已保存 Key（DPAPI 加密）");
            CurseForgeKeyStatus.Foreground = Brushes.Gray;
            Log("CurseForge API Key 已保存");
        }
        catch (Exception ex)
        {
            _settingsSummary.SetCurseForgeKeyStatus("保存失败");
            CurseForgeKeyStatus.Foreground = Brushes.OrangeRed;
            LogError("保存 CurseForge API Key 失败", ex);
        }
    }

    private void OpenModBackup_Click(object sender, RoutedEventArgs e)
    {
        string backupDir = Path.Combine(ModManager.ModsDir(CurrentGameDir()), ".lx-backup");
        if (!Directory.Exists(backupDir))
        {
            Log("还没有 mod 备份（批量更新后才会生成）");
            return;
        }

        ShellHelper.OpenFolder(backupDir);
    }

    private async void SwitchRole_Click(object sender, RoutedEventArgs e)
    {
        if (AccountBox.SelectedItem is not Account account)
        {
            Log("先选一个账号");
            return;
        }

        if (account.Type != "yggdrasil")
        {
            Log("只有外置（Yggdrasil）账号支持切换角色");
            return;
        }

        if (account.AvailableRoles is null || account.AvailableRoles.Count == 0)
        {
            Log("这个账号没有角色列表，先用「登录」重新登录一次");
            return;
        }

        var dialog = new RoleWindow(account) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedRole is null)
        {
            return;
        }

        try
        {
            Account switched = await YggdrasilAuth.SwitchRoleAsync(account, dialog.SelectedRole);
            AccountStore.Save(_installRoot, switched);
            RefreshAccounts();
            AccountBox.SelectedItem = AccountStore.FindByName(_installRoot, switched.Name);
            Log($"已切换角色：{switched.Name}（{switched.Uuid}）");
        }
        catch (InvalidOperationException ex)
        {
            LogError("切换角色失败", ex);
        }
    }

    private void MicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new MicrosoftLoginWindow(_installRoot) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            RefreshAccounts();
            AccountBox.SelectedItem = AccountStore.FindByName(_installRoot, dialog.Result.Name);
            Log($"已登录正版：{dialog.Result.Name}");
        }
    }

    // ---------- 实例 ----------

    private void RefreshInstances(string? selectName = null)
    {
        _suppressInstanceEvents = true;
        List<Instance> instances = InstanceStore.Load(_installRoot);
        InstancesList.ItemsSource = instances;
        HomeInstances.ItemsSource = instances;
        Instance? target = selectName is null
            ? instances.FirstOrDefault()
            : instances.FirstOrDefault(i => i.Name == selectName) ?? instances.FirstOrDefault();
        InstancesList.SelectedItem = target;
        _suppressInstanceEvents = false;

        if (target is not null)
        {
            ApplyInstance(target);
        }
        else
        {
            UpdatePackButton.IsEnabled = false;
        }

        RefreshMods();
    }

    private void Instances_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInstanceEvents)
        {
            return;
        }

        if (InstancesList.SelectedItem is not Instance instance)
        {
            UpdatePackButton.IsEnabled = false;
            return;
        }

        ApplyInstance(instance);
    }

    // 实例决定：版本、是否 Fabric、账号；mods 目录也跟着实例走。
    private void ApplyInstance(Instance instance)
    {
        if (VersionBox.ItemsSource is List<ManifestVersion> versions)
        {
            ManifestVersion? match = versions.FirstOrDefault(v => v.Id == instance.VersionId);
            if (match is not null)
            {
                VersionBox.SelectedItem = match;
            }
        }

        LoaderBox.SelectedIndex = string.Equals(instance.Loader, "fabric", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _ = ReloadLoaderVersionsAsync(instance.LoaderVersion);

        if (instance.AccountName is not null && AccountBox.ItemsSource is List<Account> accounts)
        {
            Account? account = accounts.FirstOrDefault(a => a.Name == instance.AccountName);
            if (account is not null)
            {
                AccountBox.SelectedItem = account;
            }
        }

        UpdatePackButton.IsEnabled = PackRecord.Load(InstanceStore.DirFor(_installRoot, instance.Name)) is not null;
        StatusText.Text = $"实例：{instance.Name}";
        RefreshMods();
    }

    private void CreateInstance_Click(object sender, RoutedEventArgs e)
    {
        if (IsForgeSelected)
        {
            Log("Forge/NeoForge 请先点「安装」，安装后会生成独立版本，再用它创建实例");
            return;
        }

        string version = (VersionBox.SelectedItem as ManifestVersion)?.Id ?? "1.20.4";
        string raw = NewInstanceName.Text.Trim();
        if (raw.Length == 0)
        {
            raw = version + " 实例";
        }

        string name;
        try
        {
            name = InstanceStore.ValidateName(raw);
        }
        catch (InvalidOperationException ex)
        {
            Log($"[错误] {ex.Message}");
            return;
        }

        string? loader = IsFabricSelected ? "fabric" : null;
        string? loaderVersion = IsFabricSelected ? (LoaderVersionBox.SelectedItem as LoaderChoice)?.Version : null;
        if (IsFabricSelected && loaderVersion is null)
        {
            Log("Fabric 版本列表还没加载好，等下拉框里有版本再试");
            return;
        }

        var instance = new Instance(
            name,
            version,
            loader,
            (AccountBox.SelectedItem as Account)?.Name,
            loaderVersion);

        InstanceStore.AddOrUpdate(_installRoot, instance);
        NewInstanceName.Text = string.Empty;
        RefreshInstances(name);
        Log($"已创建实例：{name}（{version}）→ {InstanceStore.DirFor(_installRoot, name)}");
    }

    private async void ImportPack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "整合包 (*.mrpack;*.zip;*.rar;*.7z;*.tar)|*.mrpack;*.zip;*.rar;*.7z;*.tar",
            Title = "选择整合包",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            PackKind kind = PackImporter.DetectKind(dialog.FileName);
            string? cfKey = CurseForgeClient.ResolveKey(_installRoot);

            if (kind == PackKind.CurseForge && cfKey is null)
            {
                var keyDialog = new ApiKeyWindow { Owner = this };
                if (keyDialog.ShowDialog() != true || keyDialog.Key is null)
                {
                    return;
                }

                cfKey = keyDialog.Key;
                CurseForgeClient.SaveKey(_installRoot, cfKey);
                Log("CurseForge key 已保存");
            }

            var imported = await PackImporter.ImportAsync(
                dialog.FileName,
                _installRoot,
                message => Dispatcher.Invoke(() => Log(message)),
                cfKey);

            RefreshInstances(imported.Instance.Name);
            Log($"整合包导入完成：{imported.Instance.Name}（{imported.Files.Count} 个文件）");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            LogError("导入整合包失败", ex);
        }
    }

    private async void UpdatePack_Click(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not Instance instance)
        {
            Log("先选中一个实例");
            return;
        }

        string dir = InstanceStore.DirFor(_installRoot, instance.Name);
        if (PackRecord.Load(dir) is null)
        {
            Log("这个实例不是导入的整合包（没有 pack.json），「更新包」只对导入的整合包有效。");
            return;
        }

        try
        {
            int removed = await PackImporter.UpdateAsync(
                dir,
                _installRoot,
                message => Dispatcher.Invoke(() => Log(message)));

            Log($"整合包更新完成，清理了 {removed} 个旧文件");
            RefreshMods();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            LogError("更新整合包失败", ex);
        }
    }

    private async void InstallOptiFabric_Click(object sender, RoutedEventArgs e)
    {
        string version = (VersionBox.SelectedItem as ManifestVersion)?.Id ?? "1.20.4";
        try
        {
            await OptiFineHelper.InstallOptiFabricAsync(
                _installRoot,
                version,
                CurrentGameDir(),
                message => Dispatcher.Invoke(() => Log(message)),
                CurseForgeClient.ResolveKey(_installRoot));

            RefreshMods();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            LogError("安装 OptiFabric 失败", ex);
        }
    }

    private void DeleteInstance_Click(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not Instance instance)
        {
            Log("先选中一个实例");
            return;
        }

        InstanceStore.Remove(_installRoot, instance.Name);
        RefreshInstances();
        Log($"已删除实例记录：{instance.Name}（文件保留在 {InstanceStore.DirFor(_installRoot, instance.Name)}）");
    }

    // 当前实例的游戏目录；没有实例时退回"版本目录"。
    private string CurrentGameDir()
    {
        if (InstancesList?.SelectedItem is Instance instance)
        {
            return InstanceStore.DirFor(_installRoot, instance.Name);
        }

        string version = (VersionBox.SelectedItem as ManifestVersion)?.Id ?? "1.20.4";
        string versionsDir = Path.Combine(_installRoot, "versions");

        if (IsFabricSelected && Directory.Exists(versionsDir))
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

    private void Version_Changed(object sender, RoutedEventArgs e)
    {
        RefreshMods();
        _ = ReloadLoaderVersionsAsync(null);
    }

    private void Loader_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LoaderVersionBox is null || InstallLoaderButton is null)
        {
            return;
        }

        InstallLoaderButton.IsEnabled = false;

        if (LoaderBox.SelectedIndex == 0)
        {
            LoaderVersionBox.ItemsSource = null;
            LoaderVersionBox.IsEnabled = false;
            return;
        }

        _ = ReloadLoaderVersionsAsync(null);
    }

    private async Task ReloadLoaderVersionsAsync(string? preferredVersion)
    {
        int request = ++_loaderVersionRequest;
        int kind = LoaderBox.SelectedIndex;
        InstallLoaderButton.IsEnabled = false;

        if (kind == 0)
        {
            LoaderVersionBox.ItemsSource = null;
            LoaderVersionBox.IsEnabled = false;
            return;
        }

        string? versionId = (VersionBox.SelectedItem as ManifestVersion)?.Id;
        if (versionId is null)
        {
            return;
        }

        LoaderVersionBox.IsEnabled = false;
        string cacheKey = $"{kind}:{versionId}";
        try
        {
            if (!_loaderVersionCache.TryGetValue(cacheKey, out List<LoaderChoice>? versions))
            {
                versions = kind switch
                {
                    1 => (await FabricInstaller.GetLoaderVersionsAsync(versionId))
                            .Select(v => new LoaderChoice(v.Version, v.Stable ? $"{v.Version}（稳定）" : v.Version, v.Stable))
                            .ToList(),
                    2 => (await ForgeInstaller.GetForgeVersionsAsync(versionId))
                            .Select(v => new LoaderChoice(v, ShortForgeVersion(v, versionId)))
                            .ToList(),
                    3 => (await ForgeInstaller.GetNeoForgeVersionsAsync(versionId))
                            .Select(v => new LoaderChoice(v, v))
                            .ToList(),
                    _ => new List<LoaderChoice>(),
                };
                _loaderVersionCache[cacheKey] = versions;
            }

            if (request != _loaderVersionRequest)
            {
                return;
            }

            LoaderVersionBox.ItemsSource = versions;
            LoaderChoice? selected = preferredVersion is null
                ? null
                : versions.FirstOrDefault(v => string.Equals(v.Version, preferredVersion, StringComparison.OrdinalIgnoreCase));
            selected ??= versions.FirstOrDefault(v => v.Stable) ?? versions.FirstOrDefault();
            LoaderVersionBox.SelectedItem = selected;
            LoaderVersionBox.IsEnabled = versions.Count > 0;
            InstallLoaderButton.IsEnabled = IsForgeSelected && versions.Count > 0;

            if (versions.Count == 0)
            {
                string name = kind == 1 ? "Fabric" : kind == 2 ? "Forge" : "NeoForge";
                Log($"{name} 没有支持 {versionId} 的版本");
            }
        }
        catch (Exception ex)
        {
            if (request != _loaderVersionRequest)
            {
                return;
            }

            LogError($"获取加载器版本列表失败（{versionId}）", ex);
            LoaderVersionBox.ItemsSource = null;
            LoaderVersionBox.IsEnabled = false;
            InstallLoaderButton.IsEnabled = false;
        }
    }

    private static string ShortForgeVersion(string full, string minecraftVersion) =>
        full.StartsWith(minecraftVersion + "-", StringComparison.OrdinalIgnoreCase)
            ? full[(minecraftVersion.Length + 1)..]
            : full;

    private async void InstallLoader_Click(object sender, RoutedEventArgs e)
    {
        if (!IsForgeSelected)
        {
            return;
        }

        if (VersionBox.SelectedItem is not ManifestVersion baseVersion || baseVersion.Id is null)
        {
            Log("先选一个 Minecraft 版本");
            return;
        }

        if (LoaderVersionBox.SelectedItem is not LoaderChoice choice)
        {
            Log("加载器版本列表还没加载好，等下拉框里有版本再点安装");
            return;
        }

        if (_loaderInstallCts is not null)
        {
            Log("安装器已经在运行");
            return;
        }

        string loader = LoaderBox.SelectedIndex == 2 ? "forge" : "neoforge";
        string display = loader == "forge" ? "Forge" : "NeoForge";

        var cts = new CancellationTokenSource();
        _loaderInstallCts = cts;
        LoaderInstallLog.Clear();
        LoaderInstallExpander.IsExpanded = true;
        CancelLoaderButton.IsEnabled = true;
        InstallLoaderButton.IsEnabled = false;
        LaunchButton.IsEnabled = false;
        Progress.IsIndeterminate = true;
        StatusText.Text = $"正在安装 {display} {choice.Version}…";

        var progress = new Progress<LoaderInstallProgress>(p =>
        {
            AppendLoaderInstallLog($"[{p.Stage}] {p.Message}");
            StatusText.Text = $"{p.Stage}：{p.Message}";
        });

        try
        {
            string installedId = await ForgeInstaller.InstallAsync(
                baseVersion.Id,
                _installRoot,
                loader,
                choice.Version,
                message => Dispatcher.Invoke(() => Log(message)),
                progress: progress,
                cancellationToken: cts.Token);

            Log($"安装完成：{display} {choice.Version} → {installedId}");
            StatusText.Text = $"已安装 {installedId}";

            if (VersionBox.ItemsSource is List<ManifestVersion> versions)
            {
                ManifestVersion? installed = versions.FirstOrDefault(v =>
                    string.Equals(v.Id, installedId, StringComparison.OrdinalIgnoreCase));
                if (installed is null)
                {
                    installed = new ManifestVersion { Id = installedId, Type = "local" };
                    versions.Insert(0, installed);
                }

                VersionBox.SelectedItem = installed;
            }

            LoaderBox.SelectedIndex = 0;

            if (InstancesList.SelectedItem is Instance instance)
            {
                InstanceStore.AddOrUpdate(_installRoot, instance with
                {
                    VersionId = installedId,
                    Loader = null,
                    LoaderVersion = null,
                });
                RefreshInstances(instance.Name);
            }
        }
        catch (OperationCanceledException)
        {
            Log($"已取消安装 {display}");
            StatusText.Text = "已取消安装";
        }
        catch (Exception ex)
        {
            LogError($"安装 {display} 失败", ex);
            StatusText.Text = "安装失败";
        }
        finally
        {
            if (ReferenceEquals(_loaderInstallCts, cts))
            {
                _loaderInstallCts = null;
            }

            cts.Dispose();
            Progress.IsIndeterminate = false;
            CancelLoaderButton.IsEnabled = false;
            LaunchButton.IsEnabled = _gameProcess is null;
            InstallLoaderButton.IsEnabled = IsForgeSelected && LoaderVersionBox.Items.Count > 0;
        }
    }

    private void CancelLoader_Click(object sender, RoutedEventArgs e)
    {
        if (_loaderInstallCts is null)
        {
            return;
        }

        StatusText.Text = "正在取消安装…";
        CancelLoaderButton.IsEnabled = false;
        _loaderInstallCts.Cancel();
    }

    private void AppendLoaderInstallLog(string message)
    {
        if (LoaderInstallLog is null)
        {
            return;
        }

        LoaderInstallLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        if (LoaderInstallLog.Text.Length > 50000)
        {
            LoaderInstallLog.Text = LoaderInstallLog.Text[^30000..];
        }

        LoaderInstallLog.ScrollToEnd();
    }

    // ---------- mods ----------

    private void RefreshMods_Click(object sender, RoutedEventArgs e) => RefreshMods();

    private void RefreshMods()
    {
        if (ModsList is null)
        {
            return;
        }

        try
        {
            _resourceViewModel.Load(CurrentGameDir());
        }
        catch (Exception ex)
        {
            LogError("读取 mods 失败", ex);
        }
    }

    private void ModSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (ModSearch is not null)
        {
            _resourceViewModel.Search = ModSearch.Text;
        }
    }

    private void ModSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ModSort is not null)
        {
            _resourceViewModel.SortIndex = ModSort.SelectedIndex;
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingMods)
        {
            Log("正在更新 mod，等它完成");
            return;
        }

        if (_modrinthBusy || _launchCts is not null || (_gameProcess is { } game && !game.HasExited))
        {
            Log("先等启动/游戏/在线安装结束再检查 mod 更新");
            return;
        }

        if (!TryGetModContext(out string gameVersion, out string loader))
        {
            return;
        }

        string gameDir = CurrentGameDir();
        List<ModFile> enabled = ModManager.List(gameDir).Where(mod => mod.Enabled).ToList();
        if (enabled.Count == 0)
        {
            StatusText.Text = "没有启用的 mod";
            Log("没有启用的 mod");
            return;
        }

        _updatingMods = true;
        CheckUpdatesButton.IsEnabled = false;
        ScanConflictsButton.IsEnabled = false;
        StatusText.Text = "检查 mod 更新…";
        var updates = new List<ModUpdate>();

        try
        {
            foreach (ModFile mod in enabled)
            {
                string path = Path.Combine(ModManager.ModsDir(gameDir), mod.FileName);
                try
                {
                    ModUpdate? update = await ModrinthClient.CheckAsync(path, gameVersion, loader);
                    if (update is null)
                    {
                        Log($"[最新] {mod.DisplayName} {mod.Version}");
                        continue;
                    }

                    updates.Add(update);
                    Log($"[可更新] {update.ProjectTitle} {update.CurrentVersion} → {update.LatestVersion}");
                }
                catch (Exception ex)
                {
                    LogError($"检查 {mod.FileName} 更新失败", ex);
                }
            }

            if (updates.Count == 0)
            {
                StatusText.Text = "全部已是最新";
                Log("全部已是最新");
                return;
            }

            string preview = string.Join(
                Environment.NewLine,
                updates.Take(8).Select(update => $"• {update.ProjectTitle}：{update.CurrentVersion} → {update.LatestVersion}"));
            if (updates.Count > 8)
            {
                preview += Environment.NewLine + $"…以及另外 {updates.Count - 8} 个";
            }

            MessageBoxResult choice = MessageBox.Show(
                $"发现 {updates.Count} 个更新：{Environment.NewLine}{Environment.NewLine}{preview}" +
                $"{Environment.NewLine}{Environment.NewLine}旧 jar 会先备份到 mods/.lx-backup，然后下载并替换。现在更新吗？",
                "批量更新 mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                StatusText.Text = "已取消更新";
                return;
            }

            _downloadFailures.Clear();
            CopyFailuresButton.IsEnabled = false;
            CopyFailuresButton.Content = "复制失败清单";
            Progress.Value = 0;
            Progress.Maximum = updates.Count;

            var progress = new Progress<ModInstallProgress>(p =>
            {
                Progress.Maximum = Math.Max(p.Total, 1);
                Progress.Value = p.Done;
                StatusText.Text = $"更新 mod {p.Done}/{p.Total}";
            });
            Action<DownloadFailure> onFailure = failure => _downloadFailures.Enqueue(failure);

            int failed = await ModUpdater.ApplyAsync(
                updates,
                gameDir,
                message => Dispatcher.Invoke(() => Log(message)),
                progress,
                onFailure);

            if (failed > 0)
            {
                CopyFailuresButton.IsEnabled = true;
                CopyFailuresButton.Content = $"复制失败清单 ({failed})";
                StatusText.Text = $"更新完成，失败 {failed} 个";
                Log($"[警告] mod 更新完成，失败 {failed} 个");
            }
            else
            {
                Progress.Value = Progress.Maximum;
                StatusText.Text = $"已更新 {updates.Count} 个 mod";
                Log($"mod 批量更新完成：{updates.Count} 个");
            }

            RefreshMods();
        }
        catch (Exception ex)
        {
            StatusText.Text = "mod 批量更新失败";
            LogError("mod 批量更新失败", ex);
        }
        finally
        {
            _updatingMods = false;
            CheckUpdatesButton.IsEnabled = true;
            ScanConflictsButton.IsEnabled = true;
        }
    }

    private void ScanConflicts_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingMods || _modrinthBusy)
        {
            Log("mod 正在安装/更新，等它完成再扫描");
            return;
        }

        string gameDir = CurrentGameDir();
        List<ModFile> mods = ModManager.List(gameDir);
        if (mods.Count == 0)
        {
            StatusText.Text = "mods 目录为空";
            Log("mods 目录为空");
            return;
        }

        List<ModIssue> issues = ModDiagnostics.Scan(mods);
        if (issues.Count == 0)
        {
            StatusText.Text = "未发现 mod 冲突";
            Log($"冲突扫描：{mods.Count} 个 mod，未发现重复 id 或缺失依赖");
            MessageBox.Show(
                $"扫描了 {mods.Count} 个 mod，未发现重复 mod id 或缺失的 required 依赖。",
                "mod 冲突检测",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        foreach (ModIssue issue in issues)
        {
            Log($"[冲突] {issue.Message}");
        }

        string summary = string.Join(Environment.NewLine, issues.Take(10).Select(issue => "• " + issue.Message));
        if (issues.Count > 10)
        {
            summary += Environment.NewLine + $"…以及另外 {issues.Count - 10} 个";
        }

        StatusText.Text = $"发现 {issues.Count} 个 mod 问题";
        MessageBox.Show(
            $"发现 {issues.Count} 个问题：{Environment.NewLine}{Environment.NewLine}{summary}",
            "mod 冲突检测",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ModCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not ModFile mod)
        {
            return;
        }

        _resourceViewModel.SetEnabled(mod, box.IsChecked == true);
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
                LogError($"添加 mod 失败：{Path.GetFileName(file)}", ex);
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

        if (_resourceViewModel.Remove(mod))
        {
            Log($"已删除 mod：{mod.FileName}");
        }
    }

    // ---------- Modrinth 在线安装 ----------

    // 搜索/安装的加载器上下文：
    // 1) 下拉框显式选了 Fabric/Forge/NeoForge 就用它；
    // 2) 否则从当前实例/版本 id 推断（neoforge-21.1.264 / fabric-loader-... / forge-...）；
    // 3) 游戏版本优先读 version.json 的 inheritsFrom，避免拿 neoforge-21.1.264 当 MC 版本去搜。
    private bool TryGetModContext(out string gameVersion, out string loader)
    {
        gameVersion = string.Empty;
        loader = string.Empty;

        Instance? instance = InstancesList.SelectedItem as Instance;

        // 实例的 VersionId 才是 mods 真正对应的版本（例如 neoforge-21.1.264）；
        // 版本下拉框只用于启动，不能拿它覆盖实例上下文。
        string versionId = instance?.VersionId
                           ?? (VersionBox.SelectedItem as ManifestVersion)?.Id
                           ?? string.Empty;
        if (versionId.Length == 0)
        {
            FailModContext("先选一个 Minecraft 版本或实例");
            return false;
        }
        if (instance is not null && string.Equals(instance.VersionId, versionId, StringComparison.OrdinalIgnoreCase))
        {
            loader = instance.Loader?.ToLowerInvariant() ?? string.Empty;
        }

        loader = LoaderBox.SelectedIndex switch
        {
            1 => "fabric",
            2 => "forge",
            3 => "neoforge",
            _ => loader,
        };

        string lowerId = versionId.ToLowerInvariant();
        if (loader.Length == 0)
        {
            if (lowerId.StartsWith("neoforge-", StringComparison.Ordinal))
            {
                loader = "neoforge";
            }
            else if (lowerId.StartsWith("forge-", StringComparison.Ordinal))
            {
                loader = "forge";
            }
            else if (lowerId.StartsWith("fabric-loader-", StringComparison.Ordinal))
            {
                loader = "fabric";
            }
        }

        gameVersion = versionId;
        string versionJson = Path.Combine(_installRoot, "versions", versionId, versionId + ".json");
        if (File.Exists(versionJson))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(versionJson));
                if (document.RootElement.TryGetProperty("inheritsFrom", out JsonElement parent) &&
                    parent.ValueKind == JsonValueKind.String &&
                    parent.GetString() is { Length: > 0 } parentId)
                {
                    gameVersion = parentId;
                }
            }
            catch (JsonException)
            {
                // 版本 json 坏了就退回 versionId
            }
        }

        if (loader.Length == 0)
        {
            FailModContext("当前是原版；先在「版本」页选 Fabric/Forge/NeoForge，或选一个已装加载器的实例/版本");
            return false;
        }

        ModrinthStatusText.Foreground = Brushes.Gray;
        ModrinthContextText.Text = $"当前：{gameVersion} / {loader}";
        return true;
    }

    private void FailModContext(string message)
    {
        ModrinthStatusText.Text = message;
        ModrinthStatusText.Foreground = Brushes.OrangeRed;
        ModrinthContextText.Text = "当前：未确定加载器";
        Log(message);
    }

    private void ModSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ModrinthResults is null)
        {
            return;
        }

        ModrinthResults.ItemsSource = null;
        ModrinthStatusText.Foreground = Brushes.Gray;
        ModrinthStatusText.Text = ModSourceBox.SelectedIndex == 1
            ? "CurseForge 需要先在「设置」页保存 API Key"
            : string.Empty;
    }

    private async void ModrinthSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetModContext(out string gameVersion, out string loader))
        {
            return;
        }

        string query = ModrinthSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            ModrinthStatusText.Text = "先输入搜索关键词，例如 sodium 或 iris";
            ModrinthStatusText.Foreground = Brushes.OrangeRed;
            Log("输入搜索关键词，例如 sodium 或 iris");
            return;
        }

        bool curseForge = ModSourceBox.SelectedIndex == 1;
        string? apiKey = null;
        if (curseForge)
        {
            apiKey = CurseForgeClient.ResolveKey(_installRoot);
            if (apiKey is null)
            {
                ModrinthStatusText.Text = "CurseForge 需要 API Key：先在「设置」页保存";
                ModrinthStatusText.Foreground = Brushes.OrangeRed;
                Log("CurseForge 搜索缺少 API Key");
                return;
            }
        }

        string sourceName = curseForge ? "CurseForge" : "Modrinth";
        ModrinthSearchButton.IsEnabled = false;
        ModrinthStatusText.Text = $"搜索 {sourceName}…";
        try
        {
            List<ModSearchResult> results = curseForge
                ? await CurseForgeClient.SearchAsync(query, gameVersion, loader, apiKey!, 20)
                : await ModrinthClient.SearchAsync(query, gameVersion, loader, 20);

            ModrinthResults.ItemsSource = results;
            ModrinthStatusText.Text = results.Count == 0 ? "没有结果" : $"找到 {results.Count} 个";
            Log($"[mods] {sourceName} 搜索「{query}」({gameVersion}/{loader})：{results.Count} 个");
        }
        catch (Exception ex)
        {
            ModrinthStatusText.Text = "搜索失败";
            LogError($"{sourceName} 搜索失败", ex);
        }
        finally
        {
            ModrinthSearchButton.IsEnabled = true;
        }
    }

    private void ModrinthSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ModrinthSearch_Click(sender, e);
        }
    }

    private async void ModrinthInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_modrinthBusy || _updatingMods)
        {
            Log("正在安装/更新 mod，等它完成");
            return;
        }

        if (_launchCts is not null || (_gameProcess is { } game && !game.HasExited))
        {
            Log("先等启动/游戏结束再安装 mod");
            return;
        }

        if (ModrinthResults.SelectedItem is not ModSearchResult project)
        {
            ModrinthStatusText.Text = "先在搜索结果里选一个 mod（需要先点「搜索」）";
            ModrinthStatusText.Foreground = Brushes.OrangeRed;
            Log("先在搜索结果里选一个 mod");
            return;
        }

        if (!TryGetModContext(out string gameVersion, out string loader))
        {
            return;
        }

        string? apiKey = null;
        if (string.Equals(project.Source, "curseforge", StringComparison.OrdinalIgnoreCase))
        {
            apiKey = CurseForgeClient.ResolveKey(_installRoot);
            if (apiKey is null)
            {
                ModrinthStatusText.Text = "CurseForge 需要 API Key：先在「设置」页保存";
                ModrinthStatusText.Foreground = Brushes.OrangeRed;
                Log("CurseForge 安装缺少 API Key");
                return;
            }
        }

        string modsDir = ModManager.ModsDir(CurrentGameDir());
        _modrinthBusy = true;
        ModrinthSearchButton.IsEnabled = false;
        ModrinthInstallButton.IsEnabled = false;
        ModrinthStatusText.Text = "安装中…";
        _downloadFailures.Clear();
        CopyFailuresButton.IsEnabled = false;
        CopyFailuresButton.Content = "复制失败清单";
        StatusText.Text = $"安装 {project.Title}…";
        Progress.Value = 0;
        Progress.Maximum = 1;

        try
        {
            var progress = new Progress<ModInstallProgress>(p =>
            {
                Progress.Maximum = Math.Max(p.Total, 1);
                Progress.Value = p.Done;
                StatusText.Text = $"安装 {p.Done}/{p.Total}";
            });

            Action<DownloadFailure> onFailure = failure => _downloadFailures.Enqueue(failure);

            int failed;
            if (string.Equals(project.Source, "curseforge", StringComparison.OrdinalIgnoreCase))
            {
                failed = await CurseForgeInstaller.InstallAsync(
                    project.Id,
                    gameVersion,
                    loader,
                    _installRoot,
                    modsDir,
                    apiKey!,
                    message => Dispatcher.Invoke(() => Log(message)),
                    progress,
                    onFailure);
            }
            else
            {
                failed = await ModInstaller.InstallAsync(
                    project.Id,
                    gameVersion,
                    loader,
                    _installRoot,
                    modsDir,
                    message => Dispatcher.Invoke(() => Log(message)),
                    progress,
                    onFailure);
            }

            if (failed > 0)
            {
                CopyFailuresButton.IsEnabled = true;
                CopyFailuresButton.Content = $"复制失败清单 ({failed})";
                ModrinthStatusText.Text = $"安装完成，失败 {failed} 个";
                StatusText.Text = $"安装完成，失败 {failed} 个";
                Log($"[警告] {project.Title} 安装完成，但 {failed} 个文件失败");
            }
            else
            {
                Progress.Value = Progress.Maximum;
                ModrinthStatusText.Text = $"已安装 {project.Title}";
                StatusText.Text = $"已安装 {project.Title}";
                Log($"[mods] 已安装 {project.Title} 到 {modsDir}");
            }

            RefreshMods();
        }
        catch (Exception ex)
        {
            ModrinthStatusText.Text = "安装失败";
            StatusText.Text = "安装失败";
            LogError("安装 mod 失败", ex);
        }
        finally
        {
            _modrinthBusy = false;
            ModrinthSearchButton.IsEnabled = true;
            ModrinthInstallButton.IsEnabled = true;
        }
    }

    // ---------- 启动 ----------

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_modrinthBusy || _updatingMods)
        {
            Log("正在安装/更新 mod，等它完成再启动");
            return;
        }

        if (_launchCts is not null)
        {
            // 防手快双击：刚点完"启动"的 600ms 内不把第二下当成取消
            if ((DateTime.Now - _launchStartedAt).TotalMilliseconds < 600)
            {
                return;
            }

            _launchCts.Cancel();
            LaunchButton.IsEnabled = false;
            StatusText.Text = "正在取消…";
            Log("正在取消启动…");
            return;
        }

        if (_gameProcess is { } runningGame && !runningGame.HasExited)
        {
            Log("游戏已经在运行，等它退出后再启动。");
            return;
        }

        if (AccountBox.SelectedItem is not Account selectedAccount)
        {
            Log("先选一个账号（或添加一个离线账号）");
            return;
        }

        // 从磁盘重新读一次：微软账号可能在别的会话里续过期，界面里的对象是旧的。
        Account account = AccountStore.FindByName(_installRoot, selectedAccount.Name) ?? selectedAccount;

        if (VersionBox.SelectedItem is not ManifestVersion version || version.Id is null)
        {
            Log("先选一个版本");
            return;
        }

        if (IsForgeSelected)
        {
            Log("Forge/NeoForge 需要先点「安装」生成版本；安装后会自动切到生成的条目，再启动。");
            return;
        }

        string? loader = IsFabricSelected ? "fabric" : null;
        string? loaderVersion = IsFabricSelected ? (LoaderVersionBox.SelectedItem as LoaderChoice)?.Version : null;
        if (IsFabricSelected && string.IsNullOrEmpty(loaderVersion))
        {
            Log("Fabric 版本列表还没加载好，等下拉框里有版本再启动");
            return;
        }

        _downloadFailures.Clear();
        CopyFailuresButton.IsEnabled = false;
        CopyFailuresButton.Content = "复制失败清单";

        var cts = new CancellationTokenSource();
        _launchCts = cts;
        _launchStartedAt = DateTime.Now;
        LaunchButton.Content = "取消";
        LaunchButton.IsEnabled = true;
        StatusText.Text = "准备中…";
        Progress.Value = 0;
        Progress.Maximum = 1;
        try
        {
            string? gameDir = InstancesList.SelectedItem is Instance selected
                ? InstanceStore.DirFor(_installRoot, selected.Name)
                : null;

            if (InstancesList.SelectedItem is Instance instance)
            {
                // 把当前选择写回实例，下次打开还记得
                InstanceStore.AddOrUpdate(_installRoot, instance with
                {
                    VersionId = version.Id,
                    Loader = loader,
                    LoaderVersion = loaderVersion,
                    AccountName = account.Name,
                });
            }

            var options = new LaunchOptions(
                _installRoot,
                version.Id,
                loader,
                loaderVersion,
                gameDir,
                account);

            var progress = new Progress<LaunchProgress>(p =>
            {
                // 取消后/游戏已启动后，迟到的进度回调不要覆盖状态栏。
                // 先看 _launchCts 是否还是自己，避免 finally 里 Dispose 后还被回调访问。
                if (!ReferenceEquals(_launchCts, cts) || cts.IsCancellationRequested || _gameProcess is not null)
                {
                    return;
                }

                Progress.Maximum = Math.Max(p.Total, 1);
                Progress.Value = p.Done;
                StatusText.Text = $"{p.Stage} {p.Done}/{p.Total}";
            });

            Action<DownloadFailure> onFailure = failure => _downloadFailures.Enqueue(failure);

            LaunchResult result = await LaunchPipeline.RunAsync(
                options,
                message => Dispatcher.Invoke(() => Log(message)),
                progress,
                onFailure,
                cts.Token);

            // 下载完了但用户刚好点了取消：不要再把游戏拉起来
            cts.Token.ThrowIfCancellationRequested();

            Log($"命令行：{result.CommandLine.Length} 字符");
            StartGame(result);
            Progress.Value = Progress.Maximum;

            if (_gameProcess is null)
            {
                StatusText.Text = "启动失败";
            }

            int failureCount = _downloadFailures.Count;
            if (failureCount > 0)
            {
                CopyFailuresButton.IsEnabled = true;
                CopyFailuresButton.Content = $"复制失败清单 ({failureCount})";
                Log($"[警告] 有 {failureCount} 个文件下载失败，已改用本地已有文件继续启动");
            }

            RefreshMods();
        }
        catch (OperationCanceledException)
        {
            Log("已取消启动");
            StatusText.Text = "已取消";
            Progress.Value = 0;
        }
        catch (Exception ex)
        {
            LogError("启动失败", ex);
            StatusText.Text = "失败";
            ShowFailureActions(ex);
        }
        finally
        {
            if (ReferenceEquals(_launchCts, cts))
            {
                _launchCts = null;
            }

            cts.Dispose();

            if (_gameProcess is null)
            {
                LaunchButton.Content = "启动游戏";
                LaunchButton.IsEnabled = true;
            }
            else
            {
                LaunchButton.Content = "游戏运行中…";
                LaunchButton.IsEnabled = false;
            }
        }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_modrinthBusy || _updatingMods)
        {
            Log("正在安装/更新 mod，等它完成再校验");
            return;
        }

        if (_launchCts is not null)
        {
            Log("正在启动/校验，等当前任务结束");
            return;
        }

        if (_gameProcess is { } running && !running.HasExited)
        {
            Log("游戏运行中，先退出游戏再校验文件");
            return;
        }

        if (VersionBox.SelectedItem is not ManifestVersion version || version.Id is null)
        {
            Log("先选一个版本");
            return;
        }

        if (IsForgeSelected)
        {
            Log("Forge/NeoForge 请先安装，选生成的版本、加载器切回原版后再校验");
            return;
        }

        string? loader = IsFabricSelected ? "fabric" : null;
        string? loaderVersion = IsFabricSelected ? (LoaderVersionBox.SelectedItem as LoaderChoice)?.Version : null;
        if (IsFabricSelected && string.IsNullOrEmpty(loaderVersion))
        {
            Log("Fabric 版本列表还没加载好，等下拉框里有版本再校验");
            return;
        }

        string? gameDir = InstancesList.SelectedItem is Instance selected
            ? InstanceStore.DirFor(_installRoot, selected.Name)
            : null;

        var cts = new CancellationTokenSource();
        _launchCts = cts;
        _launchStartedAt = DateTime.Now;
        _downloadFailures.Clear();
        CopyFailuresButton.IsEnabled = false;
        CopyFailuresButton.Content = "复制失败清单";
        RepairButton.IsEnabled = false;
        LaunchButton.Content = "取消";
        LaunchButton.IsEnabled = true;
        StatusText.Text = "校验中…";
        Progress.Value = 0;
        Progress.Maximum = 1;

        try
        {
            var account = new Account("offline", "Repair", new string('0', 32), "0", null);
            var options = new LaunchOptions(_installRoot, version.Id, loader, loaderVersion, gameDir, account);

            var progress = new Progress<LaunchProgress>(p =>
            {
                if (!ReferenceEquals(_launchCts, cts) || cts.IsCancellationRequested)
                {
                    return;
                }

                Progress.Maximum = Math.Max(p.Total, 1);
                Progress.Value = p.Done;
                StatusText.Text = $"{p.Stage} {p.Done}/{p.Total}";
            });

            Action<DownloadFailure> onFailure = failure => _downloadFailures.Enqueue(failure);

            await LaunchPipeline.RunAsync(
                options,
                message => Dispatcher.Invoke(() => Log(message)),
                progress,
                onFailure,
                cts.Token,
                downloadOnly: true);

            int failureCount = _downloadFailures.Count;
            if (failureCount > 0)
            {
                CopyFailuresButton.IsEnabled = true;
                CopyFailuresButton.Content = $"复制失败清单 ({failureCount})";
                StatusText.Text = $"校验完成：仍有 {failureCount} 个文件失败";
                Log($"[警告] 校验/修复完成，仍有 {failureCount} 个文件失败");
            }
            else
            {
                Progress.Value = Progress.Maximum;
                StatusText.Text = "校验完成：文件完整";
                Log("校验/修复完成：文件完整");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消";
            Log("已取消校验/修复");
        }
        catch (Exception ex)
        {
            LogError("校验/修复失败", ex);
            StatusText.Text = "校验失败";
            ShowFailureActions(ex);
        }
        finally
        {
            if (ReferenceEquals(_launchCts, cts))
            {
                _launchCts = null;
            }

            cts.Dispose();
            RepairButton.IsEnabled = true;
            LaunchButton.Content = _gameProcess is null ? "启动游戏" : "游戏运行中…";
            LaunchButton.IsEnabled = _gameProcess is null;
        }
    }

    private void StartGame(LaunchResult result)
    {
        int prefixEnd = result.CommandLine.StartsWith('"')
            ? result.CommandLine.IndexOf('"', 1) + 1
            : result.JavaPath.Length;

        string arguments = result.CommandLine[prefixEnd..].TrimStart();

        var info = new ProcessStartInfo(result.JavaPath)
        {
            Arguments = arguments,
            UseShellExecute = false,
            // 不建控制台窗口：否则用户关掉黑框会向 java 发 CTRL_CLOSE，进程以 0xC000013A 秒退。
            CreateNoWindow = true,
            WorkingDirectory = result.GameDir,
        };

        Process? process = Process.Start(info);
        if (process is null)
        {
            Log("启动失败：进程没有创建");
            return;
        }

        _gameProcess = process;
        _gameStartedAt = DateTime.Now;
        process.EnableRaisingEvents = true;
        process.Exited += GameProcess_Exited;

        LaunchButton.IsEnabled = false;
        LaunchButton.Content = "游戏运行中…";
        StatusText.Text = $"游戏运行中（PID {process.Id}）";
        Log($"已启动 java（PID {process.Id}）");
    }

    // Exited 在后台线程触发，算完退出码再回 UI 线程。
    private void GameProcess_Exited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        if (!ReferenceEquals(process, _gameProcess))
        {
            process.Dispose();
            return;
        }

        int? exitCode = null;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 拿不到就当未知
        }

        if (Dispatcher.HasShutdownStarted)
        {
            process.Dispose();
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => OnGameExited(process, exitCode)));
    }

    private void OnGameExited(Process process, int? exitCode)
    {
        if (!ReferenceEquals(process, _gameProcess))
        {
            process.Dispose();
            return;
        }

        _gameProcess = null;
        process.Dispose();

        TimeSpan duration = DateTime.Now - _gameStartedAt;
        string durationText = FormatDuration(duration);

        LaunchButton.IsEnabled = true;
        LaunchButton.Content = "启动游戏";
        StatusText.Text = exitCode is null or 0
            ? $"游戏已退出（{durationText}）"
            : $"游戏异常退出（退出码 {exitCode}，{durationText}）";
        Log($"游戏已退出：退出码 {exitCode?.ToString() ?? "未知"}，运行时长 {durationText}");

        if (exitCode is int code && code != 0 && !_closing)
        {
            Log("[提示] 游戏异常退出，点「打开日志」查看 logs/latest.log");

            MessageBoxResult choice = MessageBox.Show(
                $"游戏异常退出（退出码 {code}），运行时长 {durationText}。{Environment.NewLine}{Environment.NewLine}是否打开日志目录？",
                "游戏异常退出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Yes)
            {
                OpenLogFolder();
            }
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes} 分 {duration.Seconds} 秒";
        }

        return $"{Math.Max(duration.Seconds, 1)} 秒";
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_gameProcess is null || _gameProcess.HasExited || _closing)
        {
            _closing = true;
            return;
        }

        MessageBoxResult choice = MessageBox.Show(
            "游戏还在运行。退出启动器不会关闭游戏，确定退出吗？",
            "Lx yulin carp",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Yes)
        {
            _closing = true;
        }
        else
        {
            e.Cancel = true;
        }
    }

    // ---------- 启动失败 ----------

    private void CopyFailures_Click(object sender, RoutedEventArgs e) => CopyFailuresToClipboard();

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenLogFolder();

    private void ShowFailureActions(Exception exception)
    {
        int count = _downloadFailures.Count;
        if (count == 0)
        {
            MessageBoxResult open = MessageBox.Show(
                $"启动失败：{exception.Message}{Environment.NewLine}{Environment.NewLine}是否打开日志目录？",
                "启动失败",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (open == MessageBoxResult.Yes)
            {
                OpenLogFolder();
            }

            return;
        }

        CopyFailuresButton.IsEnabled = true;
        CopyFailuresButton.Content = $"复制失败清单 ({count})";

        MessageBoxResult choice = MessageBox.Show(
            $"启动失败：{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"本次有 {count} 个文件下载失败。{Environment.NewLine}{Environment.NewLine}" +
            "是：复制失败清单" + Environment.NewLine +
            "否：打开日志目录" + Environment.NewLine +
            "取消：关闭",
            "启动失败",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Error);

        if (choice == MessageBoxResult.Yes)
        {
            CopyFailuresToClipboard();
        }
        else if (choice == MessageBoxResult.No)
        {
            OpenLogFolder();
        }
    }

    private void CopyFailuresToClipboard()
    {
        if (_downloadFailures.IsEmpty)
        {
            Log("当前没有下载失败记录");
            return;
        }

        try
        {
            Clipboard.SetText(BuildFailureList());
            Log($"已复制 {_downloadFailures.Count} 条下载失败记录到剪贴板");
        }
        catch (Exception ex)
        {
            LogError("复制失败清单失败", ex);
        }
    }

    private string BuildFailureList()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Lx yulin carp 下载失败清单 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"# 共 {_downloadFailures.Count} 条；格式：相对路径 <TAB> 原因 <TAB> URL");
        foreach (DownloadFailure failure in _downloadFailures)
        {
            builder.AppendLine($"{failure.RelativePath}	{failure.Reason}	{failure.Url}");
        }

        return builder.ToString();
    }

    private void OpenLogFolder()
    {
        try
        {
            ShellHelper.OpenFolder(LxLog.DirectoryPath);
        }
        catch (Exception ex)
        {
            LogError("打开日志目录失败", ex);
        }
    }

    private void Log(string message)
    {
        LxLog.Info(message);
        AppendToLogBox(message);
    }

    // 异常要把堆栈写进文件，否则远程排错只剩一句"出错了"。
    private void LogError(string context, Exception exception)
    {
        LxLog.Error(context, exception);
        AppendToLogBox($"[错误] {context}：{exception.Message}");
    }

    private void AppendToLogBox(string message)
    {
        LogBox.AppendText(message + Environment.NewLine);
        LogBox.ScrollToEnd();
    }
}
