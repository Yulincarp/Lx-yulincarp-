using System.IO.Compression;
using System.Runtime.InteropServices;
using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// Step 6：1.20.4 的 JSON 里没有 library.natives / downloads.classifiers，
// natives 是独立的 ":natives-windows" 库条目，所以先按 CPU 架构挑出对应的 jar，再解压。
public static class NativesExtractor
{
    public static string ArchSuffix { get; } = RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => ":natives-windows-arm64",
        Architecture.X86 => ":natives-windows-x86",
        _ => ":natives-windows",
    };

    public static List<string> FindJars(
        IEnumerable<Library> libraries,
        Dictionary<string, bool> features,
        string librariesDir)
    {
        var jars = new List<string>();

        foreach (Library library in libraries)
        {
            if (library.Name is null || !library.Name.EndsWith(ArchSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!RuleEvaluator.IsAllowed(library.Rules, features))
            {
                continue;
            }

            string? artifactPath = library.Downloads?.Artifact?.Path;
            if (string.IsNullOrEmpty(artifactPath))
            {
                continue;
            }

            jars.Add(Path.GetFullPath(Path.Combine(librariesDir, artifactPath.Replace('/', Path.DirectorySeparatorChar))));
        }

        return jars;
    }

    // 解压到 nativesDir，跳过 META-INF/，并挡住会跑出目录的 zip 路径。
    public static int Extract(IEnumerable<string> jarPaths, string nativesDir)
    {
        Directory.CreateDirectory(nativesDir);
        string root = Path.GetFullPath(nativesDir) + Path.DirectorySeparatorChar;
        int written = 0;

        foreach (string jarPath in jarPaths)
        {
            if (!File.Exists(jarPath))
            {
                continue;
            }

            using ZipArchive zip = ZipFile.OpenRead(jarPath);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue; // 目录项
                }

                if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string destination = Path.GetFullPath(Path.Combine(nativesDir, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try
                {
                    entry.ExtractToFile(destination, overwrite: true);
                    written++;
                }
                catch (IOException)
                {
                    // 游戏正在运行时会锁住 dll：说明它已经解压过了，跳过即可。
                }
            }
        }

        return written;
    }
}
