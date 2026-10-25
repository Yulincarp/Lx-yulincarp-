namespace Lx.Probe.Resolve;

// 下载源设置：official / bmclapi / custom。
public sealed record DownloadSourceSettings(string? Source, string? CustomBaseUrl)
{
    public const string Official = "official";
    public const string Bmclapi = "bmclapi";
    public const string Custom = "custom";
}

// 把 Mojang 官方 URL 改写到镜像。纯函数，方便单测。
// 约定：custom 填的是"兼容 BMCLAPI 路径"的镜像根，例如自建反代。
public static class DownloadUrlRewriter
{
    public const string BmclapiBase = "https://bmclapi2.bangbang93.com";

    public static string Rewrite(string url, DownloadSourceSettings source)
    {
        if (string.IsNullOrWhiteSpace(url) || source.Source is null or DownloadSourceSettings.Official)
        {
            return url;
        }

        string baseUrl = source.Source == DownloadSourceSettings.Custom
            ? NormalizeBase(source.CustomBaseUrl)
            : BmclapiBase;

        if (baseUrl.Length == 0)
        {
            return url;
        }

        if (TryReplace(url, "https://piston-meta.mojang.com", baseUrl, out string result) ||
            TryReplace(url, "https://piston-data.mojang.com", baseUrl, out result) ||
            TryReplace(url, "https://launchermeta.mojang.com", baseUrl, out result) ||
            TryReplace(url, "https://resources.download.minecraft.net", baseUrl + "/assets", out result) ||
            TryReplace(url, "https://libraries.minecraft.net", baseUrl + "/maven", out result) ||
            TryReplace(url, "https://maven.minecraftforge.net", baseUrl + "/maven", out result) ||
            TryReplace(url, "https://maven.fabricmc.net", baseUrl + "/maven", out result) ||
            TryReplace(url, BmclapiBase, baseUrl, out result))
        {
            return result;
        }

        return url;
    }

    public static string Describe(DownloadSourceSettings source) => source.Source switch
    {
        DownloadSourceSettings.Bmclapi => "BMCLAPI 镜像",
        DownloadSourceSettings.Custom when !string.IsNullOrWhiteSpace(source.CustomBaseUrl) =>
            $"自定义（{source.CustomBaseUrl.TrimEnd('/')}）",
        DownloadSourceSettings.Custom => "自定义（未填地址，按官方处理）",
        _ => "官方（Mojang）",
    };

    public static string NormalizeBase(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        string trimmed = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            return string.Empty;
        }

        return uri.Scheme is "http" or "https" ? trimmed : string.Empty;
    }

    private static bool TryReplace(string url, string prefix, string replacement, out string result)
    {
        if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            result = replacement + url[prefix.Length..];
            return true;
        }

        result = url;
        return false;
    }
}
