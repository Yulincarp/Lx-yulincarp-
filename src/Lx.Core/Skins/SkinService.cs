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
