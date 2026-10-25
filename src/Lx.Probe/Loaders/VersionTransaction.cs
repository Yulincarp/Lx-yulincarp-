namespace Lx.Probe.Loaders;

// D5：安装器直接写 installRoot。失败时把本次新建的 versions/<id> 删掉，
// 避免留下一个半成品版本；libraries 是共享缓存，失败残留不影响已有版本。
public sealed class VersionTransaction
{
    private readonly string _versionsDir;
    private readonly HashSet<string> _before;

    private VersionTransaction(string versionsDir, HashSet<string> before)
    {
        _versionsDir = versionsDir;
        _before = before;
    }

    public static VersionTransaction Begin(string installRoot)
    {
        string versionsDir = Path.Combine(installRoot, "versions");
        Directory.CreateDirectory(versionsDir);
        var before = Directory.EnumerateDirectories(versionsDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new VersionTransaction(versionsDir, before!);
    }

    public void Commit()
    {
    }

    public void Rollback(Action<string>? log)
    {
        if (!Directory.Exists(_versionsDir))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(_versionsDir))
        {
            string name = Path.GetFileName(directory);
            if (_before.Contains(name))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                log?.Invoke($"回滚：删除未完成的版本目录 {name}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"回滚失败：{name}（{ex.Message}）");
            }
        }
    }
}
