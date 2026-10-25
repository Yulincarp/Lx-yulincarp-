using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// Step 5：把通过 rules 的 library 拼成 classpath，只算路径，不下载。
public static class ClasspathBuilder
{
    public static List<string> Build(
        IEnumerable<Library> libraries,
        string librariesDir,
        string clientJarPath,
        Dictionary<string, bool> features)
    {
        var entries = new List<string>();
        // NeoForge/Forge 的版本 JSON 里可能重复声明和原版相同的库，按绝对路径去重。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Library library in libraries)
        {
            if (!RuleEvaluator.IsAllowed(library.Rules, features))
            {
                continue;
            }

            // 1.20.4 的 rules 不区分 os.arch，natives 的 x86 / arm64 变体会被一起放进来，
            // 这里按名字只留当前架构的那一个。
            if (library.Name is { } name &&
                name.Contains(":natives-", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith(NativesExtractor.ArchSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!LibraryResolver.TryGetArtifact(library, out LibraryArtifact artifact) || artifact.Path is null)
            {
                continue; // 没有可用 artifact（比如只有 classifiers）
            }

            string absolute = ToAbsolutePath(librariesDir, artifact.Path);
            if (seen.Add(absolute))
            {
                entries.Add(absolute);
            }
        }

        string client = Path.GetFullPath(clientJarPath);
        if (seen.Add(client))
        {
            entries.Add(client);
        }
        return entries;
    }

    private static string ToAbsolutePath(string baseDir, string relativePath) =>
        Path.GetFullPath(Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
