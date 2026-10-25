using System.Text.Json;

namespace Lx.Probe.Modpacks;

// 记录“这个实例是哪个整合包装的、装了哪些文件”，用于后续更新和清理。
public sealed record PackRecord(
    string SourcePath,
    string PackName,
    string PackVersion,
    List<string> Files)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string PathFor(string instanceDir) => Path.Combine(instanceDir, "pack.json");

    public static PackRecord? Load(string instanceDir)
    {
        string path = PathFor(instanceDir);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PackRecord>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(string instanceDir, PackRecord record)
    {
        Directory.CreateDirectory(instanceDir);
        File.WriteAllText(PathFor(instanceDir), JsonSerializer.Serialize(record, Options));
    }
}
