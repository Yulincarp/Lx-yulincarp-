using System.Diagnostics;
using System.IO;

namespace Lx.UI;

internal static class ShellHelper
{
    // 直接对目录 ShellExecute 在"以管理员身份运行"时会报 Win32 5（拒绝访问）。
    // 统一显式启动 explorer.exe，管理员/普通权限都可用。
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
        {
            UseShellExecute = true,
        });
    }
}
