namespace Lx.Probe;

// 数据根目录（libraries / assets / versions / logs / accounts.json 所在处）。
// 顺序：LX_DATA_DIR 环境变量 -> 用户设置的数据目录指针 -> exe 旁边的 portable.txt（data/）-> 开发期仓库 minecraft/ -> %APPDATA%\Lx yulin carp。
// 所有需要"数据放哪"的代码都走这里，别再各写一份。
public static class AppPaths
{
    public static string ResolveInstallRoot()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("LX_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            return Path.GetFullPath(overrideDir);
        }

        // 用户在设置里更改过数据目录：指针在固定的 %APPDATA%\Lx yulin carp 下。
        string? configured = DataLocationStore.Read();
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return EnsureWritableRoot(configured);
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string root = ResolveFor(AppContext.BaseDirectory, appData);

        // 发布版第一次启动：如果 exe 旁边还留着旧的 minecraft/ 目录，尝试整体搬到新数据目录。
        if (!string.IsNullOrWhiteSpace(appData) &&
            !Directory.Exists(root) &&
            root.StartsWith(appData, StringComparison.OrdinalIgnoreCase))
        {
            TryMigrateLegacyData(root);
        }

        return EnsureWritableRoot(root);
    }

    // 数据目录必须真的可写：%APPDATA% 被策略/沙箱拒绝时，退回 exe 旁边的 data。
    private static string EnsureWritableRoot(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            string probe = Path.Combine(root, ".write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 继续尝试 exe 旁边的 data
        }

        string fallback = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(fallback);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 彻底写不了就让上层在写文件时报错
        }

        return fallback;
    }

    private static void TryMigrateLegacyData(string target)
    {
        string legacy = Path.Combine(AppContext.BaseDirectory, "minecraft");
        if (!Directory.Exists(legacy))
        {
            return;
        }

        try
        {
            Directory.Move(legacy, target); // 同一分区时只是改名，不会复制 700 MB
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 跨分区/被占用就留在原地，让用户用 LX_DATA_DIR 或 portable.txt
        }
    }

    // 拆出来方便单测：不读环境变量，baseDir/appDataDir 由调用方给。
    public static string ResolveFor(string baseDir, string appDataDir)
    {
        baseDir = Path.GetFullPath(baseDir);

        if (File.Exists(Path.Combine(baseDir, "portable.txt")))
        {
            return Path.Combine(baseDir, "data");
        }

        // 只有 dotnet run / bin 下的开发版才回仓库找 minecraft，发布版不会误用源码目录。
        if (IsDevBuild(baseDir))
        {
            DirectoryInfo? dir = new(baseDir);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Lx.sln")))
                {
                    return Path.Combine(dir.FullName, "minecraft");
                }

                dir = dir.Parent;
            }
        }

        if (!string.IsNullOrWhiteSpace(appDataDir))
        {
            return Path.Combine(appDataDir, AppInfo.DataDirectoryName);
        }

        return Path.Combine(baseDir, "data");
    }

    public static bool IsPortable =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    private static bool IsDevBuild(string baseDir) =>
        baseDir.Contains(Path.Combine("src", "Lx.UI", "bin"), StringComparison.OrdinalIgnoreCase) ||
        baseDir.Contains(Path.Combine("src", "Lx.Cli", "bin"), StringComparison.OrdinalIgnoreCase);
}
