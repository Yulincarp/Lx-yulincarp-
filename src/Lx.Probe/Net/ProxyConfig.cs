using System.Net;

namespace Lx.Probe.Net;

// 全链路代理：所有启动器 HTTP 客户端统一从这里取。
// SettingsStore 在加载/保存设置时调用 Configure。
public static class ProxyConfig
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, HttpClient> Cache = new(StringComparer.Ordinal);
    private static string _mode = "system";
    private static string? _url;

    public static void Configure(string? mode, string? url)
    {
        string normalized = mode?.ToLowerInvariant() switch
        {
            "none" or "direct" => "none",
            "manual" => "manual",
            _ => "system",
        };
        string? trimmed = string.IsNullOrWhiteSpace(url) ? null : url.Trim();

        if (normalized == "manual" &&
            (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? proxyUri) ||
             (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps)))
        {
            normalized = "system";
            trimmed = null;
        }

        lock (Gate)
        {
            if (normalized == _mode && string.Equals(trimmed, _url, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _mode = normalized;
            _url = trimmed;
            foreach (HttpClient client in Cache.Values)
            {
                client.Dispose();
            }

            Cache.Clear();
        }
    }

    public static HttpClient GetClient(TimeSpan timeout, string? userAgent = null, bool http11 = false)
    {
        string key = $"{(int)timeout.TotalSeconds}|{userAgent}|{http11}";
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out HttpClient? existing))
            {
                return existing;
            }

            var handler = new SocketsHttpHandler();
            if (_mode == "none")
            {
                handler.UseProxy = false;
            }
            else if (_mode == "manual" && _url is not null)
            {
                handler.Proxy = new WebProxy(_url);
                handler.UseProxy = true;
            }

            var client = new HttpClient(handler) { Timeout = timeout };
            if (http11)
            {
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            }

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            }

            Cache[key] = client;
            return client;
        }
    }
}
