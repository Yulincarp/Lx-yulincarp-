using System.Text.Json;
using Lx.Probe.Auth;
using Lx.Probe.Model;
using Lx.Probe.Resolve;

// 参数：--list（只看版本列表）、--version <id>、--root <安装根目录>。
// 只给一个位置参数时也当安装根目录（M1/M2 的老用法）。
string? rootArg = null;
string? versionArg = null;
string? authArg = null;
string? nameArg = null;
string? apiArg = null;
string? emailArg = null;
bool listOnly = false;

for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (arg == "--list")
    {
        listOnly = true;
    }
    else if (arg == "--version" && i + 1 < args.Length)
    {
        versionArg = args[++i];
    }
    else if (arg == "--root" && i + 1 < args.Length)
    {
        rootArg = args[++i];
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
    else if (!arg.StartsWith("--", StringComparison.Ordinal))
    {
        rootArg ??= arg;
    }
}

string installRoot = Path.GetFullPath(rootArg ?? "minecraft");
VersionManifest manifest = await ManifestService.EnsureManifestAsync(installRoot);

if (listOnly)
{
    Console.WriteLine($"最新 release = {manifest.Latest?.Release}，最新 snapshot = {manifest.Latest?.Snapshot}");
    Console.WriteLine($"共 {manifest.Versions?.Count ?? 0} 个版本，最近 20 个：");
    foreach (ManifestVersion version in (manifest.Versions ?? new List<ManifestVersion>()).Take(20))
    {
        Console.WriteLine($"  {version.Id,-18} {version.Type,-10} {version.ReleaseTime:yyyy-MM-dd}");
    }

    return 0;
}

string versionId = versionArg ?? "1.20.4";
string versionJsonPath;
try
{
    versionJsonPath = await ManifestService.EnsureVersionJsonAsync(manifest, versionId, installRoot);
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"[错误] {ex.Message}");
    return 1;
}

string authType = authArg ?? "offline";
Account account;
switch (authType)
{
    case "offline":
        account = OfflineAuth.Create(nameArg ?? "Player");
        break;
    case "yggdrasil":
        if (apiArg is null || emailArg is null)
        {
            Console.WriteLine("[错误] 外置登录需要 --api <站点根> 和 --email <账号>");
            return 1;
        }
        string password = ConsolePrompt.ReadPassword("密码（不会保存）：");
        try
        {
            account = await YggdrasilAuth.LoginAsync(apiArg, emailArg, password);
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
    default:
        Console.WriteLine($"[错误] 暂不支持的登录方式：{authType}（offline / yggdrasil）");
        return 1;
}
AccountStore.Save(installRoot, account);
Console.WriteLine($"账号：{account.Type} {account.Name} {account.Uuid}");

var options = new JsonSerializerOptions();
options.PropertyNameCaseInsensitive = true;

VersionJson v = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(versionJsonPath), options)!;
var features = new Dictionary<string, bool>();

Console.WriteLine($"id = {v.Id}");
Console.WriteLine($"mainClass = {v.MainClass}");
Console.WriteLine($"assets = {v.Assets}");
Console.WriteLine($"java = {v.JavaVersion?.MajorVersion}");
Console.WriteLine($"version.json = {versionJsonPath}");

// Step 3：只打印数量（1.20.4 应为 88）和前 3 个 name。
List<Library>? libraries = v.Libraries;
Console.WriteLine($"libraries = {libraries?.Count ?? 0}");
foreach (Library lib in (libraries ?? new List<Library>()).Take(3))
{
    Console.WriteLine($"  name = {lib.Name}");
}

// M2 Step 1：只算清单，不下载。
List<DownloadItem> manifestItems = DownloadManifest.Build(v, versionId, features);
double totalMb = manifestItems.Sum(i => i.Size) / 1024.0 / 1024.0;
Console.WriteLine($"清单 = {manifestItems.Count} 个文件，合计 {totalMb:F1} MB");
foreach (DownloadItem item in manifestItems.Take(3))
{
    Console.WriteLine($"  {item.RelativePath}");
}
Console.WriteLine();

// Step 5：拼 classpath（只打印，不下载）。
// 版本隔离：存档、natives、日志都放 versions/<id>/ 下，不污染安装根目录。
string gameRoot = Path.Combine(installRoot, "versions", versionId);
string librariesDir = Path.Combine(installRoot, "libraries");
string assetsDir = Path.Combine(installRoot, "assets");
string clientJar = Path.Combine(installRoot, "versions", versionId, versionId + ".jar");
int failed = await Downloader.DownloadAllAsync(manifestItems, installRoot);
Console.WriteLine($"下载结果：失败 {failed} 个");

// M2 Step 4：下载 assets（index 已经在上面下过，这里解析后再下对象）。
string indexFile = Path.Combine(assetsDir, "indexes", (v.Assets ?? "12") + ".json");
List<DownloadItem> assets = DownloadManifest.BuildAssets(indexFile);
Console.WriteLine($"assets 清单 = {assets.Count} 个文件");
int failedAssets = await Downloader.DownloadAllAsync(assets, installRoot);
Console.WriteLine($"assets 下载失败 {failedAssets} 个");

// 启动前体检：缺文件就在这里停，别等 java 抛 ClassNotFoundException。
var allFiles = new List<DownloadItem>(manifestItems);
allFiles.AddRange(assets);
List<DownloadItem> missingFiles = InstallCheck.FindMissing(allFiles, installRoot);
Console.WriteLine($"体检：共 {allFiles.Count} 个文件，缺 {missingFiles.Count} 个");
if (missingFiles.Count > 0)
{
    Console.WriteLine("缺少以下文件，先补齐再启动：");
    foreach (DownloadItem item in missingFiles.Take(10))
    {
        Console.WriteLine($"  {item.RelativePath}");
        Console.WriteLine($"    {item.Url}");
    }

    return 1;
}

List<string> classpath = ClasspathBuilder.Build(
    libraries ?? new List<Library>(),
    librariesDir,
    clientJar,
    features);

string classpathText = string.Join(";", classpath);
Console.WriteLine($"classpath entries = {classpath.Count}（library {classpath.Count - 1} + client 1）");
Console.WriteLine($"classpath length = {classpathText.Length}");

// Step 6：1.20.4 的 natives 是独立的 ":natives-windows" 库，按架构挑出来后解压到 natives/。
string nativesDir = Path.Combine(gameRoot, "natives");
List<string> nativeJars = NativesExtractor.FindJars(libraries ?? new List<Library>(), features, librariesDir);
int missingJars = nativeJars.Count(jar => !File.Exists(jar));
int nativeFiles = NativesExtractor.Extract(nativeJars, nativesDir);
int nativeDlls = Directory.GetFiles(nativesDir, "*.dll", SearchOption.AllDirectories).Length;

Console.WriteLine($"natives jars = {nativeJars.Count}（{NativesExtractor.ArchSuffix}），缺少 {missingJars} 个");
Console.WriteLine($"natives dir = {nativesDir}");
Console.WriteLine($"natives files = {nativeFiles}，dll = {nativeDlls}");

// Step 7：替换 ${...} 占位符，拼出完整命令行（先按离线账号填）。
var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["auth_player_name"] = account.Name,
    ["version_name"] = versionId,
    ["game_directory"] = gameRoot,
    ["assets_root"] = assetsDir,
    ["assets_index_name"] = v.Assets ?? "12",
    ["auth_uuid"] = account.Uuid,
    ["auth_access_token"] = account.AccessToken,
    ["user_type"] = account.Type switch
    {
        "microsoft" => "msa",
        "yggdrasil" => "mojang",
        _ => "legacy",
    },
    ["version_type"] = "release",
    ["natives_directory"] = nativesDir,
    ["launcher_name"] = "lx",
    ["launcher_version"] = "0.1",
    ["classpath"] = classpathText,
    ["clientid"] = "",
    ["auth_xuid"] = "",
    ["resolution_width"] = "854",
    ["resolution_height"] = "480",
};

List<string> jvmArgs = ArgumentExpander.Expand(v.Arguments?.Jvm, values, features);

// 外置登录：把 authlib-injector 挂成 javaagent，位置必须在 -cp 和 mainClass 之前。
if (account.Type == "yggdrasil")
{
    string injector = Path.Combine(installRoot, "authlib-injector.jar");
    if (!File.Exists(injector))
    {
        Console.WriteLine($"[错误] 缺少 {injector}");
        return 1;
    }

    jvmArgs.Insert(0, $"-javaagent:{injector}={account.ApiRoot}");
}
List<string> gameArgs = ArgumentExpander.Expand(v.Arguments?.Game, values, features);
string commandLine = CommandLineBuilder.Build("java", jvmArgs, v.MainClass ?? string.Empty, gameArgs);

Console.WriteLine($"jvm args = {jvmArgs.Count}，game args = {gameArgs.Count}");
Console.WriteLine(
    $"以 java 开头 = {commandLine.StartsWith("java ", StringComparison.Ordinal)}，" +
    $"含 -cp = {commandLine.Contains(" -cp ", StringComparison.Ordinal)}，" +
    $"含 mainClass = {commandLine.Contains("net.minecraft.client.main.Main", StringComparison.Ordinal)}，" +
    $"还有占位符 = {commandLine.Contains("${", StringComparison.Ordinal)}");
Console.WriteLine();
string printable = account.AccessToken.Length > 4 && account.AccessToken != "0"
    ? commandLine.Replace(account.AccessToken, "***", StringComparison.Ordinal)
    : commandLine;
Console.WriteLine(printable);

return 0;
