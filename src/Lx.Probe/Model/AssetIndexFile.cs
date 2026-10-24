namespace Lx.Probe.Model;

// assets/indexes/12.json 的结构：只有一个 objects 字典，键是资源逻辑名，值是 hash + size。
public class AssetIndexFile
{
    public Dictionary<string, AssetObject>? Objects { get; set; }
}

public class AssetObject
{
    public string? Hash { get; set; }
    public long Size { get; set; }
}
