using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Lx.Probe.Auth;
using Lx.Probe.Instances;
using Lx.Probe.Loaders;
using Lx.Probe.Launch;
using Lx.Probe.Model;
using Lx.Probe.Modpacks;
using Lx.Probe.Mods;
using Lx.Probe.Resolve;
using Microsoft.Win32;

namespace Lx.UI;

public partial class MainWindow : Window
{
    // 从 bin 目录往上找 Lx.sln，把 minecraft 定位到仓库根，避免"从哪个目录启动"影响结果。
    private readonly string _installRoot = ResolveInstallRoot();

    private List<ModFile> _allMods = new();
    private bool _suppressInstanceEvents;

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

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LoginWindow(_installRoot) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            RefreshAccounts();
            AccountBox.SelectedItem = AccountStore.FindByName(_installRoot, dialog.Result.Name);
            Log($"已登录：{dialog.Result.Type} {dialog.Result.Name}");
        }
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
            Log($"[错误] {ex.Message}");
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
        Instance? target = selectName is null
            ? instances.FirstOrDefault()
            : instances.FirstOrDefault(i => i.Name == selectName) ?? instances.FirstOrDefault();
        InstancesList.SelectedItem = target;
        _suppressInstanceEvents = false;

        if (target is not null)
        {
            ApplyInstance(target);
        }

        RefreshMods();
    }

    private void Instances_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInstanceEvents || InstancesList.SelectedItem is not Instance instance)
        {
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

        FabricCheck.IsChecked = string.Equals(instance.Loader, "fabric", StringComparison.OrdinalIgnoreCase);

        if (instance.AccountName is not null && AccountBox.ItemsSource is List<Account> accounts)
        {
            Account? account = accounts.FirstOrDefault(a => a.Name == instance.AccountName);
            if (account is not null)
            {
                AccountBox.SelectedItem = account;
            }
        }

        StatusText.Text = $"实例：{instance.Name}";
        RefreshMods();
    }

    private void CreateInstance_Click(object sender, RoutedEventArgs e)
    {
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

        var instance = new Instance(
            name,
            version,
            FabricCheck.IsChecked == true ? "fabric" : null,
            (AccountBox.SelectedItem as Account)?.Name);

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
            Log($"[错误] {ex.Message}");
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
            Log($"[错误] {ex.Message}");
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
            Log($"[错误] {ex.Message}");
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

    private void Version_Changed(object sender, RoutedEventArgs e) => RefreshMods();

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
            string gameDir = CurrentGameDir();
            Directory.CreateDirectory(ModManager.ModsDir(gameDir));
            ModsPathText.Text = ModManager.ModsDir(gameDir);
            _allMods = ModManager.List(gameDir);
            ApplyModFilter();
        }
        catch (Exception ex)
        {
            Log($"[错误] {ex.Message}");
        }
    }

    private void ModSearch_Changed(object sender, TextChangedEventArgs e) => ApplyModFilter();

    private void ModSort_Changed(object sender, SelectionChangedEventArgs e) => ApplyModFilter();

    private void ApplyModFilter()
    {
        if (ModsList is null)
        {
            return;
        }

        string query = ModSearch?.Text?.Trim() ?? string.Empty;
        IEnumerable<ModFile> filtered = _allMods;

        if (query.Length > 0)
        {
            filtered = filtered.Where(mod =>
                mod.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                mod.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (mod.Id ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filtered = (ModSort?.SelectedIndex ?? 0) switch
        {
            1 => filtered.OrderBy(mod => mod.FileName, StringComparer.OrdinalIgnoreCase),
            2 => filtered.OrderByDescending(mod => mod.Enabled).ThenBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(mod => mod.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        ModsList.ItemsSource = filtered.ToList();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        string gameDir = CurrentGameDir();
        List<ModFile> enabled = ModManager.List(gameDir).Where(mod => mod.Enabled).ToList();
        if (enabled.Count == 0)
        {
            Log("没有启用的 mod");
            return;
        }

        string version = (VersionBox.SelectedItem as ManifestVersion)?.Id ?? "1.20.4";
        const string loader = "fabric"; // 目前只有 Fabric 有 mod
        var updates = new List<ModUpdate>();

        foreach (ModFile mod in enabled)
        {
            string path = Path.Combine(ModManager.ModsDir(gameDir), mod.FileName);
            try
            {
                ModUpdate? update = await ModrinthClient.CheckAsync(path, version, loader);
                if (update is null)
                {
                    Log($"[最新] {mod.DisplayName} {mod.Version}");
                    continue;
                }

                updates.Add(update);
                Log($"[可更新] {update.ProjectTitle} {update.CurrentVersion} → {update.LatestVersion}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                Log($"[错误] {mod.FileName}: {ex.Message}");
            }
        }

        if (updates.Count == 0)
        {
            Log("全部已是最新");
            return;
        }

        MessageBoxResult choice = MessageBox.Show(
            $"发现 {updates.Count} 个更新，现在下载吗？",
            "mod 更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        int done = 0;
        foreach (ModUpdate update in updates)
        {
            var item = new DownloadItem(
                update.DownloadUrl,
                "mods/" + update.NewFileName,
                update.Sha1 ?? string.Empty,
                update.Size);

            int failed = await Downloader.DownloadAllAsync(new[] { item }, gameDir);
            if (failed > 0)
            {
                Log($"[失败] {update.ProjectTitle} 下载失败");
                continue;
            }

            string oldPath = Path.Combine(ModManager.ModsDir(gameDir), update.FileName);
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }

            done++;
            Log($"[已更新] {update.ProjectTitle} → {update.LatestVersion}");
        }

        Log($"更新完成：{done}/{updates.Count}");
        RefreshMods();
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

    // ---------- 启动 ----------

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
                    Loader = FabricCheck.IsChecked == true ? "fabric" : null,
                    AccountName = account.Name,
                });
            }

            var options = new LaunchOptions(
                _installRoot,
                version.Id,
                FabricCheck.IsChecked == true ? "fabric" : null,
                null,
                gameDir,
                account);

            var progress = new Progress<LaunchProgress>(p =>
            {
                Progress.Maximum = Math.Max(p.Total, 1);
                Progress.Value = p.Done;
                StatusText.Text = $"{p.Stage} {p.Done}/{p.Total}";
            });

            LaunchResult result = await LaunchPipeline.RunAsync(
                options,
                message => Dispatcher.Invoke(() => Log(message)),
                progress);

            Log($"命令行：{result.CommandLine.Length} 字符");
            StartGame(result.CommandLine);
            Progress.Value = Progress.Maximum;
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
