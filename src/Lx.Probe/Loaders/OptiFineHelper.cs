using Lx.Probe.Instances;
using Lx.Probe.Mods;
using Lx.Probe.Resolve;

namespace Lx.Probe.Loaders;

// OptiFine 在 Fabric 上的标准做法：Fabric + OptiFabric 模组 + 用户手动放的 OptiFine jar。
// 这里负责把 OptiFabric 从 Modrinth 装进 mods，并检查 OptiFine jar 在不在。
public static class OptiFineHelper
{
    private const string OptiFabricSlug = "optifabric";

    public static async Task InstallOptiFabricAsync(
        string installRoot,
        string gameVersion,
        string instanceDir,
        Action<string> log)
    {
        var file = await ModrinthClient.GetLatestFileAsync(OptiFabricSlug, gameVersion, "fabric").ConfigureAwait(false);
        if (file is null)
        {
            log("OptiFabric 没有发布在 Modrinth 上（同名项目是整合包，不是模组）。");
            log("推荐做法：用 OptiFine 官方安装器装一次，它会生成 versions/<id>/ 版本目录；");
            log("本启动器的版本下拉框会自动列出它，选它启动即可（兼容 inheritsFrom 合并）。");
            return;
        }

        string modsDir = ModManager.ModsDir(instanceDir);
        Directory.CreateDirectory(modsDir);

        var item = new DownloadItem(
            file.Value.Url,
            "mods/" + file.Value.FileName,
            file.Value.Sha1 ?? string.Empty,
            file.Value.Size);

        int failed = await Downloader.DownloadAllAsync(new[] { item }, instanceDir).ConfigureAwait(false);
        if (failed > 0)
        {
            throw new InvalidOperationException("OptiFabric 下载失败");
        }

        log($"已安装 OptiFabric：{file.Value.FileName}");

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
