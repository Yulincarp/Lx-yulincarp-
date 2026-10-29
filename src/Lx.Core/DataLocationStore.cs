using System.Text.Json;

namespace Lx.Probe;

// 数据目录指针优先放 %APPDATA%\Lx yulin carp；
// 如果 AppData 被策略/沙箱拒绝（本机就是），回退到 exe 同级目录，再回退 LocalAppData。
// 指针独立于数据目录，所以数据本身移动后仍能找到新位置。
public static class DataLocationStore
{
    private sealed class LocationFile
    {
        public string? DataRoot { get; set; }
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, AppInfo.DataDirectoryName, "data-location.json");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "data-location.json");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, AppInfo.DataDirectoryName, "data-location.json");
        }
    }

    public static string ConfigPath => CandidatePaths().First();

    public static string? Read()
    {
        foreach (string path in CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                LocationFile? file = JsonSerializer.Deserialize<LocationFile>(File.ReadAllText(path));
                if (string.IsNullOrWhiteSpace(file?.DataRoot))
                {
                    continue;
                }

                string root = Path.GetFullPath(file.DataRoot);
                if (Directory.Exists(root))
                {
                    return root;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // 继续尝试下一个候选
            }
        }

        return null;
    }

    public static void Write(string dataRoot)
    {
        string json = JsonSerializer.Serialize(
            new LocationFile { DataRoot = Path.GetFullPath(dataRoot) },
            new JsonSerializerOptions { WriteIndented = true });

        Exception? last = null;
        foreach (string path in CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
            }
        }

        throw new IOException("无法写入数据目录指针：%APPDATA%、exe 目录、LocalAppData 都不可写", last);
    }

    public static void Clear()
    {
        foreach (string path in CandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 清不掉就保留，不影响数据
            }
        }
    }
}
