using System.Text.Json;

namespace Lx.Probe.Instances;

// 一个实例 = 一个独立的游戏目录（存档、mods、options 各自独立），
// 共享安装根下的 libraries / assets / versions。
public sealed record Instance(
    string Name,
    string VersionId,
    string? Loader,
    string? AccountName,
    string? LoaderVersion = null);

public static class InstanceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string PathFor(string installRoot) => Path.Combine(installRoot, "instances.json");

    public static string DirFor(string installRoot, string name) => Path.Combine(installRoot, "instances", name);

    public static List<Instance> Load(string installRoot)
    {
        string path = PathFor(installRoot);
        if (!File.Exists(path))
        {
            return new List<Instance>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<Instance>>(File.ReadAllText(path), Options)
                   ?? new List<Instance>();
        }
        catch (JsonException)
        {
            return new List<Instance>();
        }
    }

    public static void SaveAll(string installRoot, IEnumerable<Instance> instances)
    {
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(PathFor(installRoot), JsonSerializer.Serialize(instances.ToList(), Options));
    }

    public static void AddOrUpdate(string installRoot, Instance instance)
    {
        List<Instance> all = Load(installRoot);
        all.RemoveAll(existing => string.Equals(existing.Name, instance.Name, StringComparison.OrdinalIgnoreCase));
        all.Add(instance);
        SaveAll(installRoot, all);
        Directory.CreateDirectory(DirFor(installRoot, instance.Name));
    }

    // 只删记录，不删文件——存档和 mods 让用户自己决定。
    public static bool Remove(string installRoot, string name)
    {
        List<Instance> all = Load(installRoot);
        int removed = all.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return false;
        }

        SaveAll(installRoot, all);
        return true;
    }

    public static string ValidateName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("实例名不能为空");
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains('/') || trimmed.Contains('\\'))
        {
            throw new InvalidOperationException("实例名不能包含路径字符");
        }

        return trimmed;
    }
}
