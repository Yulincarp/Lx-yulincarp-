namespace Lx.Probe.Resolve;

// 把参数列表拼成一行能直接粘进 PowerShell 的命令。
public static class CommandLineBuilder
{
    public static string Build(string javaExe, IEnumerable<string> jvmArgs, string mainClass, IEnumerable<string> gameArgs)
    {
        var parts = new List<string> { javaExe };
        parts.AddRange(jvmArgs);
        parts.Add(mainClass);
        parts.AddRange(gameArgs);
        return string.Join(" ", parts.Select(Quote));
    }

    // 空串必须留成 ""，否则后面的参数会整体错位；带空格或分号的加引号。
    private static string Quote(string arg)
    {
        if (arg.Length == 0)
        {
            return "\"\"";
        }

        return arg.Contains(' ') || arg.Contains(';') || arg.Contains('"') ? $"\"{arg}\"" : arg;
    }
}
