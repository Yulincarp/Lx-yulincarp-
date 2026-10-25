namespace Lx.Probe.Migration;

public sealed record DataMigrationProgress(string CurrentFile, int FilesCopied, long BytesCopied);

public sealed record DataMigrationResult(
    int FilesCopied,
    long BytesCopied,
    int FilesSkipped,
    IReadOnlyList<string> Errors);

// 把旧版数据目录（%APPDATA%Lx 或开发版 minecraft）复制到新版数据目录。
// 默认不覆盖已有文件；日志/临时文件/更新缓存不迁移。
public static class DataMigrationService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs",
        "crash-reports",
        ".tmp",
        "updates",
        "installers",
    };

    public static string? GuessOldDataRoot()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] candidates =
        {
            Path.Combine(appData, "Lx"),
            Path.Combine(appData, "Lx Launcher"),
        };

        return candidates.FirstOrDefault(IsLikelyDataRoot);
    }

    public static bool IsLikelyDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, "accounts.json")) ||
               File.Exists(Path.Combine(path, "instances.json")) ||
               Directory.Exists(Path.Combine(path, "versions")) ||
               Directory.Exists(Path.Combine(path, "libraries")) ||
               Directory.Exists(Path.Combine(path, "assets"));
    }

    public static async Task<DataMigrationResult> MigrateAsync(
        string source,
        string target,
        bool overwrite = false,
        IProgress<DataMigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        string targetRoot = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);

        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"旧数据目录不存在：{sourceRoot}");
        }

        if (!IsLikelyDataRoot(sourceRoot))
        {
            throw new InvalidOperationException("这个目录不像 Lx 数据目录（缺少 accounts.json / versions / libraries / assets）");
        }

        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("新旧数据目录不能是同一个");
        }

        if (targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            sourceRoot.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("新旧数据目录不能互相嵌套");
        }

        Directory.CreateDirectory(targetRoot);
        int copied = 0;
        int skipped = 0;
        long bytes = 0;
        var errors = new List<string>();

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(sourceRoot, file);
            if (IsExcluded(relative))
            {
                continue;
            }

            string destination = Path.Combine(targetRoot, relative);
            if (File.Exists(destination) && !overwrite)
            {
                skipped++;
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (FileStream input = File.OpenRead(file))
                await using (FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
                }

                long length = new FileInfo(destination).Length;
                copied++;
                bytes += length;
                progress?.Report(new DataMigrationProgress(relative, copied, bytes));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{relative}: {ex.Message}");
            }
        }

        progress?.Report(new DataMigrationProgress("完成", copied, bytes));
        return new DataMigrationResult(copied, bytes, skipped, errors);
    }

    private static bool IsExcluded(string relative)
    {
        string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => ExcludedDirectories.Contains(part));
    }
}
