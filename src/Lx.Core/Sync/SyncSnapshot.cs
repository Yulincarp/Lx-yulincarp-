using System.Text.Json;
using System.Text.Json.Serialization;
using Lx.Probe.Resolve;

namespace Lx.Probe.Sync;

// 快照里的一个文件：BackupPath 为空表示"同步前这个文件不存在"，回滚时直接删掉它。
// Sha1 是同步前的哈希，回滚后用它证明"真的回去了"。
public sealed record SnapshotFile(string Path, string? BackupPath, long Size, string? Sha1 = null);

// 一次同步前的现场：受影响文件的旧内容 + 旧的 pack.json。
// 目录：<实例>/.lx-snapshots/<id>/，默认保留最近 10 次。
public sealed record SyncSnapshot(
    string Id,
    string Directory,
    DateTimeOffset CreatedAt,
    List<SnapshotFile> Files,
    PackManifest? PreviousManifest)
{
    [JsonIgnore]
    public string Display => $"{CreatedAt.LocalDateTime:MM-dd HH:mm:ss}（{Files.Count} 个文件）";
}

public sealed record RollbackResult(SyncSnapshot Snapshot, int Restored, int Mismatches);

public static class SyncSnapshotStore
{
    public const string FolderName = ".lx-snapshots";
    private const int MaxSnapshots = 10;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string RootFor(string instanceDir) => Path.Combine(instanceDir, FolderName);

    public static string DirFor(string instanceDir, string id) =>
        Path.Combine(RootFor(instanceDir), id);

    // 同步前调用：把将要被覆盖/删除的文件整份复制到快照目录。
    public static SyncSnapshot Create(
        string instanceDir,
        IEnumerable<string> affectedPaths,
        PackManifest? previousManifest,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(instanceDir);
        string id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string dir = DirFor(root, id);
        int suffix = 1;
        while (Directory.Exists(dir))
        {
            dir = DirFor(root, $"{id}-{suffix++}");
        }

        Directory.CreateDirectory(Path.Combine(dir, "files"));

        var files = new List<SnapshotFile>();
        foreach (string relative in affectedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = PackManifest.NormalizePath(relative);
            if (path.Length == 0)
            {
                continue;
            }

            string source = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                files.Add(new SnapshotFile(path, null, 0));
                continue;
            }

            string backupRelative = "files/" + path;
            string backup = Path.Combine(dir, backupRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(source, backup, overwrite: true);
            files.Add(new SnapshotFile(path, backupRelative, new FileInfo(source).Length, Hasher.Sha1Of(source)));
        }

        var snapshot = new SyncSnapshot(id, dir, DateTimeOffset.Now, files, previousManifest);
        File.WriteAllText(Path.Combine(dir, "snapshot.json"), JsonSerializer.Serialize(snapshot, Options));
        log?.Invoke($"已创建同步快照 {id}（{files.Count} 个文件）");

        Prune(root, log);
        return snapshot;
    }

    public static IReadOnlyList<SyncSnapshot> List(string instanceDir)
    {
        string root = RootFor(instanceDir);
        if (!Directory.Exists(root))
        {
            return Array.Empty<SyncSnapshot>();
        }

        var snapshots = new List<SyncSnapshot>();
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            SyncSnapshot? snapshot = TryLoad(dir);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ToList();
    }

    public static SyncSnapshot? Latest(string instanceDir) => List(instanceDir).FirstOrDefault();

    private static SyncSnapshot? TryLoad(string dir)
    {
        string file = Path.Combine(dir, "snapshot.json");
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SyncSnapshot>(File.ReadAllText(file), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 回滚：旧文件放回去、同步时新建的文件删掉、pack.json 恢复成同步前那一份。
    public static int Rollback(string instanceDir, string? snapshotId, Action<string>? log = null) =>
        RollbackWithReport(instanceDir, snapshotId, log).Restored;

    public static RollbackResult RollbackWithReport(string instanceDir, string? snapshotId, Action<string>? log = null)
    {
        string root = Path.GetFullPath(instanceDir);
        SyncSnapshot snapshot = Resolve(root, snapshotId);

        int restored = 0;
        foreach (SnapshotFile file in snapshot.Files)
        {
            string target = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(file.BackupPath))
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                    restored++;
                }

                continue;
            }

            string backup = Path.Combine(snapshot.Directory, file.BackupPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(backup))
            {
                log?.Invoke($"[回滚] 快照里缺少 {file.Path} 的备份，跳过");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(backup, target, overwrite: true);
            if (!string.IsNullOrEmpty(file.Sha1) &&
                !string.Equals(Hasher.Sha1Of(target), file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"[回滚] {file.Path} 恢复后 sha1 对不上（快照本身可能已损坏）");
            }

            restored++;
        }

        string manifestPath = PackManifest.PathFor(root);
        if (snapshot.PreviousManifest is not null)
        {
            snapshot.PreviousManifest.Save(manifestPath);
        }
        else if (File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }

        log?.Invoke($"已回滚到快照 {snapshot.Id}：恢复/删除 {restored} 个文件");
        int mismatches = Verify(root, snapshot, log);
        return new RollbackResult(snapshot, restored, mismatches);
    }

    private static SyncSnapshot Resolve(string instanceDir, string? snapshotId)
    {
        if (!string.IsNullOrWhiteSpace(snapshotId))
        {
            SyncSnapshot? exact = TryLoad(DirFor(instanceDir, snapshotId));
            if (exact is null)
            {
                throw new InvalidOperationException($"找不到快照：{snapshotId}");
            }

            return exact;
        }

        return Latest(instanceDir) ?? throw new InvalidOperationException("这个实例还没有同步快照");
    }

    // 快照会复制 mods，体积不小，只留最近若干次。
    private static void Prune(string instanceDir, Action<string>? log)
    {
        IReadOnlyList<SyncSnapshot> all = List(instanceDir);
        foreach (SyncSnapshot old in all.Skip(MaxSnapshots))
        {
            try
            {
                Directory.Delete(old.Directory, recursive: true);
                log?.Invoke($"[快照] 删除过旧快照 {old.Id}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"[快照] 删不掉 {old.Id}：{ex.Message}");
            }
        }
    }

    // 回滚后调用：逐个文件比对"同步前记录的 sha1"，返回不一致/缺失的数量。
    // 0 就代表回到了快照那一刻的状态。
    public static int Verify(string instanceDir, SyncSnapshot snapshot, Action<string>? log = null)
    {
        string root = Path.GetFullPath(instanceDir);
        int bad = 0;
        foreach (SnapshotFile file in snapshot.Files)
        {
            string target = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));

            if (string.IsNullOrEmpty(file.BackupPath))
            {
                if (File.Exists(target))
                {
                    bad++;
                    log?.Invoke($"[校验] {file.Path} 同步前不存在，现在却还在");
                }

                continue;
            }

            if (!File.Exists(target))
            {
                bad++;
                log?.Invoke($"[校验] 回滚后缺少 {file.Path}");
                continue;
            }

            if (!string.IsNullOrEmpty(file.Sha1) &&
                !string.Equals(Hasher.Sha1Of(target), file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                bad++;
                log?.Invoke($"[校验] {file.Path} 内容与快照不一致");
            }
        }

        log?.Invoke(bad == 0 ? "回滚校验通过（sha1 与同步前一致）" : $"回滚校验：{bad} 个文件有问题");
        return bad;
    }
}
