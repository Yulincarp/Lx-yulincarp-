using System.Security.Cryptography;
using System.Text;
using Lx.Probe.Model;

namespace Lx.Probe.Auth;

// 离线登录：UUID 由名字算出来，和 Java 的 UUID.nameUUIDFromBytes("OfflinePlayer:" + name) 一致。
public static class OfflineAuth
{
    public static Account Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("离线登录需要一个玩家名", nameof(name));
        }

        return new Account("offline", name, UuidFor(name), "0", null);
    }

    public static string UuidFor(string name)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));

        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3（MD5）
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
