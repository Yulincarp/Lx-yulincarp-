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
public sealed record LaunchResult(
    string VersionId,
    string GameDir,
    string CommandLine,
    int ManifestCount,
    int AssetCount);

// 完整的启动流程：解析版本 -> 合并 loader -> 下载 -> 体检 -> 拼命令行。
// 不直接启动 java，交给调用方（控制台打印，界面可以 Process.Start）。
public static class LaunchPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<LaunchResult> RunAsync(LaunchOptions options, Action<string> log)
    {
        string installRoot = options.InstallRoot;
        VersionManifest manifest = await ManifestService.EnsureManifestAsync(installRoot).ConfigureAwait(false);

        string versionId = options.VersionId;
        string versionJsonPath = await ManifestService
            .EnsureVersionJsonAsync(manifest, versionId, installRoot)
            .ConfigureAwait(false);

        if (string.Equals(options.Loader, "fabric", StringComparison.OrdinalIgnoreCase))
        {
            log($"正在合并 Fabric（{options.LoaderVersion ?? "最新稳定版"}）…");
            (versionId, versionJsonPath) = await FabricInstaller
                .InstallAsync(versionId, installRoot, options.LoaderVersion)
                .ConfigureAwait(false);
            log($"Fabric 已合并到 {versionId}");
        }

        VersionJson version = JsonSerializer.Deserialize<VersionJson>(File.ReadAllText(versionJsonPath), JsonOptions)!;
        var features = new Dictionary<string, bool>();

        List<DownloadItem> manifestItems = DownloadManifest.Build(version, versionId, features);
        log($"清单：{manifestItems.Count} 个文件，共 {manifestItems.Sum(i => i.Size) / 1024.0 / 1024.0:F1} MB");
        int failed = await Downloader.DownloadAllAsync(manifestItems, installRoot).ConfigureAwait(false);
        log($"游戏文件下载完成，失败 {failed} 个");

        string assetsDir = Path.Combine(installRoot, "assets");
        string indexFile = Path.Combine(assetsDir, "indexes", (version.Assets ?? "12") + ".json");
        List<DownloadItem> assets = DownloadManifest.BuildAssets(indexFile);
        log($"assets：{assets.Count} 个对象");
        int failedAssets = await Downloader.DownloadAllAsync(assets, installRoot).ConfigureAwait(false);
        log($"assets 下载完成，失败 {failedAssets} 个");

        var allFiles = new List<DownloadItem>(manifestItems);
        allFiles.AddRange(assets);
        List<DownloadItem> missing = InstallCheck.FindMissing(allFiles, installRoot);
        if (missing.Count > 0)
        {
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
            ["clientid"] = "",
            ["auth_xuid"] = "",
            ["resolution_width"] = "854",
            ["resolution_height"] = "480",
        };

        List<string> jvmArgs = ArgumentExpander.Expand(version.Arguments?.Jvm, values, features);
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
        string commandLine = CommandLineBuilder.Build("java", jvmArgs, version.MainClass ?? string.Empty, gameArgs);

        return new LaunchResult(versionId, gameRoot, commandLine, manifestItems.Count, assets.Count);
    }
}
