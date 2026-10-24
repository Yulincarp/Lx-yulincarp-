using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Lx.Probe.Loaders;

// Forge / NeoForge：下载官方 installer，用 `--installClient` 跑一遍，产出本地 version.json。
// 之后启动时用 --version <生成的 id> 就行（它自带 inheritsFrom）。
public static class ForgeInstaller
{
    private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

    // 有些 maven（Cloudflare 后面）对 HTTP/2 支持不好，会一直挂着，强制 HTTP/1.1。
    private static readonly HttpClient Http = new(new HttpClientHandler())
    {
        Timeout = TimeSpan.FromSeconds(120),
        DefaultRequestVersion = System.Net.HttpVersion.Version11,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };

    public static async Task<string> InstallAsync(
        string minecraftVersion,
        string installRoot,
        string loader,
        string? loaderVersion,
        Action<string> log)
    {
        string installerPath = loader.ToLowerInvariant() switch
        {
            "forge" => await DownloadForgeAsync(minecraftVersion, loaderVersion, installRoot, log).ConfigureAwait(false),
            "neoforge" => await DownloadNeoForgeAsync(minecraftVersion, loaderVersion, installRoot, log).ConfigureAwait(false),
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
                .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
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

    private static async Task<string> DownloadNeoForgeAsync(string minecraftVersion, string? loaderVersion, string installRoot, Action<string> log)
    {
        log("查询 NeoForge 版本列表…");
        string xml = await Http.GetStringAsync(NeoForgeMetadata).ConfigureAwait(false);
        List<string> versions = ReadVersions(xml);

        string prefix = NeoForgePrefix(minecraftVersion);
        string? picked = loaderVersion;
        if (picked is null)
        {
            picked = versions
                .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                .LastOrDefault();
        }

        if (picked is null)
        {
            throw new InvalidOperationException($"NeoForge 没有支持 {minecraftVersion} 的版本（找的版本前缀是 {prefix}）");
        }

        log($"选择 NeoForge {picked}");
        string url = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{picked}/neoforge-{picked}-installer.jar";
        return await DownloadInstallerAsync(url, installRoot, $"neoforge-{picked}-installer.jar", log).ConfigureAwait(false);
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
