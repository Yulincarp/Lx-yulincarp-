using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lx.Probe.Resolve;

namespace Lx.Probe.Mods;

// 一个可用的更新：哪个文件、从哪个版本升到哪个版本、新文件从哪下。
public sealed record ModUpdate(
    string FileName,
    string ProjectId,
    string ProjectTitle,
    string CurrentVersion,
    string LatestVersion,
    string DownloadUrl,
    string NewFileName,
    string? Sha1,
    long Size);

// 用 Modrinth 的「文件哈希查版本」接口判断更新，不需要猜 mod 的 slug。
public static class ModrinthClient
{
    private const string Base = "https://api.modrinth.com/v2";

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lx-Launcher/0.1 (local test)");
        return client;
    }

    // 查不到（不在 Modrinth 上）返回 null。
    public static async Task<ModUpdate?> CheckAsync(string jarPath, string gameVersion, string loader = "fabric")
    {
        string sha1 = Hasher.Sha1Of(jarPath);
        using HttpResponseMessage versionResponse = await Http.GetAsync($"{Base}/version_file/{sha1}?algorithm=sha1").ConfigureAwait(false);

        if (versionResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (versionResponse.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("Modrinth 限流了（429），过一分钟再试");
        }

        versionResponse.EnsureSuccessStatusCode();
        JsonNode version = JsonNode.Parse(await versionResponse.Content.ReadAsStringAsync().ConfigureAwait(false))!;

        string? projectId = version["project_id"]?.GetValue<string>();
        string? currentVersion = version["version_number"]?.GetValue<string>();
        if (projectId is null || currentVersion is null)
        {
            return null;
        }

        string title = await GetProjectTitleAsync(projectId).ConfigureAwait(false);

        JsonArray? versions = await GetProjectVersionsAsync(projectId, gameVersion, loader).ConfigureAwait(false);
        if (versions is null || versions.Count == 0)
        {
            return null;
        }

        JsonNode latest = versions[0]!;
        string? latestVersion = latest["version_number"]?.GetValue<string>();
        if (latestVersion is null || string.Equals(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        JsonNode? file = PickPrimaryFile(latest);
        string? url = file?["url"]?.GetValue<string>();
        string? fileName = file?["filename"]?.GetValue<string>();
        if (url is null || fileName is null)
        {
            return null;
        }

        return new ModUpdate(
            Path.GetFileName(jarPath),
            projectId,
            title,
            currentVersion,
            latestVersion,
            url,
            fileName,
            file?["hashes"]?["sha1"]?.GetValue<string>(),
            file?["size"]?.GetValue<long>() ?? 0);
    }

    // 取某个项目（slug 或 id）在指定版本/加载器下的最新文件，用于“一键装 mod”。
    public static async Task<(string Url, string FileName, string? Sha1, long Size)?> GetLatestFileAsync(
        string projectSlug,
        string gameVersion,
        string loader)
    {
        JsonArray? versions = await GetProjectVersionsAsync(projectSlug, gameVersion, loader).ConfigureAwait(false);
        if (versions is null || versions.Count == 0)
        {
            return null;
        }

        JsonNode? file = PickPrimaryFile(versions[0]!);
        string? url = file?["url"]?.GetValue<string>();
        string? fileName = file?["filename"]?.GetValue<string>();
        if (url is null || fileName is null)
        {
            return null;
        }

        return (url, fileName, file?["hashes"]?["sha1"]?.GetValue<string>(), file?["size"]?.GetValue<long>() ?? 0);
    }

    private static async Task<string> GetProjectTitleAsync(string projectId)
    {
        try
        {
            using HttpResponseMessage response = await Http.GetAsync($"{Base}/project/{projectId}").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return projectId;
            }

            JsonNode project = JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))!;
            return project["title"]?.GetValue<string>() ?? projectId;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return projectId;
        }
    }

    private static async Task<JsonArray?> GetProjectVersionsAsync(string projectId, string gameVersion, string loader)
    {
        string url = $"{Base}/project/{projectId}/version?game_versions=[\"{gameVersion}\"]&loaders=[\"{loader}\"]";
        using HttpResponseMessage response = await Http.GetAsync(url).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)) as JsonArray;
    }

    private static JsonNode? PickPrimaryFile(JsonNode version)
    {
        JsonArray? files = version["files"] as JsonArray;
        if (files is null || files.Count == 0)
        {
            return null;
        }

        return files.FirstOrDefault(file => file?["primary"]?.GetValue<bool>() == true) ?? files[0];
    }
}
