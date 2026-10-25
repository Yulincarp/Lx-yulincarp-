using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Lx.Probe.Mods;

namespace Lx.Probe.Modpacks;

public sealed record CurseForgeDependency(long ModId, int RelationType);

// CurseForge 的一个文件（下载地址 + sha1 + required 依赖）。
public sealed record CurseForgeFile(
    long FileId,
    string FileName,
    string? DownloadUrl,
    string? Sha1,
    long Size,
    long ModId = 0,
    string? DisplayName = null,
    int ReleaseType = 0,
    List<CurseForgeDependency>? Dependencies = null);

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
            return ReadStoredKey(path);
        }

        return null;
    }

    // Windows 上用 DPAPI 加密（当前用户才能解开）；其他平台退回明文。
    public static void SaveKey(string installRoot, string key)
    {
        Directory.CreateDirectory(installRoot);
        string trimmed = key.Trim();

        if (OperatingSystem.IsWindows())
        {
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(trimmed),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            File.WriteAllText(KeyFile(installRoot), "dpapi:" + Convert.ToBase64String(protectedBytes));
            return;
        }

        File.WriteAllText(KeyFile(installRoot), trimmed);
    }

    private static string? ReadStoredKey(string path)
    {
        string raw = File.ReadAllText(path).Trim();
        if (raw.Length == 0)
        {
            return null;
        }

        if (!raw.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null; // 非 Windows 解不开 DPAPI
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(raw["dpapi:".Length..]);
            byte[] plain = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    // 按 slug 找项目，再按游戏版本 + 加载器找最新文件（modLoaderType：1 Forge / 4 Fabric / 6 NeoForge）。
    public static async Task<CurseForgeFile?> FindModFileAsync(
        string slug,
        string gameVersion,
        int modLoaderType,
        string apiKey,
        Action<string> log)
    {
        using var search = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Base}/mods/search?gameId=432&slug={Uri.EscapeDataString(slug)}&pageSize=1");
        search.Headers.Add("x-api-key", apiKey);
        search.Headers.Add("Accept", "application/json");

        using HttpResponseMessage searchResponse = await Http.SendAsync(search).ConfigureAwait(false);
        await EnsureOkAsync(searchResponse, "搜索项目").ConfigureAwait(false);

        JsonNode? searchRoot = JsonNode.Parse(await searchResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        long? modId = (searchRoot?["data"] as JsonArray)?.FirstOrDefault()?["id"]?.GetValue<long>();
        if (modId is null)
        {
            log($"CurseForge 上找不到项目 {slug}");
            return null;
        }

        using var filesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Base}/mods/{modId}/files?gameVersion={Uri.EscapeDataString(gameVersion)}&modLoaderType={modLoaderType}&pageSize=50");
        filesRequest.Headers.Add("x-api-key", apiKey);
        filesRequest.Headers.Add("Accept", "application/json");

        using HttpResponseMessage filesResponse = await Http.SendAsync(filesRequest).ConfigureAwait(false);
        await EnsureOkAsync(filesResponse, "查询文件").ConfigureAwait(false);

        JsonNode? filesRoot = JsonNode.Parse(await filesResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        JsonArray? files = filesRoot?["data"] as JsonArray;
        if (files is null || files.Count == 0)
        {
            return null;
        }

        JsonNode? newest = files
            .Where(node => node?["downloadUrl"] is not null)
            .OrderByDescending(node => node?["fileDate"]?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal)
            .FirstOrDefault();

        if (newest is null)
        {
            return null;
        }

        string? sha1 = null;
        foreach (JsonNode? hash in newest["hashes"] as JsonArray ?? new JsonArray())
        {
            if (hash?["algo"]?.GetValue<int>() == 1)
            {
                sha1 = hash["value"]?.GetValue<string>();
                break;
            }
        }

        return new CurseForgeFile(
            newest["id"]?.GetValue<long>() ?? 0,
            newest["fileName"]?.GetValue<string>() ?? $"{slug}.jar",
            newest["downloadUrl"]?.GetValue<string>(),
            sha1,
            newest["fileLength"]?.GetValue<long>() ?? 0);
    }

    // 1 Forge / 4 Fabric / 6 NeoForge（CurseForge 的 modLoaderType）。
    public static int LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0,
    };

    public static async Task<List<ModSearchResult>> SearchAsync(
        string query,
        string gameVersion,
        string loader,
        string apiKey,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        int loaderType = LoaderType(loader);
        if (loaderType == 0)
        {
            throw new InvalidOperationException($"CurseForge 不支持加载器 {loader}");
        }

        string url =
            $"{Base}/mods/search?gameId=432&classId=6&searchFilter={Uri.EscapeDataString(query)}" +
            $"&gameVersion={Uri.EscapeDataString(gameVersion)}&modLoaderType={loaderType}" +
            $"&sortField=2&sortOrder=desc&pageSize={Math.Clamp(limit, 1, 50)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("Accept", "application/json");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(response, "搜索 mod").ConfigureAwait(false);

        JsonNode? root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var results = new List<ModSearchResult>();
        foreach (JsonNode? node in root?["data"] as JsonArray ?? new JsonArray())
        {
            long? id = node?["id"]?.GetValue<long>();
            string? name = node?["name"]?.GetValue<string>();
            if (id is null || name is null)
            {
                continue;
            }

            string slug = node?["slug"]?.GetValue<string>() ?? id.Value.ToString();
            string author = (node?["authors"] as JsonArray)?
                .FirstOrDefault()?["name"]?.GetValue<string>() ?? string.Empty;

            results.Add(new ModSearchResult(
                id.Value.ToString(),
                slug,
                name,
                node?["summary"]?.GetValue<string>() ?? string.Empty,
                author,
                node?["downloadCount"]?.GetValue<long>() ?? 0,
                "curseforge"));
        }

        return results;
    }

    public static async Task<List<CurseForgeFile>> GetModFilesAsync(
        long modId,
        string gameVersion,
        string loader,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        int loaderType = LoaderType(loader);
        string url =
            $"{Base}/mods/{modId}/files?gameVersion={Uri.EscapeDataString(gameVersion)}" +
            $"&modLoaderType={loaderType}&pageSize=50";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("Accept", "application/json");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureOkAsync(response, $"查询 mod {modId} 文件").ConfigureAwait(false);

        JsonNode? root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var files = new List<CurseForgeFile>();
        foreach (JsonNode? node in root?["data"] as JsonArray ?? new JsonArray())
        {
            CurseForgeFile? file = ParseFile(node);
            if (file is not null)
            {
                files.Add(file);
            }
        }

        return files;
    }

    public static async Task<string> GetModNameAsync(
        long modId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/mods/{modId}");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("Accept", "application/json");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return modId.ToString();
        }

        JsonNode? root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return root?["data"]?["name"]?.GetValue<string>() ?? modId.ToString();
    }

    private static CurseForgeFile? ParseFile(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        long? id = node["id"]?.GetValue<long>();
        string? fileName = node["fileName"]?.GetValue<string>();
        string? downloadUrl = node["downloadUrl"]?.GetValue<string>();
        if (id is null || fileName is null || downloadUrl is null)
        {
            return null;
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

        var dependencies = new List<CurseForgeDependency>();
        foreach (JsonNode? dependency in node["dependencies"] as JsonArray ?? new JsonArray())
        {
            long? depModId = dependency?["modId"]?.GetValue<long>();
            if (depModId is > 0)
            {
                dependencies.Add(new CurseForgeDependency(
                    depModId.Value,
                    dependency?["relationType"]?.GetValue<int>() ?? 0));
            }
        }

        return new CurseForgeFile(
            id.Value,
            fileName,
            downloadUrl,
            sha1,
            node["fileLength"]?.GetValue<long>() ?? 0,
            node["modId"]?.GetValue<long>() ?? 0,
            node["displayName"]?.GetValue<string>(),
            node["releaseType"]?.GetValue<int>() ?? 0,
            dependencies);
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response, string what)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("CurseForge 拒绝了这个 API key（401/403），检查 key 是否有效");
        }

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"CurseForge {what}失败（{(int)response.StatusCode}）：{body}");
        }
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
