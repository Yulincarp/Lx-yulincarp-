using Lx.Probe.Instances;
using Lx.Probe.Modpacks;
using Lx.Probe.Mods;
using Lx.Probe.Resolve;

namespace Lx.Probe.Loaders;

// OptiFine 在 Fabric 上的标准做法：Fabric + OptiFabric 模组 + 用户手动放的 OptiFine jar。
// OptiFabric 只发布在 CurseForge，这里用 API key 搜索并装进 mods，再检查 OptiFine jar 在不在。
public static class OptiFineHelper
{
    private const string OptiFabricSlug = "optifabric";

    public static async Task InstallOptiFabricAsync(
        string installRoot,
        string gameVersion,
        string instanceDir,
        Action<string> log,
        string? apiKey = null)
    {
        string? key = CurseForgeClient.ResolveKey(installRoot, apiKey);
        if (key is null)
        {
            log("OptiFabric 只发布在 CurseForge，自动下载需要 API key。");
            log("用 --cf-key <key>（或写进 minecraft\\curseforge-key.txt）后重试；");
            log("或者用 OptiFine 官方安装器装一次，本启动器会把它列进版本列表直接启动。");
            return;
        }

        var file = await CurseForgeClient.FindModFileAsync("optifabric", gameVersion, 4, key, log).ConfigureAwait(false);
        if (file?.DownloadUrl is null)
        {
            log($"CurseForge 上没有支持 {gameVersion} 的 OptiFabric（可能只支持更早的版本）");
            return;
        }

        string modsDir = ModManager.ModsDir(instanceDir);
        Directory.CreateDirectory(modsDir);

        var item = new DownloadItem(
            file.DownloadUrl,
            "mods/" + file.FileName,
            file.Sha1 ?? string.Empty,
            file.Size);

        int failed = await Downloader.DownloadAllAsync(new[] { item }, instanceDir).ConfigureAwait(false);
        if (failed > 0)
        {
            throw new InvalidOperationException("OptiFabric 下载失败");
        }

        log($"已安装 OptiFabric：{file.FileName}");

        bool hasOptiFine = Directory.EnumerateFiles(modsDir, "*.jar")
            .Any(jar => Path.GetFileName(jar).Contains("OptiFine", StringComparison.OrdinalIgnoreCase));

        if (!hasOptiFine)
        {
            log("⚠ 还需要把 OptiFine 的 jar 放进这个 mods 目录（官网下载），然后启动 Fabric 即可。");
            log($"  mods 目录：{modsDir}");
        }
        else
        {
            log("检测到 OptiFine jar，可以直接启动 Fabric。");
        }
    }
}
