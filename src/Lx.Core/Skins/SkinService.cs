using System.Text.Json;
using Lx.Probe.Net;

namespace Lx.Probe.Skins;

// 皮肤获取：按 UUID 从 Mojang 拿 skin 纹理地址，下载整张皮肤缓存到
// <数据目录>/cache/skins/<uuid>.png。取不到就返回 null，界面用字母头像兜底。
public static class SkinService
{
    private static HttpClient Http => ProxyConfig.GetClient(TimeSpan.FromSeconds(10));

    public static string CachePath(string installRoot, string uuid) =>
        Path.Combine(installRoot, "cache", "skins", Normalize(uuid) + ".png");

    // 用户自己选的本地皮肤；按账号 UUID 存，和在线皮肤分开，不会互相覆盖。
    public static string CustomPath(string installRoot, string key) =>
        Path.Combine(installRoot, "cache", "skins", "custom-" + Normalize(key) + ".png");

    public static string? CustomSkinPath(string installRoot, string key)
    {
        string path = CustomPath(installRoot, key);
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // 导入本地皮肤：只接受 64×64 / 64×32 的 PNG（Minecraft 皮肤格式），其余拒绝。
    public static string ImportLocal(string installRoot, string key, string sourcePath, out string error)
    {
        error = string.Empty;

        if (!File.Exists(sourcePath))
        {
            error = "找不到这个文件";
            return string.Empty;
        }

        if (!sourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            error = "皮肤必须是 PNG 图片（64×64 或 64×32）";
            return string.Empty;
        }

        try
        {
            (int width, int height) = PngSize(File.ReadAllBytes(sourcePath));
            bool valid = width == 64 && (height == 64 || height == 32);
            if (!valid)
            {
                error = $"皮肤尺寸不对：{width}×{height}，需要 64×64 或 64×32";
                return string.Empty;
            }

            string target = CustomPath(installRoot, key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourcePath, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"读取皮肤失败：{ex.Message}";
            return string.Empty;
        }
    }

    // 只读 PNG 头（IHDR）拿宽高，不依赖图片库。
    private static (int Width, int Height) PngSize(byte[] bytes)
    {
        if (bytes.Length < 24 ||
            bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
        {
            throw new ArgumentException("不是 PNG 文件");
        }

        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }

    // 已缓存的皮肤路径；没有或为空文件返回 null。
    public static string? CachedSkinPath(string installRoot, string uuid)
    {
        string path = CachePath(installRoot, uuid);
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static string Normalize(string uuid) => uuid.Replace("-", string.Empty).ToLowerInvariant();

    // 从 profile JSON 里解出皮肤地址：properties[].name == "textures" 的值是 base64。
    public static string? DecodeTextureUrl(string profileJson)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(profileJson);
            if (!doc.RootElement.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement property in properties.EnumerateArray())
            {
                if (property.TryGetProperty("name", out JsonElement name) &&
                    name.GetString() == "textures" &&
                    property.TryGetProperty("value", out JsonElement value) &&
                    value.GetString() is { Length: > 0 } encoded)
                {
                    string decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    using JsonDocument textures = JsonDocument.Parse(decoded);
                    return textures.RootElement.GetProperty("textures").GetProperty("SKIN").GetProperty("url").GetString();
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            // 结构不对就当没有皮肤
        }

        return null;
    }

    // 外置登录（Yggdrasil）站点自己的 profile 接口，取不到再退回 Mojang。
    public static string ProfileUrl(string? apiRoot, string uuid)
    {
        string normalized = Normalize(uuid);
        if (!string.IsNullOrWhiteSpace(apiRoot))
        {
            return $"{apiRoot.TrimEnd('/')}/sessionserver/session/minecraft/profile/{normalized}";
        }

        return $"https://sessionserver.mojang.com/session/minecraft/profile/{normalized}";
    }

    // 优先用缓存；没有就请求 profile -> 下载皮肤 -> 落盘。
    public static async Task<string?> GetSkinAsync(
        string installRoot,
        string uuid,
        string? apiRoot = null,
        CancellationToken cancellationToken = default)
    {
        string? cached = CachedSkinPath(installRoot, uuid);
        if (cached is not null)
        {
            return cached;
        }

        if (uuid.Length == 0)
        {
            return null;
        }

        try
        {
            string profile = await Http.GetStringAsync(ProfileUrl(apiRoot, uuid), cancellationToken).ConfigureAwait(false);
            string? textureUrl = DecodeTextureUrl(profile);
            if (textureUrl is null)
            {
                return null;
            }

            byte[] bytes = await Http.GetByteArrayAsync(textureUrl, cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return null;
            }

            string path = CachePath(installRoot, uuid);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
