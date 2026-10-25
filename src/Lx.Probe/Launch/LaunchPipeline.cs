using System.Text.Json;
using Lx.Probe.Auth;
using Lx.Probe.Model;
using Lx.Probe.Resolve;

namespace Lx.Probe.Launch;

// 一次启动需要的全部输入。
public sealed record LaunchOptions(
    string InstallRoot,
    string VersionId,
    string? Loader,
    string? LoaderVersion,
    string? GameDir,
    Account Account);

// 一次启动的产物：命令行 + 关键统计，界面和命令行都用它。
public sealed record LaunchProgress(string Stage, int Done, int Total);

public sealed record LaunchResult(
    string VersionId,
    string GameDir,
    string CommandLine,
    int ManifestCount,
    int AssetCount,
    string JavaPath);

// 完整的启动流程：解析版本 -> 合并 loader -> 下载 -> 体检 -> 拼命令行。
// 不直接启动 java，交给调用方（控制台打印，界面可以 Process.Start）。
public static class LaunchPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<LaunchResult> RunAsync(
        LaunchOptions options,
        Action<string> log,
        IProgress<LaunchProgress>? progress = null,
        Action<DownloadFailure>? onDownloadFailure = null)
    {
        string installRoot = options.InstallRoot;
        LaunchSettings settings = SettingsStore.Load(installRoot);
        var source = new DownloadSourceSettings(settings.DownloadSource, settings.CustomDownloadBase);
        Func<string, string> rewriteUrl = url => DownloadUrlRewriter.Rewrite(url, source);
        log($"下载源：{DownloadUrlRewriter.Describe(source)}");

        VersionManifest manifest = await ManifestService.EnsureManifestAsync(installRoot, rewriteUrl).ConfigureAwait(false);

        string versionId = options.VersionId;
        string versionJsonPath = await ManifestService
            .EnsureVersionJsonAsync(manifest, versionId, installRoot, rewriteUrl)
            .ConfigureAwait(false);

        if (string.Equals(options.Loader, "fabric", StringComparison.OrdinalIgnoreCase))
        {
            log($"正在合并 Fabric（{options.LoaderVersion ?? "最新稳定版"}）…");
            (versionId, versionJsonPath) = await FabricInstaller
                .InstallAsync(versionId, installRoot, options.LoaderVersion)
                .ConfigureAwait(false);
            log($"Fabric 已合并到 {versionId}");
        }

        string versionText = File.ReadAllText(versionJsonPath);
        VersionJson version = JsonSerializer.Deserialize<VersionJson>(versionText, JsonOptions)!;

        // Forge / NeoForge 的 version.json 只含自己的库，靠 inheritsFrom 指向原版，这里合并父版本。
        // Fabric 的合并已经在 FabricInstaller 里做过，跳过以免重复。
        if (!string.Equals(options.Loader, "fabric", StringComparison.OrdinalIgnoreCase) &&
            version.InheritsFrom is { Length: > 0 } parentId)
        {
            string parentPath = Path.Combine(installRoot, "versions", parentId, parentId + ".json");
            if (File.Exists(parentPath))
            {
                string merged = VersionMerger.Merge(File.ReadAllText(parentPath), versionText);
                version = JsonSerializer.Deserialize<VersionJson>(merged, JsonOptions)!;
                log($"已合并父版本 {parentId} 的库与参数");
            }
        }

        var features = new Dictionary<string, bool>();

        // 同一条相对路径只报一次：下载失败后体检还会把它算成"缺失"，两份重复没意义。
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object reportGate = new();

        void ReportFailure(DownloadFailure failure)
        {
            lock (reportGate)
            {
                if (!reported.Add(failure.RelativePath))
                {
                    return;
                }
            }

            onDownloadFailure?.Invoke(failure);
        }

        List<DownloadItem> manifestItems = DownloadManifest.Build(version, versionId, features, rewriteUrl);
        log($"清单：{manifestItems.Count} 个文件，共 {manifestItems.Sum(i => i.Size) / 1024.0 / 1024.0:F1} MB");
        int failed = await Downloader.DownloadAllAsync(
            manifestItems,
            installRoot,
            8,
            (done, total) => progress?.Report(new LaunchProgress("游戏文件", done, total)),
            ReportFailure).ConfigureAwait(false);
        log($"游戏文件下载完成，失败 {failed} 个");

        string assetsDir = Path.Combine(installRoot, "assets");
        string indexFile = Path.Combine(assetsDir, "indexes", (version.Assets ?? "12") + ".json");
        List<DownloadItem> assets = DownloadManifest.BuildAssets(indexFile, rewriteUrl);
        log($"assets：{assets.Count} 个对象");
        int failedAssets = await Downloader.DownloadAllAsync(
            assets,
            installRoot,
            8,
            (done, total) => progress?.Report(new LaunchProgress("assets", done, total)),
            ReportFailure).ConfigureAwait(false);
        log($"assets 下载完成，失败 {failedAssets} 个");

        var allFiles = new List<DownloadItem>(manifestItems);
        allFiles.AddRange(assets);
        List<DownloadItem> missing = InstallCheck.FindMissing(allFiles, installRoot);
        if (missing.Count > 0)
        {
            foreach (DownloadItem item in missing)
            {
                ReportFailure(new DownloadFailure(item.Url, item.RelativePath, null, "文件缺失"));
            }

            foreach (DownloadItem item in missing.Take(10))
            {
                log($"缺文件：{item.RelativePath}  {item.Url}");
            }

            throw new InvalidOperationException($"有 {missing.Count} 个文件缺失，无法启动");
        }

        string gameRoot = options.GameDir is not null
            ? Path.GetFullPath(options.GameDir)
            : Path.Combine(installRoot, "versions", versionId);
        string librariesDir = Path.Combine(installRoot, "libraries");
        string clientVersionId = version.InheritsFrom ?? versionId;
        string clientJar = Path.Combine(installRoot, "versions", clientVersionId, clientVersionId + ".jar");

        List<string> classpath = ClasspathBuilder.Build(
            version.Libraries ?? new List<Library>(),
            librariesDir,
            clientJar,
            features);

        string nativesDir = Path.Combine(gameRoot, "natives");
        List<string> nativeJars = NativesExtractor.FindJars(version.Libraries ?? new List<Library>(), features, librariesDir);
        NativesExtractor.Extract(nativeJars, nativesDir);
        log($"classpath：{classpath.Count} 项，natives：{nativeJars.Count} 个 jar");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["auth_player_name"] = options.Account.Name,
            ["version_name"] = versionId,
            ["game_directory"] = gameRoot,
            ["assets_root"] = assetsDir,
            ["assets_index_name"] = version.Assets ?? "12",
            ["auth_uuid"] = options.Account.Uuid,
            ["auth_access_token"] = options.Account.AccessToken,
            ["user_type"] = options.Account.Type switch
            {
                "microsoft" => "msa",
                "yggdrasil" => "mojang",
                _ => "legacy",
            },
            ["version_type"] = "release",
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = "lx",
            ["launcher_version"] = "0.1",
            ["classpath"] = string.Join(";", classpath),
            ["library_directory"] = librariesDir,
            ["classpath_separator"] = ";",
            ["clientid"] = "",
            ["auth_xuid"] = "",
            ["resolution_width"] = "854",
            ["resolution_height"] = "480",
        };

        List<string> jvmArgs = ArgumentExpander.Expand(version.Arguments?.Jvm, values, features);

        // 这台机器的 %TEMP% 不可写（ImageIO / 部分 mod 会崩），统一指到安装目录里。
        string javaTemp = Path.Combine(installRoot, ".tmp", "java");
        Directory.CreateDirectory(javaTemp);
        if (!jvmArgs.Any(arg => arg.StartsWith("-Djava.io.tmpdir=", StringComparison.OrdinalIgnoreCase)))
        {
            jvmArgs.Insert(0, $"-Djava.io.tmpdir={javaTemp}");
        }

        // Forge/NeoForge 用 -DignoreList 决定哪些 classpath jar 不参与模块化。
        // 官方启动器靠 ${version_name}.jar 忽略原版客户端 jar；我们 --version 用的是 loader 的 id，
        // 所以把它显式追加进去，避免和补丁版 client 的模块冲突。
        string clientFileName = Path.GetFileName(clientJar);
        for (int i = 0; i < jvmArgs.Count; i++)
        {
            if (jvmArgs[i].StartsWith("-DignoreList=", StringComparison.Ordinal) &&
                !jvmArgs[i].Contains(clientFileName, StringComparison.OrdinalIgnoreCase))
            {
                jvmArgs[i] += "," + clientFileName;
                log($"ignoreList 追加 {clientFileName}");
            }
        }
        if (options.Account.Type == "yggdrasil")
        {
            string injector = Path.Combine(installRoot, "authlib-injector.jar");
            if (!File.Exists(injector))
            {
                throw new InvalidOperationException($"缺少 {injector}");
            }

            jvmArgs.Insert(0, $"-javaagent:{injector}={options.Account.ApiRoot}");
        }

        List<string> gameArgs = ArgumentExpander.Expand(version.Arguments?.Game, values, features);

        if (settings.MaxMemoryMb > 0 && !jvmArgs.Any(arg => arg.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase)))
        {
            jvmArgs.Insert(0, $"-Xmx{settings.MaxMemoryMb}M");
        }

        if (settings.MinMemoryMb > 0 && !jvmArgs.Any(arg => arg.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase)))
        {
            jvmArgs.Insert(0, $"-Xms{settings.MinMemoryMb}M");
        }

        int javaMajor = version.JavaVersion?.MajorVersion ?? 8;
        string javaExe;
        if (!string.IsNullOrWhiteSpace(settings.JavaPath) && File.Exists(settings.JavaPath))
        {
            javaExe = settings.JavaPath;
            log($"使用设置里指定的 Java：{javaExe}");
        }
        else
        {
            javaExe = await JavaRuntime.EnsureJavaAsync(installRoot, javaMajor, version.JavaVersion?.Component, log, rewriteUrl).ConfigureAwait(false);
        }
        string commandLine = CommandLineBuilder.Build(javaExe, jvmArgs, version.MainClass ?? string.Empty, gameArgs);

        return new LaunchResult(versionId, gameRoot, commandLine, manifestItems.Count, assets.Count, javaExe);
    }
}
