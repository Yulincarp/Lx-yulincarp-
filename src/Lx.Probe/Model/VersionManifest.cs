namespace Lx.Probe.Model;

// version_manifest_v2.json：Mojang 的版本总表。
public class VersionManifest
{
    public ManifestLatest? Latest { get; set; }
    public List<ManifestVersion>? Versions { get; set; }
}

public class ManifestLatest
{
    public string? Release { get; set; }
    public string? Snapshot { get; set; }
}

public class ManifestVersion
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Sha1 { get; set; }
    public DateTime ReleaseTime { get; set; }
}
