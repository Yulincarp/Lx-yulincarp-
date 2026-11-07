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

    // 这些主机不走代理：自家镜像 + 本机地址。
    // 理由很实在：镜像在国内，绕境外代理反而慢一到两个数量级
    //（实测同一份 75MB 的包：直连 1.67 MB/s，走代理 70 KB/s）。
    public static readonly string[] BypassHosts =
    {
        "*.dpdns.org",
        "dpdns.org",
        "localhost",
        "127.0.0.1",
        "[::1]",
    };

    public static bool ShouldBypass(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        foreach (string pattern in BypassHosts)
        {
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                string suffix = pattern[1..]; // ".dpdns.org"
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(host, pattern.Trim('[', ']'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

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

    // 把系统代理解析成具体的地址，再带上绕过列表。
    // 拿不到（比如 Linux 上没有配置）就返回 null，交回"直连"。
    private static IWebProxy? CreateSystemProxy()
    {
        try
        {
            IWebProxy system = WebRequest.GetSystemWebProxy();
            Uri probe = new("https://example.com/");
            Uri? resolved = system.GetProxy(probe);
            if (resolved is null || resolved == probe)
            {
                return null;
            }

            return new WebProxy(resolved, true, BypassHosts);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
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
                handler.Proxy = new WebProxy(_url, true, BypassHosts);
                handler.UseProxy = true;
            }
            else
            {
                // 系统代理：自己包一层 WebProxy，才能带上绕过列表。
                handler.Proxy = CreateSystemProxy();
                handler.UseProxy = handler.Proxy is not null;
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
