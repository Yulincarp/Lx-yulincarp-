using System.Text.Json;
using Lx.Probe.Sync;
using Xunit;

namespace Lx.Tests;

public sealed class PackSigningTests
{
    private static PackManifest Sample() => new PackManifest
    {
        PackName = "栖月港",
        PackVersion = "1.2.0",
        MinecraftVersion = "1.21.1",
        Loader = "neoforge",
        LoaderVersion = "21.1.264",
        Server = new PackServerInfo("mc.yulincarp.dpdns.org", 62132),
        Files = new List<string> { "mods/a.jar", "config/b.toml" },
        Entries = new List<PackFileEntry>
        {
            new("mods/a.jar", "da39a3ee5e6b4b0d3255bfef95601890afd80709", 1024),
            new("config/b.toml", "adc83b19e793491b1c6ea0fd8b46cd9f32e592fc", 42),
        },
        Delete = new List<string> { "mods/old.jar" },
    }.Normalize();

    [Fact]
    public void Sign_ThenVerify_Passes()
    {
        PackSigningKey key = PackSigning.CreateKeyPair();
        PackManifest signed = PackSigning.Sign(Sample(), key);

        Assert.True(PackSigning.IsSigned(signed));
        Assert.Equal(PackSigning.Algorithm, signed.SignatureAlgorithm);
        Assert.Equal(key.PublicKey, signed.SignerKey);
        Assert.True(PackSigning.Verify(signed, out string error), error);
    }

    [Fact]
    public void TamperedManifest_FailsVerification()
    {
        PackSigningKey key = PackSigning.CreateKeyPair();
        PackManifest signed = PackSigning.Sign(Sample(), key);

        // 改一个字节的哈希，模拟中间人换包
        var tampered = signed with
        {
            Entries = signed.Entries
                .Select(e => e.Path == "mods/a.jar" ? e with { Sha1 = "0000000000000000000000000000000000000000" } : e)
                .ToList(),
        };

        Assert.False(PackSigning.Verify(tampered, out string error));
        Assert.Contains("不匹配", error);
    }

    [Fact]
    public void UnsignedManifest_ReportsMissingSignature()
    {
        Assert.False(PackSigning.Verify(Sample(), out string error));
        Assert.Contains("没有签名", error);
    }

    [Fact]
    public void SignatureSurvivesJsonRoundTrip()
    {
        PackSigningKey key = PackSigning.CreateKeyPair();
        PackManifest signed = PackSigning.Sign(Sample(), key);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        string json = JsonSerializer.Serialize(signed, options);
        PackManifest? back = JsonSerializer.Deserialize<PackManifest>(json, options);

        Assert.NotNull(back);
        Assert.True(PackSigning.Verify(back!, out string error), error);
    }

    [Fact]
    public void ParseRoundTrip_KeepsSignatureValid()
    {
        PackSigningKey key = PackSigning.CreateKeyPair();
        PackManifest signed = PackSigning.Sign(Sample(), key);
        string json = signed.ToJson();
        PackManifest parsed = PackManifest.Parse(json);

        string before = System.Text.Encoding.UTF8.GetString(PackSigning.CanonicalBytes(signed));
        string after = System.Text.Encoding.UTF8.GetString(PackSigning.CanonicalBytes(parsed));

        Assert.True(PackSigning.Verify(parsed, out string error),
            error + "\n--- 签名的规范化字节 ---\n" + before + "\n--- 读回来的规范化字节 ---\n" + after);
    }
}
