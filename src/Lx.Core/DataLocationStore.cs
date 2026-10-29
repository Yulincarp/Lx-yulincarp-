using System.Text.Json;

namespace Lx.Probe;

// 数据目录指针放在固定的 %APPDATA%\Lx yulin carp 下，和数据本身分开。
// 这样数据目录被移动后，下次启动仍能找到新位置。
public static class DataLocationStore
{
    private sealed class LocationFile
    {
        public string? DataRoot { get; set; }
    }

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.DataDirectoryName,
        "data-location.json");

    public static string? Read()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return null;
            }

            LocationFile? file = JsonSerializer.Deserialize<LocationFile>(File.ReadAllText(ConfigPath));
            return string.IsNullOrWhiteSpace(file?.DataRoot) ? null : Path.GetFullPath(file.DataRoot);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string dataRoot)
    {
        string path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        string json = JsonSerializer.Serialize(
            new LocationFile { DataRoot = Path.GetFullPath(dataRoot) },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清不掉就保留，不影响数据
        }
    }
}
