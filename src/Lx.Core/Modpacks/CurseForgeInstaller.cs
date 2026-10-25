using Lx.Probe.Mods;
using Lx.Probe.Resolve;

namespace Lx.Probe.Modpacks;

public sealed record CurseForgeInstallItem(
    long ModId,
    string ModName,
    CurseForgeFile File,
    bool IsDependency);

// CurseForge 安装：按 modId 找最新 release 文件，递归 required 依赖，再交给 Downloader。
public static class CurseForgeInstaller
{
    public static async Task<List<CurseForgeInstallItem>> ResolveAsync(
        string projectId,
        string gameVersion,
        string loader,
        string apiKey,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(projectId, out long rootId))
        {
            throw new InvalidOperationException($"CurseForge 项目 id 无效：{projectId}");
        }

        var items = new List<CurseForgeInstallItem>();
        var visited = new HashSet<long>();
        var names = new Dictionary<long, string>();

        await AddAsync(rootId, false);
        return items;

        async Task AddAsync(long modId, bool dependency)
        {
            if (!visited.Add(modId))
            {
                return;
            }

            List<CurseForgeFile> files = await CurseForgeClient
                .GetModFilesAsync(modId, gameVersion, loader, apiKey, cancellationToken)
                .ConfigureAwait(false);

            CurseForgeFile? file = files.FirstOrDefault(f => f.ReleaseType == 1) ?? files.FirstOrDefault();
            if (file is null)
            {
                log?.Invoke($"[cf] 找不到兼容文件：mod {modId}");
                return;
            }

            if (!names.TryGetValue(modId, out string? name))
            {
                name = await CurseForgeClient.GetModNameAsync(modId, apiKey, cancellationToken).ConfigureAwait(false);
                names[modId] = name;
            }

            items.Add(new CurseForgeInstallItem(modId, name, file, dependency));
            log?.Invoke($"[cf] {(dependency ? "依赖" : "安装")}：{name}（{file.FileName}）");

            foreach (CurseForgeDependency required in file.Dependencies?
                         .Where(d => d.RelationType == 3) ?? Enumerable.Empty<CurseForgeDependency>())
            {
                await AddAsync(required.ModId, true);
            }
        }
    }

    public static async Task<int> InstallAsync(
        string projectId,
        string gameVersion,
        string loader,
        string installRoot,
        string modsDir,
        string apiKey,
        Action<string> log,
        IProgress<ModInstallProgress>? progress = null,
        Action<DownloadFailure>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        List<CurseForgeInstallItem> items = await ResolveAsync(
                projectId,
                gameVersion,
                loader,
                apiKey,
                log,
                cancellationToken)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            throw new InvalidOperationException("CurseForge 没有返回可下载文件（可能作者禁止第三方下载）");
        }

        Directory.CreateDirectory(modsDir);
        var downloads = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);
        foreach (CurseForgeInstallItem item in items)
        {
            string fileName = Path.GetFileName(item.File.FileName);
            if (fileName.Length == 0 || string.IsNullOrEmpty(item.File.DownloadUrl))
            {
                continue;
            }

            string destination = Path.Combine(modsDir, fileName);
            string relative = Path.GetRelativePath(installRoot, destination);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"mods 目录不在安装目录下：{modsDir}");
            }

            downloads[relative] = new DownloadItem(
                item.File.DownloadUrl,
                relative,
                item.File.Sha1 ?? string.Empty,
                item.File.Size);
        }

        if (downloads.Count == 0)
        {
            throw new InvalidOperationException("CurseForge 文件没有可用的下载地址");
        }

        int failed = await Downloader.DownloadAllAsync(
            downloads.Values.ToList(),
            installRoot,
            4,
            (done, total) => progress?.Report(new ModInstallProgress(done, total)),
            onFailure,
            cancellationToken).ConfigureAwait(false);

        log($"[cf] 下载完成：{downloads.Count} 个文件，失败 {failed} 个");
        return failed;
    }
}
