using Lx.Probe.Skins;

namespace Lx.Tests;

public class LocalSkinTests
{
    private static string TempRoot()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "skinlocal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // 只写 PNG 签名 + IHDR 尺寸：ImportLocal 校验的就是这个头，不需要真解码像素。
    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[40];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        signature.CopyTo(bytes, 0);
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    [Theory]
    [InlineData(64, 64)]
    [InlineData(64, 32)]
    public void ImportLocal_AcceptsMinecraftSkinSizes(int width, int height)
    {
        string root = TempRoot();
        try
        {
            string source = Path.Combine(root, "skin.png");
            File.WriteAllBytes(source, PngHeader(width, height));

            string path = SkinService.ImportLocal(root, "ABC-123", source, out string error);

            Assert.Equal(string.Empty, error);
            Assert.Equal(SkinService.CustomPath(root, "ABC-123"), path);
            Assert.True(File.Exists(path));
            Assert.Equal(path, SkinService.CustomSkinPath(root, "abc123"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportLocal_RejectsWrongSize_AndNonPng()
    {
        string root = TempRoot();
        try
        {
            string big = Path.Combine(root, "big.png");
            File.WriteAllBytes(big, PngHeader(128, 128));
            Assert.Equal(string.Empty, SkinService.ImportLocal(root, "k", big, out string sizeError));
            Assert.Contains("尺寸", sizeError);

            string jpg = Path.Combine(root, "skin.jpg");
            File.WriteAllBytes(jpg, PngHeader(64, 64));
            Assert.Equal(string.Empty, SkinService.ImportLocal(root, "k", jpg, out string typeError));
            Assert.Contains("PNG", typeError);

            Assert.Equal(string.Empty, SkinService.ImportLocal(root, "k", Path.Combine(root, "none.png"), out string missing));
            Assert.Contains("找不到", missing);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
