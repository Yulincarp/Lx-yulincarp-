using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lx.Probe.Resolve;

// 从 Fabric 官方 meta 取 loader profile，合并进原版 version.json，写到 versions/<id>/ 下。
public static class FabricInstaller
{
    private const string MetaBase = "https://meta.fabricmc.net/v2/versions/loader";

    public static async Task<(string Id, string JsonPath)> InstallAsync(
        string gameVersion,
        string installRoot,
        string? loaderVersion = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        loaderVersion ??= await LatestStableLoaderAsync(http, gameVersion);

        string profileUrl = $"{MetaBase}/{gameVersion}/{loaderVersion}/profile/json";
        string profileJson = await http.GetStringAsync(profileUrl).ConfigureAwait(false);
        profileJson = await FillMissingSha1Async(http, profileJson).ConfigureAwait(false);

        string id;
        using (JsonDocument profile = JsonDocument.Parse(profileJson))
        {
            id = profile.RootElement.GetProperty("id").GetString()
                 ?? throw new InvalidOperationException("Fabric profile 没有 id");
        }

        string parentPath = Path.Combine(installRoot, "versions", gameVersion, gameVersion + ".json");
        if (!File.Exists(parentPath))
        {
            throw new InvalidOperationException($"缺少原版 version.json：{parentPath}");
        }

        string merged = VersionMerger.Merge(File.ReadAllText(parentPath), profileJson);

        string dir = Path.Combine(installRoot, "versions", id);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, id + ".json");
        await File.WriteAllTextAsync(path, merged).ConfigureAwait(false);

        Console.WriteLine($"Fabric {loaderVersion} 已合并到 {id}");
        return (id, path);
    }

    // Fabric 的 meta 里 fabric-loader / intermediary 不带 sha1，但 maven 仓库有 .sha1 旁文件。
    private static async Task<string> FillMissingSha1Async(HttpClient http, string profileJson)
    {
        JsonNode? root = JsonNode.Parse(profileJson);
        JsonArray? libraries = root?["libraries"] as JsonArray;
        if (libraries is null)
        {
            return profileJson;
        }

        foreach (JsonNode? node in libraries)
        {
            JsonObject? library = node as JsonObject;
            if (library?["sha1"] is not null || library?["url"] is null || library?["name"] is null)
            {
                continue;
            }

            string coordinate = library["name"]!.GetValue<string>();
            string path = LibraryResolver.MavenPath(coordinate);
            if (path.Length == 0)
            {
                continue;
            }

            string shaUrl = library["url"]!.GetValue<string>().TrimEnd('/') + "/" + path + ".sha1";
            try
            {
                string sha1 = (await http.GetStringAsync(shaUrl).ConfigureAwait(false)).Trim();
                if (sha1.Length == 40)
                {
                    library["sha1"] = sha1;
                }
            }
            catch (HttpRequestException)
            {
                // 拿不到就留空，由下载器决定怎么处理
            }
        }

        return root!.ToJsonString();
    }

    private static async Task<string> LatestStableLoaderAsync(HttpClient http, string gameVersion)
    {
        string json = await http.GetStringAsync($"{MetaBase}/{gameVersion}").ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement? fallback = null;
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            JsonElement loader = entry.GetProperty("loader");
            fallback ??= loader;
            if (loader.GetProperty("stable").GetBoolean())
            {
                return loader.GetProperty("version").GetString()!;
            }
        }

        return fallback?.GetProperty("version").GetString()
               ?? throw new InvalidOperationException($"Fabric 没有支持 {gameVersion} 的 loader");
    }
}
