using System.Text.Json;
using Lx.Probe;
using Lx.Probe.Auth;
using Lx.Probe.Instances;
using Lx.Probe.Launch;
using Lx.Probe.Loaders;
using Lx.Probe.Model;
using Lx.Probe.Modpacks;
using Lx.Probe.Mods;
using Lx.Probe.Resolve;

// 参数：--list / --version <id> / --root <dir> / --game-dir <dir> / --loader fabric
//       --auth offline|yggdrasil|microsoft / --name / --api / --email / --client-id
//       --accounts / --account <名字> / --mods list|enable|disable|add|remove
string? rootArg = null;
string? versionArg = null;
string? authArg = null;
string? nameArg = null;
string? apiArg = null;
string? emailArg = null;
string? clientIdArg = null;
string? loaderArg = null;
string? loaderVersionArg = null;
string? gameDirArg = null;
string? modsAction = null;
string? modsTarget = null;
string? importArg = null;
string? cfKeyArg = null;
string? installLoaderArg = null;
string? artifactSourceArg = null;
string? roleArg = null;
bool updatePackFlag = false;
bool installOptiFabricFlag = false;
string? accountArg = null;
bool listOnly = false;
bool listAccounts = false;
bool debug = false;
bool relogin = false;

for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg == "--list")
    {
        listOnly = true;
    }
    else if (arg == "--accounts")
    {
        listAccounts = true;
    }
    else if (arg == "--debug")
    {
        debug = true;
    }
    else if (arg == "--relogin")
    {
        relogin = true;
    }
    else if (arg == "--version" && i + 1 < args.Length)
    {
        versionArg = args[++i];
    }
    else if (arg == "--root" && i + 1 < args.Length)
    {
        rootArg = args[++i];
    }
    else if (arg == "--import" && i + 1 < args.Length)
    {
        importArg = args[++i];
    }
    else if (arg == "--cf-key" && i + 1 < args.Length)
    {
        cfKeyArg = args[++i];
    }
    else if (arg == "--game-dir" && i + 1 < args.Length)
    {
        gameDirArg = args[++i];
    }
    else if (arg == "--mods" && i + 1 < args.Length)
    {
        modsAction = args[++i].ToLowerInvariant();
        if (modsAction != "list" && modsAction != "check" && i + 1 < args.Length)
        {
            modsTarget = args[++i];
        }
    }
    else if (arg == "--loader" && i + 1 < args.Length)
    {
        loaderArg = args[++i];
    }
    else if (arg == "--install-loader" && i + 1 < args.Length)
    {
        installLoaderArg = args[++i];
    }
    else if (arg == "--artifact-source" && i + 1 < args.Length)
    {
        artifactSourceArg = args[++i];
    }
    else if (arg == "--role" && i + 1 < args.Length)
    {
        roleArg = args[++i];
    }
    else if (arg == "--update-pack")
    {
        updatePackFlag = true;
    }
    else if (arg == "--install-optifabric")
    {
        installOptiFabricFlag = true;
    }
    else if (arg == "--loader-version" && i + 1 < args.Length)
    {
        loaderVersionArg = args[++i];
    }
    else if (arg == "--auth" && i + 1 < args.Length)
    {
        authArg = args[++i];
    }
    else if (arg == "--name" && i + 1 < args.Length)
    {
        nameArg = args[++i];
    }
    else if (arg == "--api" && i + 1 < args.Length)
    {
        apiArg = args[++i];
    }
    else if (arg == "--email" && i + 1 < args.Length)
    {
        emailArg = args[++i];
    }
    else if (arg == "--client-id" && i + 1 < args.Length)
    {
        clientIdArg = args[++i];
    }
    else if (arg == "--account" && i + 1 < args.Length)
    {
        accountArg = args[++i];
    }
    else if (!arg.StartsWith("--", StringComparison.Ordinal))
    {
        rootArg ??= arg;
    }
}

string installRoot = Path.GetFullPath(rootArg ?? AppPaths.ResolveInstallRoot());
string versionId = versionArg ?? "1.20.4";

if (installLoaderArg is not null)
{
    try
    {
        string installedId = await ForgeInstaller.InstallAsync(
            versionId,
            installRoot,
            installLoaderArg,
            loaderVersionArg,
            Console.WriteLine,
            artifactSourceArg);

        Console.WriteLine($"安装完成：{installedId}");
        Console.WriteLine($"启动：dotnet run --project src/Lx.Cli -- --version {installedId}");
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException)
    {
        Console.WriteLine($"[错误] {ex.Message}");
        return 1;
    }
}

if (updatePackFlag)
{
    if (gameDirArg is null)
    {
        Console.WriteLine("[错误] --update-pack 需要 --game-dir <实例目录>");
        return 1;
    }

    try
    {
        int removed = await PackImporter.UpdateAsync(Path.GetFullPath(gameDirArg), installRoot, Console.WriteLine);
        Console.WriteLine($"更新结束，清理了 {removed} 个旧文件");
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException)
    {
        Console.WriteLine($"[错误] {ex.Message}");
        return 1;
    }
}

if (installOptiFabricFlag)
{
    string targetDir = gameDirArg is not null
        ? Path.GetFullPath(gameDirArg)
        : Path.Combine(installRoot, "versions", versionId);

    try
    {
        if (cfKeyArg is not null)
        {
            CurseForgeClient.SaveKey(installRoot, cfKeyArg);
        }

        await OptiFineHelper.InstallOptiFabricAsync(
            installRoot,
            versionId,
            targetDir,
            Console.WriteLine,
            CurseForgeClient.ResolveKey(installRoot, cfKeyArg));

        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException)
    {
        Console.WriteLine($"[错误] {ex.Message}");
        return 1;
    }
}

if (modsAction is not null)
{
    string modsGameDir = gameDirArg is not null
        ? Path.GetFullPath(gameDirArg)
        : Path.Combine(installRoot, "versions", versionId);

    if (modsAction == "check")
    {
        return await CheckModsAsync(modsGameDir, versionId, loaderArg ?? "fabric");
    }

    return RunMods(modsAction, modsTarget, modsGameDir);
}

if (importArg is not null)
{
    try
    {
        if (cfKeyArg is not null)
        {
            CurseForgeClient.SaveKey(installRoot, cfKeyArg);
            Console.WriteLine("CurseForge key 已保存到 minecraft\\curseforge-key.txt");
        }

        string? cfKey = CurseForgeClient.ResolveKey(installRoot, cfKeyArg);
        PackImportResult imported = await PackImporter.ImportAsync(importArg, installRoot, Console.WriteLine, cfKey);
        Console.WriteLine($"导入完成：{imported.Instance.Name}（{imported.Instance.VersionId}），{imported.Files.Count} 个文件");
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException)
    {
        Console.WriteLine($"[错误] {ex.Message}");
        return 1;
    }
}

if (listAccounts)
{
    List<Account> stored = AccountStore.Load(installRoot);
    if (stored.Count == 0)
    {
        Console.WriteLine("还没有保存的账号，先登录一次。");
        return 0;
    }

    Console.WriteLine($"已保存 {stored.Count} 个账号（{AccountStore.PathFor(installRoot)}）：");
    foreach (Account saved in stored)
    {
        Console.WriteLine($"  {saved.Type,-9} {saved.Name,-16} {saved.Uuid} {saved.ApiRoot ?? string.Empty}");
    }

    return 0;
}

if (listOnly)
{
    LaunchSettings listSettings = SettingsStore.Load(installRoot);
    var listSource = new DownloadSourceSettings(listSettings.DownloadSource, listSettings.CustomDownloadBase);
    VersionManifest manifest = await ManifestService.EnsureManifestAsync(
        installRoot,
        url => DownloadUrlRewriter.Rewrite(url, listSource));

    Console.WriteLine($"下载源 = {DownloadUrlRewriter.Describe(listSource)}");
    Console.WriteLine($"最新 release = {manifest.Latest?.Release}，最新 snapshot = {manifest.Latest?.Snapshot}");
    Console.WriteLine($"共 {manifest.Versions?.Count ?? 0} 个版本，最近 20 个：");
    foreach (ManifestVersion version in (manifest.Versions ?? new List<ManifestVersion>()).Take(20))
    {
        Console.WriteLine($"  {version.Id,-18} {version.Type,-10} {version.ReleaseTime:yyyy-MM-dd}");
    }

    return 0;
}

Account account;
if (accountArg is not null)
{
    Account? selected = AccountStore.FindByName(installRoot, accountArg);
    if (selected is null)
    {
        Console.WriteLine($"[错误] 没有叫 {accountArg} 的账号，用 --accounts 查看已保存的账号");
        return 1;
    }

    account = selected;
}
else
{
    string authType = authArg ?? "offline";
    switch (authType)
    {
        case "offline":
            account = OfflineAuth.Create(nameArg ?? "Player");
            break;

        case "yggdrasil":
            if (apiArg is null)
            {
                Console.WriteLine("[错误] 外置登录需要 --api <站点根>");
                return 1;
            }

            Account? cached = relogin
                ? null
                : AccountStore.Load(installRoot).FirstOrDefault(a =>
                    a.Type == "yggdrasil" &&
                    string.Equals(a.ApiRoot, apiArg, StringComparison.OrdinalIgnoreCase));
            if (cached is not null)
            {
                if (roleArg is null)
                {
                    account = cached;
                    break;
                }

                try
                {
                    account = await YggdrasilAuth.SwitchRoleAsync(cached, roleArg);
                    break;
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"[错误] {ex.Message}");
                    return 1;
                }
            }

            if (emailArg is null)
            {
                Console.WriteLine("[错误] 外置登录需要 --email <账号>");
                return 1;
            }

            string password = ConsolePrompt.ReadPassword("密码（不会保存）：");
            try
            {
                account = await YggdrasilAuth.LoginAsync(apiArg, emailArg, password, debug, roleArg);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[错误] {ex.Message}");
                return 1;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[错误] 连不上 {apiArg}：{ex.Message}");
                return 1;
            }

            break;

        case "microsoft":
            if (clientIdArg is null)
            {
                Console.WriteLine("[错误] 正版登录需要 --client-id <Azure 应用的客户端 ID>");
                return 1;
            }

            try
            {
                account = await MicrosoftAuth.LoginAsync(clientIdArg);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"[错误] {ex.Message}");
                return 1;
            }

            break;

        default:
            Console.WriteLine($"[错误] 不支持的登录方式：{authType}（offline / yggdrasil / microsoft）");
            return 1;
    }
}

AccountStore.Save(installRoot, account);
Console.WriteLine($"账号：{account.Type} {account.Name} {account.Uuid}");

var options = new LaunchOptions(installRoot, versionId, loaderArg, loaderVersionArg, gameDirArg, account);

LaunchResult result;
try
{
    result = await LaunchPipeline.RunAsync(options, Console.WriteLine);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"[错误] {ex.Message}");
    return 1;
}

Console.WriteLine();
if (account.AccessToken.Length > 4 && account.AccessToken != "0")
{
    Console.WriteLine("（下面这行含 accessToken，别发给别人）");
}

Console.WriteLine(result.CommandLine);

return 0;

// mods 子命令：list / enable / disable / add / remove
static int RunMods(string action, string? target, string gameDir)
{
    Console.WriteLine($"mods 目录：{ModManager.ModsDir(gameDir)}");

    switch (action)
    {
        case "list":
            List<ModFile> mods = ModManager.List(gameDir);
            if (mods.Count == 0)
            {
                Console.WriteLine("（空）");
                return 0;
            }

            foreach (ModFile mod in mods)
            {
                string state = mod.Enabled ? "启用" : "禁用";
                Console.WriteLine($"  [{state}] {mod.Name ?? mod.FileName} {mod.Version}  ({mod.FileName}, {mod.Size / 1024} KB)");
            }

            return 0;

        case "enable":
        case "disable":
            if (target is null)
            {
                Console.WriteLine("[错误] 需要文件名，例如 --mods disable sodium.jar");
                return 1;
            }

            bool updated = ModManager.SetEnabled(gameDir, target, action == "enable");
            Console.WriteLine(updated ? "已更新" : "[错误] 找不到这个 mod");
            return updated ? 0 : 1;

        case "add":
            if (target is null)
            {
                Console.WriteLine("[错误] 需要 jar 路径，例如 --mods add C:\\Downloads\\sodium.jar");
                return 1;
            }

            try
            {
                ModManager.Add(gameDir, target);
                Console.WriteLine("已添加");
                return 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                Console.WriteLine($"[错误] {ex.Message}");
                return 1;
            }

        case "remove":
            if (target is null)
            {
                Console.WriteLine("[错误] 需要文件名");
                return 1;
            }

            bool removed = ModManager.Remove(gameDir, target);
            Console.WriteLine(removed ? "已删除" : "[错误] 找不到这个 mod");
            return removed ? 0 : 1;

        default:
            Console.WriteLine("[错误] --mods 支持 list / enable / disable / add / remove / check");
            return 1;
    }
}

// 检查所有启用 mod 的更新（Modrinth，按文件 sha1 反查版本）。
static async Task<int> CheckModsAsync(string gameDir, string gameVersion, string loader)
{
    List<ModFile> mods = ModManager.List(gameDir);
    if (mods.Count == 0)
    {
        Console.WriteLine("（没有 mod）");
        return 0;
    }

    int updates = 0;
    foreach (ModFile mod in mods.Where(m => m.Enabled))
    {
        string path = Path.Combine(ModManager.ModsDir(gameDir), mod.FileName);
        try
        {
            ModUpdate? update = await ModrinthClient.CheckAsync(path, gameVersion, loader);
            if (update is null)
            {
                Console.WriteLine($"  [最新]   {mod.DisplayName} {mod.Version}");
                continue;
            }

            updates++;
            Console.WriteLine($"  [可更新] {update.ProjectTitle} {update.CurrentVersion} → {update.LatestVersion}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [错误]   {mod.FileName}: {ex.Message}");
        }
    }

    Console.WriteLine($"检查完成：可更新 {updates} 个");
    return 0;
}