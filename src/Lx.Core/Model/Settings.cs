using System.Text.Json;
using Lx.Probe.Net;

namespace Lx.Probe.Model;

// 启动器设置：游戏、下载、启动器。存在数据目录/settings.json。
public sealed record LaunchSettings(
    int MinMemoryMb = 1024,
    int MaxMemoryMb = 4096,
    string? JavaPath = null,
    string? DownloadSource = null,
    string? CustomDownloadBase = null,
    string? JvmArgs = null,
    int WindowWidth = 854,
    int WindowHeight = 480,
    bool Fullscreen = false,
    int DownloadConcurrency = 8,
    string? ProxyMode = "system",
    string? ProxyUrl = null,
    string? UpdateChannel = "stable",
    string? Theme = "light",
    // 崩溃上报：默认关闭，只有用户勾选且填了地址才会真的发出去
    bool TelemetryEnabled = false,
    string? CrashReportUrl = null)
{
    public const int MinAllowedMb = 256;
    public const int MaxAllowedMb = 65536;
    public const int MinWindowWidth = 640;
    public const int MinWindowHeight = 480;
    public const int MaxWindowSize = 7680;
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
            var defaults = new LaunchSettings();
            ProxyConfig.Configure(defaults.ProxyMode, defaults.ProxyUrl);
            return defaults;
        }

        try
        {
            LaunchSettings settings = JsonSerializer.Deserialize<LaunchSettings>(File.ReadAllText(path), Options)
                   ?? new LaunchSettings();
            ProxyConfig.Configure(settings.ProxyMode, settings.ProxyUrl);
            return settings;
        }
        catch (JsonException)
        {
            var defaults = new LaunchSettings();
            ProxyConfig.Configure(defaults.ProxyMode, defaults.ProxyUrl);
            return defaults;
        }
    }

    public static void Save(string installRoot, LaunchSettings settings)
    {
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(PathFor(installRoot), JsonSerializer.Serialize(settings, Options));
        ProxyConfig.Configure(settings.ProxyMode, settings.ProxyUrl);
    }
}
