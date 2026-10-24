using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Lx.Probe.Modpacks;

// CurseForge 的一个文件（下载地址 + sha1）。
public sealed record CurseForgeFile(long FileId, string FileName, string? DownloadUrl, string? Sha1, long Size);

// CurseForge API：用 key 换下载地址。key 从 --cf-key / 环境变量 / minecraft\curseforge-key.txt 取。
public static class CurseForgeClient
{
    private const string Base = "https://api.curseforge.com/v1";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string KeyFile(string installRoot) => Path.Combine(installRoot, "curseforge-key.txt");

    public static string? ResolveKey(string installRoot, string? overrideKey = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideKey))
        {
            return overrideKey.Trim();
        }

        string? fromEnv = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        string path = KeyFile(installRoot);
        if (File.Exists(path))
        {
            string key = File.ReadAllText(path).Trim();
            if (key.Length > 0)
            {
                return key;
            }
        }

        return null;
    }

    public static void SaveKey(string installRoot, string key)
    {
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(KeyFile(installRoot), key.Trim());
    }

    // 批量查文件信息（接口一次最多 100 个）。
    public static async Task<Dictionary<long, CurseForgeFile>> GetFilesAsync(
        IReadOnlyCollection<long> fileIds,
        string apiKey,
        Action<string> log)
    {
        var result = new Dictionary<long, CurseForgeFile>();

        foreach (long[] chunk in fileIds.Chunk(100))
        {
            string json = "{\"fileIds\":[" + string.Join(",", chunk) + "]}";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/mods/files")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("Accept", "application/json");

            using HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException("CurseForge 拒绝了这个 API key（401/403），检查 key 是否有效");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"CurseForge 接口失败（{(int)response.StatusCode}）：{body}");
            }

            JsonNode? root = JsonNode.Parse(body);
            foreach (JsonNode? node in root?["data"] as JsonArray ?? new JsonArray())
            {
                if (node is null)
                {
                    continue;
                }

                long? id = node["id"]?.GetValue<long>();
                string? fileName = node["fileName"]?.GetValue<string>();
                if (id is null || fileName is null)
                {
                    continue;
                }

                string? sha1 = null;
                foreach (JsonNode? hash in node["hashes"] as JsonArray ?? new JsonArray())
                {
                    if (hash?["algo"]?.GetValue<int>() == 1)
                    {
                        sha1 = hash["value"]?.GetValue<string>();
                        break;
                    }
                }

                result[id.Value] = new CurseForgeFile(
                    id.Value,
                    fileName,
                    node["downloadUrl"]?.GetValue<string>(),
                    sha1,
                    node["fileLength"]?.GetValue<long>() ?? 0);
            }

            log($"CurseForge：查询 {chunk.Length} 个文件，返回 {result.Count} 条");
        }

        return result;
    }
}
