namespace Lx.Probe;

// 安装根目录（libraries / assets / versions 所在处）。
// 开发期从 exe 所在目录往上找 Lx.sln；发布版会改成 %APPDATA%\Lx 或便携模式（见 ROADMAP C2）。
// 所有需要"安装在哪"的代码都走这里，别再各写一份。
public static class AppPaths
{
    public static string ResolveInstallRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lx.sln")))
            {
                return Path.Combine(dir.FullName, "minecraft");
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath("minecraft");
    }
}
