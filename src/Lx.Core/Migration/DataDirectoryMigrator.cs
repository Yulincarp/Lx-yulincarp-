namespace Lx.Probe.Migration;

public sealed record DataDirectoryMoveProgress(string CurrentFile, int FilesCopied, long BytesCopied);

// 更改数据目录：完整复制当前数据到新目录（包括日志），校验文件数/大小后再切换指针。
// 旧目录保留作为备份，不删除。
public static class DataDirectoryMigrator
{
    public static async Task<DataDirectoryMoveProgress> MigrateAsync(
        string source,
        string target,
        IProgress<DataDirectoryMoveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        string targetRoot = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);

        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"当前数据目录不存在：{sourceRoot}");
        }

        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("新数据目录不能和当前数据目录相同");
        }

        if (targetRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            sourceRoot.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("新旧数据目录不能互相嵌套");
        }

        bool targetExisted = Directory.Exists(targetRoot);
        if (targetExisted && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new InvalidOperationException("目标目录不是空的。请选择一个空目录或新建目录，避免覆盖其他文件。");
        }

        Directory.CreateDirectory(targetRoot);
        string[] files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
        int copied = 0;
        long bytes = 0;

        try
        {
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(sourceRoot, file);
                string destination = Path.Combine(targetRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                await using (FileStream input = File.OpenRead(file))
                await using (FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
                }

                copied++;
                bytes += new FileInfo(destination).Length;
                progress?.Report(new DataDirectoryMoveProgress(relative, copied, bytes));
            }

            string[] copiedFiles = Directory.GetFiles(targetRoot, "*", SearchOption.AllDirectories);
            if (copiedFiles.Length != files.Length ||
                copiedFiles.Sum(path => new FileInfo(path).Length) != files.Sum(path => new FileInfo(path).Length))
            {
                throw new IOException("迁移校验失败：目标文件数量或总大小与源目录不一致");
            }
        }
        catch
        {
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(targetRoot))
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }

                if (!targetExisted)
                {
                    Directory.Delete(targetRoot, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 清理失败时保留现场，旧数据不受影响
            }

            throw;
        }

        return new DataDirectoryMoveProgress("完成", copied, bytes);
    }
}
