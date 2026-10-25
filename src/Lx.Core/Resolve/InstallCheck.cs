namespace Lx.Probe.Resolve;

// 启动前体检：清单里的文件是不是都在盘上。
// 只查"存在"，不重算 sha1——710 MB 每次重算要十几秒，完整性由下载器负责。
public static class InstallCheck
{
    public static List<DownloadItem> FindMissing(IEnumerable<DownloadItem> items, string installRoot)
    {
        var missing = new List<DownloadItem>();

        foreach (DownloadItem item in items)
        {
            string path = Path.Combine(installRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                missing.Add(item);
            }
        }

        return missing;
    }
}
