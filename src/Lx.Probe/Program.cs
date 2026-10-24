using System.Text.Json;
using Lx.Probe.Model;
using Lx.Probe.Resolve;

string text = File.ReadAllText("samples/1.20.4.json");

var options = new JsonSerializerOptions();
options.PropertyNameCaseInsensitive = true;

VersionJson v = JsonSerializer.Deserialize<VersionJson>(text, options)!;

Console.WriteLine($"id = {v.Id}");
Console.WriteLine($"mainClass = {v.MainClass}");
Console.WriteLine($"assets = {v.Assets}");
Console.WriteLine($"java = {v.JavaVersion?.MajorVersion}");

// Step 3：只打印数量（1.20.4 应为 88）和前 3 个 name。
List<Library>? libraries = v.Libraries;
Console.WriteLine($"libraries = {libraries?.Count ?? 0}");
if (libraries is { Count: > 0 })
{
    foreach (Library lib in libraries.Take(3))
    {
        Console.WriteLine($"  name = {lib.Name}");
    }
}

// Step 5：拼 classpath（只打印，不下载）。游戏根目录先用 Lx/minecraft。
string gameRoot = Path.GetFullPath("minecraft");
string librariesDir = Path.Combine(gameRoot, "libraries");
string clientJar = Path.Combine(gameRoot, "versions", "1.20.4", "1.20.4.jar");

List<string> classpath = ClasspathBuilder.Build(
    libraries ?? new List<Library>(),
    librariesDir,
    clientJar,
    new Dictionary<string, bool>());

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
List<string> nativeJars = NativesExtractor.FindJars(
    libraries ?? new List<Library>(),
    new Dictionary<string, bool>(),
    librariesDir);

int missingJars = nativeJars.Count(jar => !File.Exists(jar));
int nativeFiles = NativesExtractor.Extract(nativeJars, nativesDir);
int nativeDlls = Directory.GetFiles(nativesDir, "*.dll", SearchOption.AllDirectories).Length;

Console.WriteLine($"natives jars = {nativeJars.Count}（{NativesExtractor.ArchSuffix}），缺少 {missingJars} 个");
Console.WriteLine($"natives dir = {nativesDir}");
Console.WriteLine($"natives files = {nativeFiles}，dll = {nativeDlls}");
foreach (string dll in Directory.GetFiles(nativesDir, "*.dll", SearchOption.TopDirectoryOnly).OrderBy(n => n).Take(5))
{
    Console.WriteLine($"  {Path.GetFileName(dll)}");
}