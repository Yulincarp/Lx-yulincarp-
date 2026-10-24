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

        foreach (Library library in libraries)
        {
            if (!RuleEvaluator.IsAllowed(library.Rules, features))
            {
                continue;
            }

            string? artifactPath = library.Downloads?.Artifact?.Path;
            if (string.IsNullOrEmpty(artifactPath))
            {
                continue; // 有些 library 只有 classifiers，没有 artifact
            }

            entries.Add(ToAbsolutePath(librariesDir, artifactPath));
        }

        entries.Add(Path.GetFullPath(clientJarPath));
        return entries;
    }

    private static string ToAbsolutePath(string baseDir, string relativePath) =>
        Path.GetFullPath(Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
