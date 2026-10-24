using System.Text.Json;
using Lx.Probe.Model;
using Lx.Probe.Resolve;

string text = File.ReadAllText("samples/1.20.4.json");

var options = new JsonSerializerOptions();
options.PropertyNameCaseInsensitive = true;

VersionJson v = JsonSerializer.Deserialize<VersionJson>(text, options)!;
var features = new Dictionary<string, bool>();

Console.WriteLine($"id = {v.Id}");
Console.WriteLine($"mainClass = {v.MainClass}");
Console.WriteLine($"assets = {v.Assets}");
Console.WriteLine($"java = {v.JavaVersion?.MajorVersion}");

// Step 3：只打印数量（1.20.4 应为 88）和前 3 个 name。
List<Library>? libraries = v.Libraries;
Console.WriteLine($"libraries = {libraries?.Count ?? 0}");
foreach (Library lib in (libraries ?? new List<Library>()).Take(3))
{
    Console.WriteLine($"  name = {lib.Name}");
}

// M2 Step 1：只算清单，不下载。
List<DownloadItem> manifest = DownloadManifest.Build(v, v.Id ?? "1.20.4", features);
double totalMb = manifest.Sum(i => i.Size) / 1024.0 / 1024.0;
Console.WriteLine($"清单 = {manifest.Count} 个文件，合计 {totalMb:F1} MB");
foreach (DownloadItem item in manifest.Take(3))
{
    Console.WriteLine($"  {item.RelativePath}");
}
Console.WriteLine();

// Step 5：拼 classpath（只打印，不下载）。
// 第 1 个参数 = 游戏安装根目录（libraries / assets / versions 在它下面），第 2 个 = 存档与 natives 所在的游戏目录。
string installRoot = Path.GetFullPath(args.Length > 0 ? args[0] : "minecraft");
string gameRoot = Path.GetFullPath(args.Length > 1 ? args[1] : "minecraft");
string librariesDir = Path.Combine(installRoot, "libraries");
string assetsDir = Path.Combine(installRoot, "assets");
string clientJar = Path.Combine(installRoot, "versions", "1.20.4", "1.20.4.jar");
int failed = Downloader.DownloadAll(manifest, installRoot);
Console.WriteLine($"下载结果：失败 {failed} 个");

// M2 Step 4：下载 assets（index 已经在上面下过，这里解析后再下 3811 个对象）。
string indexFile = Path.Combine(assetsDir, "indexes", (v.Assets ?? "12") + ".json");
List<DownloadItem> assets = DownloadManifest.BuildAssets(indexFile);
Console.WriteLine($"assets 清单 = {assets.Count} 个文件");
int failedAssets = Downloader.DownloadAll(assets, installRoot);
Console.WriteLine($"assets 下载失败 {failedAssets} 个");

List<string> classpath = ClasspathBuilder.Build(
    libraries ?? new List<Library>(),
    librariesDir,
    clientJar,
    features);

string classpathText = string.Join(";", classpath);
Console.WriteLine($"classpath entries = {classpath.Count}（library {classpath.Count - 1} + client 1）");
Console.WriteLine($"classpath length = {classpathText.Length}");
foreach (string entry in classpath.Take(3))
{
    Console.WriteLine($"  {entry}");
}
Console.WriteLine($"classpath 全是绝对路径 = {classpath.All(Path.IsPathRooted)}，全是 .jar = {classpath.All(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))}");

// Step 6：1.20.4 的 natives 是独立的 ":natives-windows" 库，按架构挑出来后解压到 natives/。
string nativesDir = Path.Combine(gameRoot, "natives");
List<string> nativeJars = NativesExtractor.FindJars(libraries ?? new List<Library>(), features, librariesDir);
int missingJars = nativeJars.Count(jar => !File.Exists(jar));
int nativeFiles = NativesExtractor.Extract(nativeJars, nativesDir);
int nativeDlls = Directory.GetFiles(nativesDir, "*.dll", SearchOption.AllDirectories).Length;

Console.WriteLine($"natives jars = {nativeJars.Count}（{NativesExtractor.ArchSuffix}），缺少 {missingJars} 个");
Console.WriteLine($"natives dir = {nativesDir}");
Console.WriteLine($"natives files = {nativeFiles}，dll = {nativeDlls}");

// Step 7：替换 ${...} 占位符，拼出完整命令行（M1 先按离线账号填）。
var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["auth_player_name"] = "Player",
    ["version_name"] = v.Id ?? "1.20.4",
    ["game_directory"] = gameRoot,
    ["assets_root"] = assetsDir,
    ["assets_index_name"] = v.Assets ?? "12",
    ["auth_uuid"] = "00000000000000000000000000000000",
    ["auth_access_token"] = "0",
    ["user_type"] = "legacy",
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
List<string> gameArgs = ArgumentExpander.Expand(v.Arguments?.Game, values, features);
string commandLine = CommandLineBuilder.Build("java", jvmArgs, v.MainClass ?? string.Empty, gameArgs);

Console.WriteLine($"jvm args = {jvmArgs.Count}，game args = {gameArgs.Count}");
Console.WriteLine(
    $"以 java 开头 = {commandLine.StartsWith("java ", StringComparison.Ordinal)}，" +
    $"含 -cp = {commandLine.Contains(" -cp ", StringComparison.Ordinal)}，" +
    $"含 mainClass = {commandLine.Contains("net.minecraft.client.main.Main", StringComparison.Ordinal)}，" +
    $"还有占位符 = {commandLine.Contains("${", StringComparison.Ordinal)}");
Console.WriteLine();
Console.WriteLine(commandLine);
