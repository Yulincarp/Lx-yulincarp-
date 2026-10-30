namespace Lx.Probe.Instances;

// 实例的"管理动作"：改名、复制、删除。
// 和 InstanceStore（纯记录读写）分开，是因为这些动作要动文件系统，失败时得能说清停在哪一步。
public static class InstanceService
{
    // 复制实例时不带的目录：存档、快照、日志这些属于"这台机器上的玩家自己"，不是包的一部分。
    // 复制实例本来就是为了另开一份，把存档一起复制过去反而容易混淆（两边都以为自己在用同一份世界）。
    private static readonly string[] CopyExcludedDirs =
    {
        "saves",
        ".lx-snapshots",
        "logs",
        "crash-reports",
        "screenshots",
        "backups",
    };

    public static Instance Rename(string installRoot, string oldName, string newName, Action<string>? log = null)
    {
        string target = InstanceStore.ValidateName(newName);
        List<Instance> all = InstanceStore.Load(installRoot);
        Instance existing = all.FirstOrDefault(instance =>
            string.Equals(instance.Name, oldName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"找不到实例：{oldName}");

        if (string.Equals(existing.Name, target, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        if (all.Any(instance => string.Equals(instance.Name, target, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"已经有叫「{target}」的实例了");
        }

        string from = InstanceStore.DirFor(installRoot, existing.Name);
        string to = InstanceStore.DirFor(installRoot, target);
        if (Directory.Exists(to))
        {
            throw new InvalidOperationException($"目录已经存在，换个名字：{to}");
        }

        if (Directory.Exists(from))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
            log?.Invoke($"目录已改名：{from} → {to}");
        }
        else
        {
            log?.Invoke($"[警告] 实例目录本来就不在：{from}，只改了记录");
        }

        var renamed = existing with { Name = target };
        all.Remove(existing);
        all.Add(renamed);
        InstanceStore.SaveAll(installRoot, all);
        log?.Invoke($"实例已改名：{existing.Name} → {target}");
        return renamed;
    }

    public static Instance Copy(
        string installRoot,
        string sourceName,
        string newName,
        bool includeSaves = false,
        Action<string>? log = null)
    {
        string target = InstanceStore.ValidateName(newName);
        List<Instance> all = InstanceStore.Load(installRoot);
        Instance source = all.FirstOrDefault(instance =>
            string.Equals(instance.Name, sourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"找不到实例：{sourceName}");

        if (all.Any(instance => string.Equals(instance.Name, target, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"已经有叫「{target}」的实例了");
        }

        string from = InstanceStore.DirFor(installRoot, source.Name);
        string to = InstanceStore.DirFor(installRoot, target);
        if (!Directory.Exists(from))
        {
            throw new InvalidOperationException($"找不到实例目录：{from}");
        }

        if (Directory.Exists(to))
        {
            throw new InvalidOperationException($"目录已经存在，换个名字：{to}");
        }

        Directory.CreateDirectory(to);
        int files = CopyDirectory(from, to, includeSaves, log);

        var clone = source with { Name = target };
        all.Add(clone);
        InstanceStore.SaveAll(installRoot, all);
        log?.Invoke($"已复制实例：{source.Name} → {target}（{files} 个文件{(includeSaves ? "，含存档" : "，不含存档")}）");
        return clone;
    }

    // deleteFiles=false 只删记录（保留目录）；true 连目录一起删。
    public static bool Delete(string installRoot, string name, bool deleteFiles, Action<string>? log = null)
    {
        List<Instance> all = InstanceStore.Load(installRoot);
        Instance? target = all.FirstOrDefault(instance =>
            string.Equals(instance.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return false;
        }

        string dir = InstanceStore.DirFor(installRoot, target.Name);
        bool removed = InstanceStore.Remove(installRoot, target.Name);
        log?.Invoke($"已删除实例记录：{target.Name}");

        if (deleteFiles && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
            log?.Invoke($"已删除实例目录：{dir}");
        }
        else if (!deleteFiles)
        {
            log?.Invoke($"文件保留在：{dir}");
        }

        return removed;
    }

    private static int CopyDirectory(string source, string target, bool includeSaves, Action<string>? log)
    {
        int copied = 0;
        var pending = new Stack<(string From, string To)>();
        pending.Push((source, target));

        while (pending.Count > 0)
        {
            (string from, string to) = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(from))
            {
                string name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(to, name), overwrite: true);
                copied++;
            }

            foreach (string dir in Directory.EnumerateDirectories(from))
            {
                string name = Path.GetFileName(dir);
                if (IsExcluded(name, includeSaves, to, target))
                {
                    continue;
                }

                string next = Path.Combine(to, name);
                Directory.CreateDirectory(next);
                pending.Push((dir, next));
            }
        }

        return copied;
    }

    private static bool IsExcluded(string directoryName, bool includeSaves, string currentTarget, string rootTarget)
    {
        // 只跳过实例根下一级的目录：config/saves 之类嵌套配置不动。
        if (!string.Equals(Path.GetFullPath(currentTarget), Path.GetFullPath(rootTarget), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (includeSaves && string.Equals(directoryName, "saves", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CopyExcludedDirs.Any(excluded => string.Equals(excluded, directoryName, StringComparison.OrdinalIgnoreCase));
    }
}
