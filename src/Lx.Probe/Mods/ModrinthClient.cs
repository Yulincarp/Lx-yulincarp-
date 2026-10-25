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

public sealed record ModSearchResult(
    string Id,
    string Slug,
    string Title,
    string Description,
    string Author,
    long Downloads);

public sealed record ModDependencyInfo(
    string? VersionId,
    string? ProjectId,
    string DependencyType);

public sealed record ModFileInfo(string FileName, string Url, string? Sha1, long Size);

public sealed record ModVersionInfo(
    string Id,
    string ProjectId,
    string VersionNumber,
    string Name,
    List<ModDependencyInfo> Dependencies,
    ModFileInfo? File,
    string? VersionType = null);

// 用 Modrinth 的「文件哈希查版本」接口判断更新，不需要猜 mod 的 slug。
public static class ModrinthClient
{
    private const string Base = "https://api.modrinth.com/v2";

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent + " (local test)");
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

        JsonNode latest = versions.FirstOrDefault(version =>
            string.Equals(version?["version_type"]?.GetValue<string>(), "release", StringComparison.OrdinalIgnoreCase))
            ?? versions[0]!;
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

    // 搜索 Modrinth（只搜 mod，按游戏版本和加载器过滤）。
    public static async Task<List<ModSearchResult>> SearchAsync(
        string query,
        string gameVersion,
        string loader,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        string facets = $"[[\"project_type:mod\"],[\"versions:{gameVersion}\"],[\"categories:{loader}\"]]";
        string url = $"{Base}/search?query={Uri.EscapeDataString(query)}&limit={limit}&index=relevance&facets={Uri.EscapeDataString(facets)}";

        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("Modrinth 限流了（429），过一分钟再试");
        }

        response.EnsureSuccessStatusCode();
        JsonNode root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!;
        JsonArray? hits = root["hits"] as JsonArray;
        if (hits is null)
        {
            return new List<ModSearchResult>();
        }

        var results = new List<ModSearchResult>();
        foreach (JsonNode? hit in hits)
        {
            string? id = hit?["project_id"]?.GetValue<string>();
            string? title = hit?["title"]?.GetValue<string>();
            if (id is null || title is null)
            {
                continue;
            }

            results.Add(new ModSearchResult(
                id,
                hit?["slug"]?.GetValue<string>() ?? id,
                title,
                hit?["description"]?.GetValue<string>() ?? string.Empty,
                hit?["author"]?.GetValue<string>() ?? string.Empty,
                hit?["downloads"]?.GetValue<long>() ?? 0));
        }

        return results;
    }

    // 项目在指定游戏版本/加载器下可用的版本（新到旧）。
    public static async Task<List<ModVersionInfo>> GetVersionsAsync(
        string projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        JsonArray? versions = await GetProjectVersionsAsync(projectId, gameVersion, loader, cancellationToken).ConfigureAwait(false);
        if (versions is null)
        {
            return new List<ModVersionInfo>();
        }

        var results = new List<ModVersionInfo>();
        foreach (JsonNode? version in versions)
        {
            ModVersionInfo? parsed = ParseVersion(version);
            if (parsed is not null)
            {
                results.Add(parsed);
            }
        }

        return results;
    }

    public static async Task<ModVersionInfo?> GetVersionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await Http
            .GetAsync($"{Base}/version/{versionId}", cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return ParseVersion(JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
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

        JsonNode latest = versions.FirstOrDefault(version =>
            string.Equals(version?["version_type"]?.GetValue<string>(), "release", StringComparison.OrdinalIgnoreCase))
            ?? versions[0]!;
        JsonNode? file = PickPrimaryFile(latest);
        string? url = file?["url"]?.GetValue<string>();
        string? fileName = file?["filename"]?.GetValue<string>();
        if (url is null || fileName is null)
        {
            return null;
        }

        return (url, fileName, file?["hashes"]?["sha1"]?.GetValue<string>(), file?["size"]?.GetValue<long>() ?? 0);
    }

    public static async Task<string> GetProjectTitleAsync(string projectId)
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

    private static async Task<JsonArray?> GetProjectVersionsAsync(
        string projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        string url = $"{Base}/project/{projectId}/version?game_versions=[\"{gameVersion}\"]&loaders=[\"{loader}\"]";
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return JsonNode.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)) as JsonArray;
    }

    private static ModVersionInfo? ParseVersion(JsonNode? version)
    {
        if (version is null)
        {
            return null;
        }

        string? id = version["id"]?.GetValue<string>();
        string? projectId = version["project_id"]?.GetValue<string>();
        string? versionNumber = version["version_number"]?.GetValue<string>();
        if (id is null || projectId is null || versionNumber is null)
        {
            return null;
        }

        JsonNode? primary = PickPrimaryFile(version);
        string? url = primary?["url"]?.GetValue<string>();
        string? fileName = primary?["filename"]?.GetValue<string>();
        if (url is null || fileName is null)
        {
            return null;
        }

        var dependencies = new List<ModDependencyInfo>();
        if (version["dependencies"] is JsonArray dependencyArray)
        {
            foreach (JsonNode? dependency in dependencyArray)
            {
                string? type = dependency?["dependency_type"]?.GetValue<string>();
                if (type is null)
                {
                    continue;
                }

                dependencies.Add(new ModDependencyInfo(
                    dependency?["version_id"]?.GetValue<string>(),
                    dependency?["project_id"]?.GetValue<string>(),
                    type));
            }
        }

        return new ModVersionInfo(
            id,
            projectId,
            versionNumber,
            version["name"]?.GetValue<string>() ?? versionNumber,
            dependencies,
            new ModFileInfo(
                fileName,
                url,
                primary?["hashes"]?["sha1"]?.GetValue<string>(),
                primary?["size"]?.GetValue<long>() ?? 0),
            version["version_type"]?.GetValue<string>());
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
