using System.Reflection;

namespace Lx.Probe;

// 版本号唯一来源：Directory.Build.props。发布脚本、启动参数、User-Agent 全从这里取。
public static class AppInfo
{
    public static string Version { get; } = Format(typeof(AppInfo).Assembly.GetName().Version);

    public static string UserAgent => $"Lx-Launcher/{Version}";

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
