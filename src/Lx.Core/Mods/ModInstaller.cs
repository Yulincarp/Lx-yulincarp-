using Lx.Probe.Resolve;

namespace Lx.Probe.Mods;

public sealed record ModInstallProgress(int Done, int Total);

public sealed record ModInstallItem(
    string ProjectId,
    string ProjectTitle,
    string VersionNumber,
    string FileName,
    string Url,
    string? Sha1,
    long Size,
    bool IsDependency);

// Modrinth 安装：选最新兼容版本 -> 递归 required 依赖 -> 去重 -> 交给 Downloader。
public static class ModInstaller
{
    public static async Task<List<ModInstallItem>> ResolveAsync(
        string projectId,
        string gameVersion,
        string loader,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<ModInstallItem>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await AddAsync(projectId, null, false);
        return items;

        async Task AddAsync(string? id, string? versionId, bool dependency)
        {
            ModVersionInfo? version = null;

            if (!string.IsNullOrEmpty(versionId))
            {
                version = await ModrinthClient.GetVersionAsync(versionId, cancellationToken).ConfigureAwait(false);
            }

            if (version is null && !string.IsNullOrEmpty(id))
            {
                List<ModVersionInfo> versions = await ModrinthClient
                    .GetVersionsAsync(id, gameVersion, loader, cancellationToken)
                    .ConfigureAwait(false);
                version = versions.FirstOrDefault(v =>
                              string.Equals(v.VersionType, "release", StringComparison.OrdinalIgnoreCase))
                          ?? versions.FirstOrDefault();
            }

            if (version?.File is null)
            {
                log?.Invoke($"[mods] 找不到可安装版本：{id ?? versionId}");
                return;
            }

            if (!visited.Add(version.ProjectId))
            {
                return;
            }

            if (!titles.TryGetValue(version.ProjectId, out string? title))
            {
                title = await ModrinthClient.GetProjectTitleAsync(version.ProjectId).ConfigureAwait(false);
                titles[version.ProjectId] = title;
            }

            items.Add(new ModInstallItem(
                version.ProjectId,
                title,
                version.VersionNumber,
                version.File.FileName,
                version.File.Url,
                version.File.Sha1,
                version.File.Size,
                dependency));

            log?.Invoke($"[mods] {(dependency ? "依赖" : "安装")}：{title} {version.VersionNumber}");

            foreach (ModDependencyInfo required in version.Dependencies.Where(d =>
                         string.Equals(d.DependencyType, "required", StringComparison.OrdinalIgnoreCase)))
            {
                await AddAsync(required.ProjectId, required.VersionId, true);
            }
        }
    }

    public static async Task<int> InstallAsync(
        string projectId,
        string gameVersion,
        string loader,
        string installRoot,
        string modsDir,
        Action<string> log,
        IProgress<ModInstallProgress>? progress = null,
        Action<DownloadFailure>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        List<ModInstallItem> items = await ResolveAsync(projectId, gameVersion, loader, log, cancellationToken)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            throw new InvalidOperationException("没有找到可安装的文件");
        }

        Directory.CreateDirectory(modsDir);

        var downloads = new Dictionary<string, DownloadItem>(StringComparer.OrdinalIgnoreCase);
        foreach (ModInstallItem item in items)
        {
            string destination = Path.Combine(modsDir, item.FileName);
            string relative = Path.GetRelativePath(installRoot, destination);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"mods 目录不在安装目录下：{modsDir}");
            }

            downloads[relative] = new DownloadItem(item.Url, relative, item.Sha1 ?? string.Empty, item.Size);
        }

        int failed = await Downloader.DownloadAllAsync(
            downloads.Values.ToList(),
            installRoot,
            4,
            (done, total) => progress?.Report(new ModInstallProgress(done, total)),
            onFailure,
            cancellationToken).ConfigureAwait(false);

        log($"[mods] 下载完成：{items.Count} 个文件，失败 {failed} 个");
        return failed;
    }
}
