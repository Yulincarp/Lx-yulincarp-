using System.IO.Enumeration;

namespace Lx.Probe.Sync;

// 哪些文件算"整合包的一部分"：
//   默认只收 mods / config / 脚本这类服主想统一的东西；
//   存档、日志、按键设置、快照目录永远不碰，避免同步把玩家自己的东西覆盖掉。
public sealed record PackFilter(
    IReadOnlyList<string>? Includes = null,
    IReadOnlyList<string>? Excludes = null)
{
    public static readonly IReadOnlyList<string> DefaultIncludes = new[]
    {
        "mods",
        "config",
        "defaultconfigs",
        "kubejs",
        "scripts",
        "datapacks",
        "patchouli_books",
        "customnpcs",
    };

    public static readonly IReadOnlyList<string> DefaultExcludes = new[]
    {
        "saves",
        "logs",
        "crash-reports",
        "screenshots",
        "backups",
        "backup",
        ".lx-snapshots",
        ".cache",
        ".mixin.out",
        "local",
        "resourcepacks",
        "shaderpacks",
        "XaeroWorldMap",
        "XaeroWaypoints",
        "journeymap",
        "schematics",
        "options.txt",
        "optionsof.txt",
        "servers.dat",
        "servers.dat_old",
        "usercache.json",
        "realms_persistence.json",
        "pack.json",
        "*.log",
        "*.bak",
        "*.tmp",
        "*.part",
        "*.part.meta",
        "*.disabled",
    };

    public IReadOnlyList<string> IncludePatterns => Includes ?? DefaultIncludes;

    public IReadOnlyList<string> ExcludePatterns => Excludes ?? DefaultExcludes;

    // 相对路径（'/' 分隔，不含盘符），目录前缀与通配符两种写法都支持。
    public bool IsIncluded(string relativePath)
    {
        string path = PackManifest.NormalizePath(relativePath);
        if (path.Length == 0 || string.Equals(path, "pack.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExcludePatterns.Any(pattern => Matches(pattern, path)))
        {
            return false;
        }

        return IncludePatterns.Any(pattern => Matches(pattern, path));
    }

    // "config" 命中 "config/foo.txt"；"*.jar" 命中任意层级的 jar；"mods/*.jar" 限定层级。
    private static bool Matches(string rawPattern, string path)
    {
        string pattern = PackManifest.NormalizePath(rawPattern.TrimStart('/'));
        if (pattern.Length == 0)
        {
            return false;
        }

        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            if (FileSystemName.MatchesSimpleExpression(pattern, path, ignoreCase: true))
            {
                return true;
            }

            // 没写目录的 "*.jar" 允许匹配任意层级。
            return !pattern.Contains('/') &&
                   FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: true);
        }

        return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase);
    }

    // 枚举实例目录里所有受管文件（相对路径，'/' 分隔，已排序）。
    public List<string> Enumerate(string instanceDir)
    {
        string root = Path.GetFullPath(instanceDir);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"找不到实例目录：{instanceDir}");
        }

        var result = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (IsIncluded(relative))
            {
                result.Add(relative);
            }
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
