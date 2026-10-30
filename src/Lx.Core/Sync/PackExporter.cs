using System.Text.Json;
using System.Text.Json.Serialization;
using Lx.Probe.Resolve;

namespace Lx.Probe.Sync;

public sealed record PackExportOptions(
    string? PackName = null,
    string? PackVersion = null,
    string? MinecraftVersion = null,
    string? Loader = null,
    string? LoaderVersion = null,
    PackServerInfo? Server = null,
    PackFilter? Filter = null,
    bool CopyFiles = true);

public sealed record PackExportResult(
    PackManifest Manifest,
    string ManifestPath,
    int FileCount,
    long TotalBytes);

// 从实例目录生成可静态托管的整合包：
//   <输出>/pack.json          ← 清单（含每个文件的 sha1 / 大小）
//   <输出>/mods/… config/…    ← 文件本体，URL = 基地址 + 相对路径
// 导出是幂等的：上一次导出、这一次不再属于包的文件会被清掉。
public static class PackExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // 只扫描、不复制：给"先看看要发多大"或测试用。
    public static PackManifest Scan(string instanceDir, PackExportOptions options, CancellationToken cancellationToken = default)
    {
        PackFilter filter = options.Filter ?? new PackFilter();
        string root = Path.GetFullPath(instanceDir);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"找不到实例目录：{instanceDir}");
        }

        var entries = new List<PackFileEntry>();
        foreach (string relative in filter.Enumerate(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            entries.Add(new PackFileEntry(relative, Hasher.Sha1Of(full), new FileInfo(full).Length));
        }

        return new PackManifest
        {
            PackName = string.IsNullOrWhiteSpace(options.PackName) ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) : options.PackName!,
            PackVersion = options.PackVersion ?? string.Empty,
            MinecraftVersion = options.MinecraftVersion,
            Loader = options.Loader,
            LoaderVersion = options.LoaderVersion,
            Server = options.Server,
            Files = entries.Select(entry => entry.Path).ToList(),
            Entries = entries,
        }.Normalize();
    }

    public static PackExportResult Export(
        string instanceDir,
        string outputDir,
        PackExportOptions options,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(instanceDir);
        string target = Path.GetFullPath(outputDir);
        if (IsSameOrInside(target, source))
        {
            throw new InvalidOperationException("导出目录不能是实例目录自己或它的子目录");
        }

        PackManifest manifest = Scan(source, options, cancellationToken);
        Directory.CreateDirectory(target);

        if (options.CopyFiles)
        {
            long copied = 0;
            foreach (PackFileEntry entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string from = Path.Combine(source, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                string to = Path.Combine(target, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: true);
                copied += entry.Size;
            }

            // 上一版导出过、这一版没有的文件：从输出目录删掉，避免托管方带着过期文件。
            int removed = RemoveStale(target, manifest, log);
            log?.Invoke($"导出完成：{manifest.Entries.Count} 个文件，{copied / 1024.0 / 1024.0:F1} MB，清理旧文件 {removed} 个");
        }

        string manifestPath = PackManifest.PathFor(target);
        manifest.Save(manifestPath);
        log?.Invoke($"清单已写入 {manifestPath}（{manifest.PackName} {manifest.PackVersion}）");

        return new PackExportResult(manifest, manifestPath, manifest.Entries.Count, manifest.Entries.Sum(entry => entry.Size));
    }

    // 把清单写到已有的托管目录（比如只改了服务器地址、不想重传文件）。
    public static void WriteManifestOnly(string outputDir, PackManifest manifest, Action<string>? log = null)
    {
        Directory.CreateDirectory(outputDir);
        manifest.Save(PackManifest.PathFor(outputDir));
        log?.Invoke($"清单已更新：{PackManifest.PathFor(outputDir)}");
    }

    private static int RemoveStale(string outputDir, PackManifest current, Action<string>? log)
    {
        PackManifest? previous = PackManifest.TryLoad(PackManifest.PathFor(outputDir));
        if (previous is null)
        {
            return 0;
        }

        var keep = current.AllPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        foreach (string path in previous.AllPaths)
        {
            if (keep.Contains(path))
            {
                continue;
            }

            string full = Path.Combine(outputDir, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                File.Delete(full);
                removed++;
                RemoveEmptyParents(outputDir, full);
            }
        }

        if (removed > 0)
        {
            log?.Invoke($"[清理] 删除了 {removed} 个上一版遗留文件");
        }

        return removed;
    }

    private static void RemoveEmptyParents(string root, string file)
    {
        string? dir = Path.GetDirectoryName(file);
        while (dir is not null && IsSameOrInside(dir, root) && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.EnumerateFileSystemEntries(dir).Any())
            {
                return;
            }

            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    private static bool IsSameOrInside(string candidate, string parent)
    {
        string child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return string.Equals(child, root, StringComparison.OrdinalIgnoreCase) ||
               child.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
