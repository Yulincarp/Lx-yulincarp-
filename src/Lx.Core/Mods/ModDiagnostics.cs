namespace Lx.Probe.Mods;

public sealed record ModIssue(string Kind, string Message, IReadOnlyList<ModFile> Files);

// 本地 mods 的静态体检：重复 mod id、缺失 required 依赖。
// 冲突检测只基于 jar 内的 metadata，不联网；Forge/NeoForge 目前能读 modId，但不解析依赖块。
public static class ModDiagnostics
{
    private static readonly HashSet<string> RuntimeIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft",
        "fabricloader",
        "quilt_loader",
        "java",
        "forge",
        "neoforge",
    };

    public static List<ModIssue> Scan(IEnumerable<ModFile> mods)
    {
        List<ModFile> list = mods.ToList();
        var issues = new List<ModIssue>();

        foreach (IGrouping<string, ModFile> group in list
                     .Where(mod => !string.IsNullOrWhiteSpace(mod.Id))
                     .GroupBy(mod => mod.Id!, StringComparer.OrdinalIgnoreCase))
        {
            List<ModFile> files = group.ToList();
            if (files.Count > 1)
            {
                issues.Add(new ModIssue(
                    "duplicate",
                    $"mod id「{group.Key}」重复：{string.Join("、", files.Select(f => f.FileName))}",
                    files));
            }
        }

        HashSet<string> installed = list
            .Where(mod => !string.IsNullOrWhiteSpace(mod.Id))
            .Select(mod => mod.Id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (ModFile mod in list)
        {
            if (mod.Dependencies is null)
            {
                continue;
            }

            foreach (string dependency in mod.Dependencies
                         .Where(dep => !RuntimeIds.Contains(dep) && !installed.Contains(dep))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ModIssue(
                    "missing-dependency",
                    $"{mod.DisplayName} 缺少依赖：{dependency}",
                    new[] { mod }));
            }
        }

        return issues;
    }
}
