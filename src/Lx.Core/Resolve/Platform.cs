using System.Runtime.InteropServices;

namespace Lx.Probe.Resolve;

// version.json 的 rules 里 os.name / os.arch 用的写法，和本机平台对应。
public static class Platform
{
    public static string OsName { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
        : "linux";

    public static string OsArch { get; } = RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "x86",
        Architecture.Arm64 => "arm64",
        _ => "x86_64",
    };
}
