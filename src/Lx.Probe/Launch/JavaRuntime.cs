using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lx.Probe.Resolve;

namespace Lx.Probe.Launch;

// 按版本要求选 Java；本机没有就下载 Mojang 官方运行时（java-runtime-*）。
public static class JavaRuntime
{
    private const string AllRuntimesUrl =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static JavaRuntime()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Lx-Launcher/0.1");
    }

    public static async Task<string> EnsureJavaAsync(
        string installRoot,
        int requiredMajor,
        string? component,
        Action<string> log,
        Func<string, string>? rewriteUrl = null,
        CancellationToken cancellationToken = default)
    {
        string platform = OperatingSystem.IsWindows() ? "windows-x64" : "linux";

        string? installed = FindInstalled(requiredMajor, log);
        if (installed is not null)
        {
            return installed;
        }

        component ??= MapComponent(requiredMajor);
        if (component is null)
        {
            throw new InvalidOperationException($"需要 Java {requiredMajor}，本机没有，也不知道该下哪个运行时");
        }

        string runtimeDir = Path.Combine(installRoot, "runtime", component, platform);
        string exeName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        string exePath = Path.Combine(runtimeDir, "bin", exeName);

        if (File.Exists(exePath))
        {
            return exePath;
        }

        log($"需要 Java {requiredMajor}，本机没有；下载 Mojang 运行时 {component}（首次约 150 MB）…");
        await DownloadRuntimeAsync(runtimeDir, component, platform, log, rewriteUrl, cancellationToken).ConfigureAwait(false);

        if (!File.Exists(exePath))
        {
            throw new InvalidOperationException($"运行时下载完了但没找到 {exePath}");
        }

        return exePath;
    }

    // 8 → jre-legacy，17 → java-runtime-gamma，21 → java-runtime-delta
    private static string? MapComponent(int major) => major switch
    {
        8 => "jre-legacy",
        16 => "java-runtime-alpha",
        17 => "java-runtime-gamma",
        21 => "java-runtime-delta",
        _ => null,
    };

    private static async Task DownloadRuntimeAsync(
        string runtimeDir,
        string component,
        string platform,
        Action<string> log,
        Func<string, string>? rewriteUrl = null,
        CancellationToken cancellationToken = default)
    {
        string allRuntimesUrl = rewriteUrl?.Invoke(AllRuntimesUrl) ?? AllRuntimesUrl;
        JsonNode root = JsonNode.Parse(await Http.GetStringAsync(allRuntimesUrl, cancellationToken).ConfigureAwait(false))!;
        JsonNode? entry = (root[platform]?[component] as JsonArray)?.FirstOrDefault();
        string? manifestUrl = entry?["manifest"]?["url"]?.GetValue<string>();

        if (manifestUrl is null)
        {
            throw new InvalidOperationException($"Mojang 没有给 {platform} 提供运行时 {component}");
        }

        string runtimeManifestUrl = rewriteUrl?.Invoke(manifestUrl) ?? manifestUrl;
        JsonNode manifest = JsonNode.Parse(await Http.GetStringAsync(runtimeManifestUrl, cancellationToken).ConfigureAwait(false))!;
        if (manifest["files"] is not JsonObject files)
        {
            throw new InvalidOperationException("运行时清单格式不对");
        }

        var items = new List<DownloadItem>();
        foreach ((string relative, JsonNode? node) in files)
        {
            if (node?["type"]?.GetValue<string>() != "file")
            {
                continue;
            }

            JsonNode? raw = node["downloads"]?["raw"];
            string? url = raw?["url"]?.GetValue<string>();
            if (url is null)
            {
                continue;
            }

            items.Add(new DownloadItem(
                rewriteUrl?.Invoke(url) ?? url,
                relative.Replace('/', Path.DirectorySeparatorChar),
                raw?["sha1"]?.GetValue<string>() ?? string.Empty,
                raw?["size"]?.GetValue<long>() ?? 0));
        }

        log($"运行时文件：{items.Count} 个");
        int failed = await Downloader.DownloadAllAsync(
            items,
            runtimeDir,
            concurrency: 8,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (failed > 0)
        {
            throw new InvalidOperationException($"运行时下载失败 {failed} 个文件");
        }
    }

    // 列出本机找到的所有 java（设置页用）。
    public static List<(string Path, int? Major)> Discover()
    {
        var result = new List<(string Path, int? Major)>();
        foreach (string candidate in CollectCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            int? major = GetMajorVersion(candidate);
            result.Add((candidate, major));
        }

        return result
            .OrderByDescending(item => item.Major ?? 0)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // 在 JAVA_HOME、PATH、常见安装目录里找符合主版本的 java。
    private static string? FindInstalled(int requiredMajor, Action<string> log)
    {
        var candidates = CollectCandidates();

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            int? major = GetMajorVersion(candidate);
            if (major == requiredMajor)
            {
                log($"使用本机 Java {major}：{candidate}");
                return candidate;
            }
        }

        return null;
    }

    private static List<string> CollectCandidates()
    {
        var candidates = new List<string>();

        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));
        }

        string? pathJava = FindOnPath("java.exe");
        if (pathJava is not null)
        {
            candidates.Add(pathJava);
        }

        string[] roots =
        {
            @"C:\Program Files\Java",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Zulu",
            @"C:\Program Files\BellSoft",
            @"C:\Program Files\Amazon Corretto",
            @"C:\Program Files\Semeru",
            @"C:\Program Files\RedHat",
            @"C:\Program Files (x86)\Java",
        };

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                candidates.AddRange(Directory.EnumerateFiles(root, "java.exe", SearchOption.AllDirectories));
            }
            catch (IOException)
            {
                // 忽略没权限的目录
            }
        }

        return candidates;
    }

    private static string? FindOnPath(string exeName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(dir, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int? GetMajorVersion(string javaPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo(javaPath, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process process = Process.Start(startInfo)!;
            string text = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            Match match = Regex.Match(text, "version \"(\\d+)(?:\\.(\\d+))?");
            if (!match.Success)
            {
                return null;
            }

            int first = int.Parse(match.Groups[1].Value);
            if (first == 1 && match.Groups[2].Success)
            {
                return int.Parse(match.Groups[2].Value);
            }

            return first;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
