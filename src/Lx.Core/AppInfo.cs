using System.Reflection;

namespace Lx.Probe;

// 版本号唯一来源：Directory.Build.props。发布脚本、启动参数、User-Agent 全从这里取。
public static class AppInfo
{
    public const string ProductName = "Lx yulin carp";
    // 指针文件 / 日志用：%APPDATA% 与 %LOCALAPPDATA% 下的产品目录。
    public const string DataDirectoryName = "Lx yulin carp";
    // 默认数据目录是 %APPDATA%\Lx yulin carp\.minecraft，与常见启动器的 .minecraft 布局一致。
    public const string DataFolderName = ".minecraft";
    public const string ExecutableName = "Lx yulin carp.exe";

    public static string Version { get; } = Format(typeof(AppInfo).Assembly.GetName().Version);

    public static string UserAgent => $"Lx-yulin-carp/{Version}";

    private static string Format(Version? version)
    {
        if (version is null)
        {
            return "0.0.0";
        }

        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}.0";
    }
}
