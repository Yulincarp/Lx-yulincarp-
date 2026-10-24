namespace Lx.Probe.Model;

// 对应 Mojang version.json 的顶层字段（M1 只需要这几个）。
public class VersionJson
{
    public string? Id { get; set; }
    public string? MainClass { get; set; }
    public string? Assets { get; set; }
    public JavaVersion? JavaVersion { get; set; }
    public List<Library>? Libraries { get; set; }
}

public class JavaVersion
{
    public string? Component { get; set; }
    public int MajorVersion { get; set; }
}

public class Library
{
    public string? Name { get; set; }
    public List<Rule>? Rules { get; set; }
    public LibraryDownloads? Downloads { get; set; }
}

public class LibraryDownloads
{
    public LibraryArtifact? Artifact { get; set; }
}

public class LibraryArtifact
{
    public string? Path { get; set; }
}