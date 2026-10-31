using System.Text;
using Lx.Probe.Skins;

namespace Lx.Tests;

public class SkinServiceTests
{
    private static string TempRoot()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void DecodeTextureUrl_ReadsBase64TexturesProperty()
    {
        string textures = """
        {"textures":{"SKIN":{"url":"http://textures.minecraft.net/texture/abc123"}}}
        """;
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(textures));
        string profile = $$"""{"id":"x","name":"Steve","properties":[{"name":"textures","value":"{{encoded}}"}]}""";

        Assert.Equal("http://textures.minecraft.net/texture/abc123", SkinService.DecodeTextureUrl(profile));
    }

    [Fact]
    public void DecodeTextureUrl_ReturnsNullForBrokenData()
    {
        Assert.Null(SkinService.DecodeTextureUrl("{}"));
        Assert.Null(SkinService.DecodeTextureUrl("not json"));
        Assert.Null(SkinService.DecodeTextureUrl("""{"properties":[{"name":"textures","value":"!!!not-base64!!!"}]}"""));
    }

    [Fact]
    public void ProfileUrl_UsesYggdrasilApiRootWhenPresent()
    {
        Assert.Equal(
            "https://littleskin.cn/api/yggdrasil/sessionserver/session/minecraft/profile/abc123",
            SkinService.ProfileUrl("https://littleskin.cn/api/yggdrasil/", "ABC-123"));

        Assert.Equal(
            "https://sessionserver.mojang.com/session/minecraft/profile/abc123",
            SkinService.ProfileUrl(null, "ABC-123"));
    }

    [Fact]
    public void CachePath_NormalizesUuid()
    {
        string root = TempRoot();
        try
        {
            string path = SkinService.CachePath(root, "ABCD-1234-EF56");

            Assert.EndsWith("abcd1234ef56.png", path);
            Assert.StartsWith(Path.Combine(root, "cache", "skins"), path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CachedSkinPath_ReturnsFileOnlyWhenNonEmpty()
    {
        string root = TempRoot();
        try
        {
            const string uuid = "0123456789abcdef0123456789abcdef";
            string path = SkinService.CachePath(root, uuid);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            Assert.Null(SkinService.CachedSkinPath(root, uuid));

            File.WriteAllBytes(path, Array.Empty<byte>());
            Assert.Null(SkinService.CachedSkinPath(root, uuid));

            File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            Assert.Equal(path, SkinService.CachedSkinPath(root, uuid));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
