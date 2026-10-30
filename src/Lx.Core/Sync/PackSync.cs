using System.Net.Http;
using Lx.Probe.Net;
using Lx.Probe.Resolve;

namespace Lx.Probe.Sync;

public enum SyncPhase
{
    Loading,
    Planning,
    Snapshot,
    Download,
    Delete,
    Manifest,
    Done,
}

public sealed record PackSyncProgress(SyncPhase Phase, string Message, int Done = 0, int Total = 0);

// 同步计划：要下哪些、要删哪些、已经对上多少。不碰文件，方便界面先预览。
public sealed record PackSyncPlan(
    PackManifest Remote,
    string BaseUrl,
    IReadOnlyList<PackFileEntry> Download,
    IReadOnlyList<string> Delete,
    int Unchanged)
{
    public bool IsEmpty => Download.Count == 0 && Delete.Count == 0;
}

public sealed record PackSyncResult(
    bool Success,
    PackSyncPlan Plan,
    int Downloaded,
    int Deleted,
    int Failed,
    IReadOnlyList<DownloadFailure> Failures,
    string? SnapshotId,
    PackManifest Manifest)
{
    public string Summary => Success
        ? $"同步完成：下载/更新 {Downloaded}，删除 {Deleted}，已是新版 {Plan.Unchanged}"
        : $"同步未完成：下载 {Downloaded}，失败 {Failed}（快照 {SnapshotId} 可回滚）";
}

// 增量同步：读远端 pack.json → 比对本地 sha1 → 快照 → 只下差异 → 删旧文件 → 落新清单。
// 传输只认两种：http(s) 走 Downloader（带断点续传/校验/重试），本地目录直接复制。
public static class PackSync
{
    private static HttpClient Http => ProxyConfig.GetClient(TimeSpan.FromSeconds(120));

    public static bool IsHttp(string location) =>
        location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // 清单位置可以是 URL，也可以是本地文件/目录（目录里找 pack.json）。
    public static async Task<PackManifest> LoadRemoteAsync(string location, CancellationToken cancellationToken = default)
    {
        string target = ResolveManifestLocation(location);
        if (IsHttp(target))
        {
            string json;
            try
            {
                json = await Http.GetStringAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"下载 pack.json 失败：{ex.Message}", ex);
            }

            return PackManifest.Parse(json);
        }

        return PackManifest.Load(target);
    }

    // 清单地址换成"文件基地址"：https://host/pack/pack.json → https://host/pack
    public static string ResolveBaseUrl(string manifestLocation)
    {
        string target = ResolveManifestLocation(manifestLocation);
        if (IsHttp(target))
        {
            int cut = target.LastIndexOf('/');
            return cut > "https://".Length ? target[..cut] : target;
        }

        string full = Path.GetFullPath(target);
        return Path.GetDirectoryName(full) ?? full;
    }

    private static string ResolveManifestLocation(string location)
    {
        string value = (location ?? string.Empty).Trim().Trim('"');
        if (value.Length == 0)
        {
            throw new InvalidOperationException("请先填整合包同步地址（HTTP 或本地目录）");
        }

        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(value).LocalPath;
        }

        if (IsHttp(value))
        {
            return value;
        }

        string full = Path.GetFullPath(value);
        if (Directory.Exists(full))
        {
            return PackManifest.PathFor(full);
        }

        return full;
    }

    public static PackSyncPlan Plan(string instanceDir, PackManifest remote, string baseUrl)
    {
        string root = Path.GetFullPath(instanceDir);
        var downloads = new List<PackFileEntry>();
        int unchanged = 0;

        foreach (PackFileEntry entry in remote.AllEntries)
        {
            string full = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                if (!entry.HasHash)
                {
                    // v0 清单没有哈希：本地有就当对上了，别每次全量重下。
                    unchanged++;
                    continue;
                }

                var info = new FileInfo(full);
                if ((entry.Size <= 0 || info.Length == entry.Size) &&
                    string.Equals(Hasher.Sha1Of(full), entry.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    unchanged++;
                    continue;
                }
            }

            downloads.Add(entry);
        }

        var remotePaths = remote.AllPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deletions = new List<string>();

        // 作者显式列的删除清单
        foreach (string path in remote.Delete)
        {
            if (!remotePaths.Contains(path) && Exists(root, path) && !deletions.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                deletions.Add(path);
            }
        }

        // 上一版清单里有、这一版没有的：自动补进删除清单（比让服主手写可靠）
        PackManifest? previous = PackManifest.TryLoad(PackManifest.PathFor(root));
        if (previous is not null)
        {
            foreach (string path in previous.AllPaths)
            {
                if (!remotePaths.Contains(path) && Exists(root, path) && !deletions.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    deletions.Add(path);
                }
            }
        }

        return new PackSyncPlan(remote, baseUrl, downloads, deletions, unchanged);
    }

    public static async Task<PackSyncResult> SyncAsync(
        string instanceDir,
        string manifestLocation,
        Action<PackSyncProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        onProgress?.Invoke(new PackSyncProgress(SyncPhase.Loading, $"读取清单：{manifestLocation}"));
        PackManifest remote = await LoadRemoteAsync(manifestLocation, cancellationToken).ConfigureAwait(false);
        string baseUrl = ResolveBaseUrl(manifestLocation);
        onProgress?.Invoke(new PackSyncProgress(SyncPhase.Loading, $"清单版本 {remote.PackVersion}（{remote.Entries.Count} 个文件）"));

        PackSyncPlan plan = Plan(instanceDir, remote, baseUrl);
        return await ApplyAsync(instanceDir, plan, manifestLocation, onProgress, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<PackSyncResult> ApplyAsync(
        string instanceDir,
        PackSyncPlan plan,
        string? manifestLocation,
        Action<PackSyncProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(instanceDir);
        Directory.CreateDirectory(root);

        var failures = new List<DownloadFailure>();
        PackManifest? previous = PackManifest.TryLoad(PackManifest.PathFor(root));

        onProgress?.Invoke(new PackSyncProgress(
            SyncPhase.Planning,
            plan.IsEmpty
                ? "本地已经是这个版本"
                : $"需要下载 {plan.Download.Count} 个、删除 {plan.Delete.Count} 个文件"));

        // 就算没有差异也留一份快照：玩家可能只是想"回到同步前"。
        var affected = plan.Download.Select(entry => entry.Path).Concat(plan.Delete).ToList();
        SyncSnapshot snapshot = SyncSnapshotStore.Create(
            root,
            affected,
            previous,
            message => onProgress?.Invoke(new PackSyncProgress(SyncPhase.Snapshot, message)),
            cancellationToken);

        int downloaded = 0;
        int deleted = 0;

        try
        {
            // ---- 下载 ----
            var httpItems = new List<DownloadItem>();
            var localCopies = new List<(PackFileEntry Entry, string Source)>();
            foreach (PackFileEntry entry in plan.Download)
            {
                string url = ResolveUrl(plan.BaseUrl, entry);
                if (IsHttp(url))
                {
                    httpItems.Add(new DownloadItem(url, entry.Path, entry.Sha1, entry.Size));
                }
                else
                {
                    localCopies.Add((entry, url));
                }
            }

            if (httpItems.Count > 0)
            {
                int total = httpItems.Count;
                onProgress?.Invoke(new PackSyncProgress(SyncPhase.Download, $"下载 {total} 个文件", 0, total));
                int failed = await Downloader.DownloadAllAsync(
                    httpItems,
                    root,
                    concurrency: 4,
                    onProgress: (done, count) => onProgress?.Invoke(new PackSyncProgress(SyncPhase.Download, "下载中", done, count)),
                    onFailure: failure => failures.Add(failure),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                downloaded += httpItems.Count - Math.Min(failed, httpItems.Count);
            }

            foreach ((PackFileEntry entry, string source) in localCopies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DownloadFailure? failure = CopyLocal(root, entry, source);
                if (failure is null)
                {
                    downloaded++;
                }
                else
                {
                    failures.Add(failure);
                }
            }

            // ---- 删除（只删清单里的，且删之前已经备份） ----
            foreach (string path in plan.Delete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    continue;
                }

                File.Delete(full);
                deleted++;
                RemoveEmptyParents(root, full);
            }

            if (deleted > 0)
            {
                onProgress?.Invoke(new PackSyncProgress(SyncPhase.Delete, $"删除 {deleted} 个旧文件"));
            }

            // ---- 落清单：失败就不写，保持旧版本，玩家可回滚 ----
            if (failures.Count > 0)
            {
                onProgress?.Invoke(new PackSyncProgress(
                    SyncPhase.Done,
                    $"同步失败 {failures.Count} 个文件，清单未更新；快照 {snapshot.Id} 可回滚"));
                return new PackSyncResult(false, plan, downloaded, deleted, failures.Count, failures, snapshot.Id, previous ?? plan.Remote);
            }

            PackManifest manifest = (plan.Remote with
            {
                SourcePath = previous?.SourcePath ?? manifestLocation,
                Files = plan.Remote.AllPaths.ToList(),
                Entries = plan.Remote.AllEntries.ToList(),
                Delete = new List<string>(),
            }).Normalize();

            manifest.Save(PackManifest.PathFor(root));
            onProgress?.Invoke(new PackSyncProgress(SyncPhase.Manifest, $"清单已更新到 {manifest.PackVersion}"));

            var result = new PackSyncResult(true, plan, downloaded, deleted, 0, failures, snapshot.Id, manifest);
            onProgress?.Invoke(new PackSyncProgress(SyncPhase.Done, result.Summary));
            return result;
        }
        catch (OperationCanceledException)
        {
            onProgress?.Invoke(new PackSyncProgress(SyncPhase.Done, $"已取消；快照 {snapshot.Id} 可回滚"));
            throw;
        }
    }

    // 回滚到上一次（或指定）同步快照。
    public static int Rollback(string instanceDir, string? snapshotId = null, Action<string>? log = null) =>
        SyncSnapshotStore.Rollback(instanceDir, snapshotId, log);

    // 清单里的相对路径 → 实际下载地址；显式 Url 优先，路径按段转义（中文/空格文件名也能下）。
    public static string ResolveUrl(string baseUrl, PackFileEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Url))
        {
            string explicitUrl = entry.Url!.Trim();
            return IsHttp(explicitUrl) ? explicitUrl : Path.Combine(baseUrl, explicitUrl);
        }

        string encoded = string.Join('/', entry.Path.Split('/').Select(Uri.EscapeDataString));
        return IsHttp(baseUrl) ? $"{baseUrl.TrimEnd('/')}/{encoded}" : Path.Combine(baseUrl, entry.Path);
    }

    private static DownloadFailure? CopyLocal(string root, PackFileEntry entry, string source)
    {
        if (!File.Exists(source))
        {
            return new DownloadFailure(source, entry.Path, null, "源文件不存在");
        }

        string dest = Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        string temp = dest + ".part";
        File.Copy(source, temp, overwrite: true);

        if (entry.HasHash &&
            !string.Equals(Hasher.Sha1Of(temp), entry.Sha1, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temp);
            return new DownloadFailure(source, entry.Path, null, "sha1 不符");
        }

        File.Move(temp, dest, overwrite: true);
        return null;
    }

    private static bool Exists(string root, string relativePath) =>
        File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static void RemoveEmptyParents(string root, string file)
    {
        string? dir = Path.GetDirectoryName(file);
        while (dir is not null && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase) &&
               dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.EnumerateFileSystemEntries(dir).Any())
            {
                return;
            }

            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }
}
