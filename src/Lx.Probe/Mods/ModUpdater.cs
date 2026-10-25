using Lx.Probe.Resolve;

namespace Lx.Probe.Mods;

// 批量更新：先备份旧 jar，再下载新 jar；下载失败不删旧文件。
public static class ModUpdater
{
    public static async Task<int> ApplyAsync(
        IReadOnlyList<ModUpdate> updates,
        string gameDir,
        Action<string> log,
        IProgress<ModInstallProgress>? progress = null,
        Action<DownloadFailure>? onFailure = null,
        CancellationToken cancellationToken = default)
    {
        string modsDir = ModManager.ModsDir(gameDir);
        Directory.CreateDirectory(modsDir);
        string backupDir = Path.Combine(modsDir, ".lx-backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        int failed = 0;

        for (int i = 0; i < updates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModUpdate update = updates[i];
            string newName = Path.GetFileName(update.NewFileName);
            if (string.IsNullOrWhiteSpace(newName))
            {
                failed++;
                log($"[失败] {update.ProjectTitle}：更新文件名无效");
                continue;
            }

            string oldPath = Path.Combine(modsDir, update.FileName);
            string newPath = Path.Combine(modsDir, newName);

            if (File.Exists(oldPath))
            {
                Directory.CreateDirectory(backupDir);
                string backup = Path.Combine(backupDir, update.FileName);
                File.Copy(oldPath, backup, overwrite: true);
                log($"[备份] {update.FileName} → {backup}");
            }

            string relative = Path.GetRelativePath(gameDir, newPath);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                failed++;
                log($"[失败] {update.ProjectTitle}：目标路径越界");
                continue;
            }

            var item = new DownloadItem(
                update.DownloadUrl,
                relative,
                update.Sha1 ?? string.Empty,
                update.Size);

            int one = await Downloader.DownloadAllAsync(
                new[] { item },
                gameDir,
                4,
                onFailure: onFailure,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (one > 0)
            {
                failed++;
                log($"[失败] {update.ProjectTitle} 下载失败，旧文件保留");
                continue;
            }

            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
            {
                File.Delete(oldPath);
                log($"[删除旧版] {update.FileName}");
            }

            progress?.Report(new ModInstallProgress(i + 1, updates.Count));
            log($"[已更新] {update.ProjectTitle} → {update.LatestVersion}");
        }

        return failed;
    }
}
