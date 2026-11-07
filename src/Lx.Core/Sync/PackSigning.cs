using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lx.Probe.Sync;

// 整合包签名：pack.json 里带 SignerKey（公钥）+ Signature（签名），
// 客户端拿到清单后重算一遍规范化字节再验签，对不上就拒绝同步。
//
// 为什么是 ECDSA P-256 而不是 Ed25519：.NET 8 的 BCL 没有 Ed25519，
// 引第三方库会给这个"离线也要能装"的启动器增加依赖面。P-256 同样是
// 椭圆曲线签名、密钥短、平台无关，够用。
public sealed record PackSigningKey(string PrivateKey, string PublicKey);

public static class PackSigning
{
    public const string Algorithm = "ECDSA-P256-SHA256";

    // 规范化字节：去掉 Signature 字段本身，其余字段按声明顺序、不缩进序列化。
    // 两边（导出端/客户端）必须用同一套选项，否则验签永远对不上。
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PackSigningKey CreateKeyPair()
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new PackSigningKey(
            Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()),
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    public static byte[] CanonicalBytes(PackManifest manifest)
    {
        // 三个签名字段全部清空再序列化：签名时它们还没填，验签时已经填了，
        // 不清空就会导致"签的时候和验的时候内容不一样"，永远失败。
        PackManifest bare = manifest with { Signature = null, SignerKey = null, SignatureAlgorithm = null };
        return JsonSerializer.SerializeToUtf8Bytes(bare, CanonicalOptions);
    }

    // 签名后返回带 Signature / SignerKey / SignatureAlgorithm 的新清单。
    public static PackManifest Sign(PackManifest manifest, PackSigningKey key)
    {
        byte[] payload = CanonicalBytes(manifest);
        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(key.PrivateKey), out _);
        byte[] signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);

        return manifest with
        {
            Signature = Convert.ToBase64String(signature),
            SignerKey = key.PublicKey,
            SignatureAlgorithm = Algorithm,
        };
    }

    // 返回 false 时 error 里是给人看的原因；未签名清单返回 false，但 error 说明是"没签"。
    public static bool Verify(PackManifest manifest, out string error)
    {
        if (string.IsNullOrWhiteSpace(manifest.Signature))
        {
            error = "清单没有签名";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.SignerKey))
        {
            error = "清单有签名但缺少公钥";
            return false;
        }

        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(manifest.SignerKey), out _);
            bool ok = ecdsa.VerifyData(
                CanonicalBytes(manifest),
                Convert.FromBase64String(manifest.Signature),
                HashAlgorithmName.SHA256);
            error = ok ? string.Empty : "签名与内容不匹配（清单被改过）";
            return ok;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            error = "签名或公钥格式不对：" + ex.Message;
            return false;
        }
    }

    public static bool IsSigned(PackManifest manifest) => !string.IsNullOrWhiteSpace(manifest.Signature);
}
