using Lx.Probe.Model;

namespace Lx.Probe.Resolve;

// 两种 library 来源统一成一个 artifact：
// 1) 原版：downloads.artifact（path/url/sha1 都有）；
// 2) Fabric / Forge：只有 maven 坐标 name + url + sha1，路径要自己按 group/artifact/version 拼。
public static class LibraryResolver
{
    public static bool TryGetArtifact(Library library, out LibraryArtifact artifact)
    {
        LibraryArtifact? vanilla = library.Downloads?.Artifact;
        if (vanilla?.Path is not null && vanilla.Url is not null)
        {
            artifact = vanilla;
            return true;
        }

        if (library.Name is not null && library.Url is not null)
        {
            string path = MavenPath(library.Name);
            if (path.Length > 0)
            {
                artifact = new LibraryArtifact
                {
                    Path = path,
                    Url = library.Url.TrimEnd('/') + "/" + path,
                    Sha1 = library.Sha1,
                    Size = library.Size,
                };
                return true;
            }
        }

        artifact = null!;
        return false;
    }

    // "group:artifact:version[:classifier]" → "group/artifact/version/artifact-version[-classifier].jar"
    public static string MavenPath(string coordinate)
    {
        string[] parts = coordinate.Split(':');
        if (parts.Length < 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return string.Empty;
        }

        string group = parts[0].Replace('.', '/');
        string artifactName = parts[1];
        string version = parts[2];
        string classifier = parts.Length > 3 && parts[3].Length > 0 ? "-" + parts[3] : string.Empty;

        return $"{group}/{artifactName}/{version}/{artifactName}-{version}{classifier}.jar";
    }
}
