using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lx.Probe.Resolve;

namespace Lx.Probe.Loaders;

// Forge / NeoForge：下载官方 installer，用 `--installClient` 跑一遍，产出本地 version.json。
// 之后启动时用 --version <生成的 id> 就行（它自带 inheritsFrom）。
public static class ForgeInstaller
{
    private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    // 国内直连 maven.neoforged.net 基本不通，BMCLAPI 镜像了同一个 maven 目录。
    private const string NeoForgeMirrorBase = "https://bmclapi2.bangbang93.com/maven/";
    private const string NeoForgeMirrorMetadata = NeoForgeMirrorBase + "net/neoforged/neoforge/maven-metadata.xml";
    private const string ForgeMirrorMetadata = NeoForgeMirrorBase + "net/minecraftforge/forge/maven-metadata.xml";

    // 有些 maven（Cloudflare 后面）对 HTTP/2 支持不好，会一直挂着，强制 HTTP/1.1。
    private static readonly HttpClient Http = new(new HttpClientHandler())
    {
        Timeout = TimeSpan.FromSeconds(120),
        DefaultRequestVersion = System.Net.HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };

    static ForgeInstaller()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
    }

    // 给界面用：列出某个 Minecraft 版本可用的 Forge 版本（完整坐标，如 1.20.1-47.2.0，新到旧）。
    public static async Task<List<string>> GetForgeVersionsAsync(string minecraftVersion)
    {
        string xml = await GetTextWithFallbackAsync(ForgeMetadata, ForgeMirrorMetadata, _ => { }).ConfigureAwait(false);
        return ReadVersions(xml)
            .Where(version => version.StartsWith(minecraftVersion + "-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(version => version, Comparer<string>.Create(CompareVersions))
            .ToList();
    }

    // 给界面用：列出某个 Minecraft 版本可用的 NeoForge 版本（如 21.1.264，新到旧）。
    public static async Task<List<string>> GetNeoForgeVersionsAsync(string minecraftVersion)
    {
        string prefix;
        try
        {
            prefix = NeoForgePrefix(minecraftVersion);
        }
        catch (InvalidOperationException)
        {
            return new List<string>(); // 快照/非 1.x 版本没有 NeoForge
        }

        string xml = await GetTextWithFallbackAsync(NeoForgeMetadata, NeoForgeMirrorMetadata, _ => { }).ConfigureAwait(false);
        return ReadVersions(xml)
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(version => !version.Contains('-', StringComparison.Ordinal))
            .OrderByDescending(version => version, Comparer<string>.Create(CompareVersions))
            .ToList();
    }

    public static async Task<string> InstallAsync(
        string minecraftVersion,
        string installRoot,
        string loader,
        string? loaderVersion,
        Action<string> log,
        string? artifactSource = null)
    {
        string installerPath = loader.ToLowerInvariant() switch
        {
            "forge" => await DownloadForgeAsync(minecraftVersion, loaderVersion, installRoot, log).ConfigureAwait(false),
            "neoforge" => await DownloadNeoForgeAsync(minecraftVersion, loaderVersion, installRoot, artifactSource, log).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"不认识的加载器：{loader}（只支持 forge / neoforge）"),
        };

        await RunInstallerAsync(installerPath, installRoot, log).ConfigureAwait(false);

        return FindInstalledVersionId(installRoot, minecraftVersion, loader);
    }

    private static async Task<string> DownloadForgeAsync(string minecraftVersion, string? loaderVersion, string installRoot, Action<string> log)
    {
        log("查询 Forge 版本列表…");
        string xml = await Http.GetStringAsync(ForgeMetadata).ConfigureAwait(false);
        List<string> versions = ReadVersions(xml);

        string? picked = loaderVersion;
        if (picked is null)
        {
            picked = versions
                .Where(version => version.StartsWith(minecraftVersion + "-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(version => version, Comparer<string>.Create(CompareVersions))
                .LastOrDefault();
        }

        if (picked is null)
        {
            throw new InvalidOperationException($"Forge 没有支持 {minecraftVersion} 的版本");
        }

        log($"选择 Forge {picked}");
        string url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{picked}/forge-{picked}-installer.jar";
        return await DownloadInstallerAsync(url, installRoot, $"forge-{picked}-installer.jar", log).ConfigureAwait(false);
    }

    private static async Task<string> DownloadNeoForgeAsync(string minecraftVersion, string? loaderVersion, string installRoot, string? artifactSource, Action<string> log)
    {
        log("查询 NeoForge 版本列表…");
        string xml = await GetTextWithFallbackAsync(NeoForgeMetadata, NeoForgeMirrorMetadata, log).ConfigureAwait(false);
        List<string> versions = ReadVersions(xml);

        string prefix = NeoForgePrefix(minecraftVersion);
        string? picked = loaderVersion;
        if (picked is null)
        {
            picked = versions
                .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Where(version => !version.Contains('-', StringComparison.Ordinal))
                .OrderBy(version => version, Comparer<string>.Create(CompareVersions))
                .LastOrDefault();
        }

        if (picked is null)
        {
            throw new InvalidOperationException($"NeoForge 没有支持 {minecraftVersion} 的版本（找的版本前缀是 {prefix}）");
        }

        log($"选择 NeoForge {picked}");
        string fileName = $"neoforge-{picked}-installer.jar";
        string directUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{picked}/{fileName}";
        string mirrorUrl = $"{NeoForgeMirrorBase}net/neoforged/neoforge/{picked}/{fileName}";
        string installer = await DownloadInstallerWithFallbackAsync(directUrl, mirrorUrl, installRoot, fileName, log).ConfigureAwait(false);
        return PatchNeoForgeInstaller(installer, installRoot, artifactSource, log);
    }

    // 1.20.253 和 1.20.99 这种，必须按数字比，不能按字符串。
    private static int CompareVersions(string left, string right)
    {
        string[] leftParts = left.Split('.', '-');
        string[] rightParts = right.Split('.', '-');
        int length = Math.Max(leftParts.Length, rightParts.Length);

        for (int i = 0; i < length; i++)
        {
            string leftPart = i < leftParts.Length ? leftParts[i] : "0";
            string rightPart = i < rightParts.Length ? rightParts[i] : "0";

            if (int.TryParse(leftPart, out int leftNumber) && int.TryParse(rightPart, out int rightNumber))
            {
                if (leftNumber != rightNumber)
                {
                    return leftNumber.CompareTo(rightNumber);
                }

                continue;
            }

            int text = string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
            if (text != 0)
            {
                return text;
            }
        }

        return 0;
    }

    // 1.20.4 → 20.4.    1.21.1 → 21.1.    1.21 → 21.0.
    private static string NeoForgePrefix(string minecraftVersion)
    {
        Match match = Regex.Match(minecraftVersion, @"^1\.(\d+)(?:\.(\d+))?$");
        if (!match.Success)
        {
            throw new InvalidOperationException($"看不懂的 Minecraft 版本：{minecraftVersion}");
        }

        string minor = match.Groups[1].Value;
        string patch = match.Groups[2].Success ? match.Groups[2].Value : "0";
        return $"{minor}.{patch}.";
    }

    private static List<string> ReadVersions(string xml)
    {
        XDocument document = XDocument.Parse(xml);
        return document.Descendants("version")
            .Select(node => node.Value.Trim())
            .Where(value => value.Length > 0)
            .ToList();
    }

    private static async Task<string> GetTextWithFallbackAsync(string direct, string mirror, Action<string> log)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await Http.GetStringAsync(direct, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            log($"直连失败（{ex.GetType().Name}），改用镜像");
            return await Http.GetStringAsync(mirror).ConfigureAwait(false);
        }
    }

    private static async Task<string> DownloadInstallerWithFallbackAsync(
        string directUrl,
        string mirrorUrl,
        string installRoot,
        string fileName,
        Action<string> log)
    {
        string dir = Path.Combine(installRoot, "installers");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
        {
            log($"安装器已存在：{path}");
            return path;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await Http
                .GetAsync(directUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (Stream input = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            await using (FileStream output = File.Create(path))
            {
                await input.CopyToAsync(output, cts.Token).ConfigureAwait(false);
            }

            log("安装器直连下载成功");
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            log($"直连下载失败（{ex.GetType().Name}），改用镜像");
        }

        log($"从镜像下载安装器：{mirrorUrl}");
        using HttpResponseMessage mirrorResponse = await Http.GetAsync(mirrorUrl).ConfigureAwait(false);
        mirrorResponse.EnsureSuccessStatusCode();
        await using (Stream input = await mirrorResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
        await using (FileStream output = File.Create(path))
        {
            await input.CopyToAsync(output).ConfigureAwait(false);
        }

        log($"安装器已下载：{new FileInfo(path).Length / 1024 / 1024} MB");
        return path;
    }

    // 安装器里的库地址写死成 maven.neoforged.net，这里整包改写成 BMCLAPI 镜像。
    private static string PatchNeoForgeInstaller(string installerPath, string installRoot, string? artifactSource, Action<string> log)
    {
        string patched = Path.Combine(
            Path.GetDirectoryName(installerPath)!,
            Path.GetFileNameWithoutExtension(installerPath) + "-mirror.jar");

        if (File.Exists(patched))
        {
            File.Delete(patched);
        }

        // BMCLAPI 没有 JarJarFileSystems；如果本地（--artifact-source 或 installRoot/libraries）
        // 有别的版本（比如 0.4.1），就用它顶替并把校验值改成对应文件的值。
        string? jarJar = FindArtifact(installRoot, artifactSource, "net/neoforged/JarJarFileSystems");
        if (jarJar is not null)
        {
            log($"JarJarFileSystems 用本地文件顶替：{jarJar}");
        }

        using (ZipArchive source = ZipFile.OpenRead(installerPath))
        using (var patchedStream = File.Create(patched))
        using (var target = new ZipArchive(patchedStream, ZipArchiveMode.Create))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                ZipArchiveEntry copy = target.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using Stream input = entry.Open();
                using Stream output = copy.Open();

                if (entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(input);
                    string text = reader.ReadToEnd()
                        .Replace("https://maven.neoforged.net/releases/", NeoForgeMirrorBase)
                        .Replace("https://maven.neoforged.net/", NeoForgeMirrorBase);

                    if (jarJar is not null && text.Contains("JarJarFileSystems/0.4.0", StringComparison.Ordinal))
                    {
                        text = ReplaceJarJarVersion(text, jarJar);
                    }

                    using var writer = new StreamWriter(output);
                    writer.Write(text);
                }
                else
                {
                    input.CopyTo(output);
                }
            }
        }

        if (jarJar is not null)
        {
            CopyJarJarInto(installRoot, jarJar, log);
        }

        log("已把安装器里的 maven 地址改写为 BMCLAPI 镜像");
        return patched;
    }

    // 在给定目录里找某个 group/artifact 的任意版本 jar（取路径排序最靠后的，通常是最高版本）。
    private static string? FindArtifact(string installRoot, string? artifactSource, string relativeGroupDir)
    {
        var roots = new List<string>();
        if (artifactSource is not null)
        {
            roots.Add(artifactSource);
        }

        roots.Add(Path.Combine(installRoot, "libraries"));

        foreach (string root in roots)
        {
            string dir = Path.Combine(root, relativeGroupDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir))
            {
                continue;
            }

            string? found = Directory
                .EnumerateFiles(dir, "*.jar", SearchOption.AllDirectories)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // 把 JSON 里 JarJar 的 0.4.0 全部换成实际文件版本，并把 artifact 的 sha1/size/url 改成真实值。
    private static string ReplaceJarJarVersion(string json, string jarPath)
    {
        string version = new DirectoryInfo(Path.GetDirectoryName(jarPath)!).Name;
        string fileName = Path.GetFileName(jarPath);

        string text = json
            .Replace("JarJarFileSystems/0.4.0/", $"JarJarFileSystems/{version}/")
            .Replace("JarJarFileSystems-0.4.0.jar", fileName);

        System.Text.Json.Nodes.JsonNode? root = System.Text.Json.Nodes.JsonNode.Parse(text);
        if (root?["libraries"] is System.Text.Json.Nodes.JsonArray libraries)
        {
            string sha1 = Hasher.Sha1Of(jarPath);
            long size = new FileInfo(jarPath).Length;

            foreach (System.Text.Json.Nodes.JsonNode? library in libraries)
            {
                System.Text.Json.Nodes.JsonNode? artifact = library?["downloads"]?["artifact"];
                string? path = artifact?["path"]?.GetValue<string>();
                if (artifact is not null && path is not null &&
                    path.Contains("JarJarFileSystems", StringComparison.OrdinalIgnoreCase))
                {
                    artifact["sha1"] = sha1;
                    artifact["size"] = size;
                    artifact["url"] = NeoForgeMirrorBase + path;
                }
            }

            text = root.ToJsonString();
        }

        return text;
    }

    private static void CopyJarJarInto(string installRoot, string jarPath, Action<string> log)
    {
        string version = new DirectoryInfo(Path.GetDirectoryName(jarPath)!).Name;
        string fileName = Path.GetFileName(jarPath);
        string targetDir = Path.Combine(
            installRoot,
            "libraries",
            "net",
            "neoforged",
            "JarJarFileSystems",
            version);

        Directory.CreateDirectory(targetDir);
        string target = Path.Combine(targetDir, fileName);
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(jarPath).Length)
        {
            File.Copy(jarPath, target, overwrite: true);
            log($"已复制 {fileName} 到 libraries");
        }
    }

    private static async Task<string> DownloadInstallerAsync(string url, string installRoot, string fileName, Action<string> log)
    {
        string dir = Path.Combine(installRoot, "installers");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);

        if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
        {
            log($"安装器已存在：{path}");
            return path;
        }

        log($"下载安装器：{url}");
        using HttpResponseMessage response = await Http.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        await using (FileStream output = File.Create(path))
        {
            await input.CopyToAsync(output).ConfigureAwait(false);
        }

        log($"安装器已下载：{new FileInfo(path).Length / 1024 / 1024} MB");
        return path;
    }

    private static async Task RunInstallerAsync(string installerPath, string installRoot, Action<string> log)
    {
        // Forge 安装器要求目录里有 launcher_profiles.json，否则直接拒绝。
        string profiles = Path.Combine(installRoot, "launcher_profiles.json");
        if (!File.Exists(profiles))
        {
            Directory.CreateDirectory(installRoot);
            await File.WriteAllTextAsync(profiles, "{\"profiles\":{},\"settings\":{},\"version\":3}").ConfigureAwait(false);
        }

        // 安装器后处理阶段会在 %TEMP% 下建目录；这台机器的 Temp 不可写，指到安装目录里。
        string workTemp = Path.Combine(installRoot, ".tmp", "forge-installer");
        Directory.CreateDirectory(workTemp);

        log("运行安装器（第一次会下载很多库，几分钟）…");
        var startInfo = new ProcessStartInfo("java")
        {
            Arguments = $"-jar \"{installerPath}\" --installClient \"{installRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.Environment["TMP"] = workTemp;
        startInfo.Environment["TEMP"] = workTemp;
        startInfo.Environment["TMPDIR"] = workTemp;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("启动 java 安装器失败");

        var output = new List<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.Add(e.Data);
                if (output.Count % 10 == 0)
                {
                    log("  " + e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.Add(e.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // 已经退出了
            }

            throw new InvalidOperationException("安装器超时（10 分钟），检查网络或磁盘");
        }

        if (process.ExitCode != 0)
        {
            string tail = string.Join(Environment.NewLine, output.TakeLast(15));
            throw new InvalidOperationException($"安装器退出码 {process.ExitCode}：{tail}");
        }

        log("安装器执行完成");
    }

    private static string FindInstalledVersionId(string installRoot, string minecraftVersion, string loader)
    {
        string versionsDir = Path.Combine(installRoot, "versions");
        if (!Directory.Exists(versionsDir))
        {
            throw new InvalidOperationException("安装器没有生成 versions 目录");
        }

        string pattern = loader.Equals("neoforge", StringComparison.OrdinalIgnoreCase)
            ? "neoforge-*"
            : $"{minecraftVersion}-forge-*";

        string? found = Directory.EnumerateDirectories(versionsDir, pattern)
            .Where(dir => File.Exists(Path.Combine(dir, Path.GetFileName(dir) + ".json")))
            .OrderByDescending(dir => Directory.GetLastWriteTime(dir))
            .Select(Path.GetFileName)
            .FirstOrDefault();

        return found ?? throw new InvalidOperationException("没找到安装器生成的版本目录");
    }
}
