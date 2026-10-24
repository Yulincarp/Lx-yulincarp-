using System.Text.Json;

namespace Lx.Probe.Model;

// 启动器设置：内存分配和 Java 选择。存在 minecraft/settings.json。
public sealed record LaunchSettings(
    int MinMemoryMb = 1024,
    int MaxMemoryMb = 4096,
    string? JavaPath = null)
{
    public const int MinAllowedMb = 256;
    public const int MaxAllowedMb = 65536;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string PathFor(string installRoot) => Path.Combine(installRoot, "settings.json");

    public static LaunchSettings Load(string installRoot)
    {
        string path = PathFor(installRoot);
        if (!File.Exists(path))
        {
            return new LaunchSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<LaunchSettings>(File.ReadAllText(path), Options)
                   ?? new LaunchSettings();
        }
        catch (JsonException)
        {
            return new LaunchSettings();
        }
    }

    public static void Save(string installRoot, LaunchSettings settings)
    {
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(PathFor(installRoot), JsonSerializer.Serialize(settings, Options));
    }
}
