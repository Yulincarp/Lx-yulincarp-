using System.Security.Cryptography;

namespace Lx.Probe.Resolve;

// 算文件 sha1，下载器和 manifest 服务共用一份实现。
public static class Hasher
{
    public static string Sha1Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
