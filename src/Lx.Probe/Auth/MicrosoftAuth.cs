using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lx.Probe.Model;
using Lx.Probe.Net;

namespace Lx.Probe.Auth;

// 正版登录（微软账号）：device code → 微软 token → XBL → XSTS → Minecraft token → 查档案。
// 需要自己在 Azure/Entra 注册一个应用，并把 client id 传进来（公共客户端，不需要 secret）。
public static class MicrosoftAuth
{
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    private static HttpClient Http => ProxyConfig.GetClient(TimeSpan.FromSeconds(60), AppInfo.UserAgent);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static async Task<Account> LoginAsync(string clientId, Action<string>? log = null)
    {
        Action<string> say = log ?? Console.WriteLine;
        DeviceCodeResponse device = await RequestDeviceCodeAsync(clientId);

        say($"1) 打开 {device.VerificationUri}");
        say($"2) 输入代码：{device.UserCode}");
        say("（授权完成后自动继续）");

        TokenResponse msToken = await PollForTokenAsync(clientId, device);

        XboxTokens xbl = await XboxLiveAsync(msToken.AccessToken!);
        XboxTokens xsts = await XstsAsync(xbl.Token, xbl.Uhs);
        (string mcToken, int expiresIn) = await MinecraftLoginAsync(xsts.Uhs, xsts.Token);
        (string id, string name) = await MinecraftProfileAsync(mcToken);

        return new Account(
            "microsoft",
            name,
            id,
            mcToken,
            null,
            RefreshToken: msToken.RefreshToken,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn, 60)),
            ClientId: clientId);
    }

    // 有 refresh token 且快到期的账号，启动前静默续期。
    public static bool NeedsRefresh(Account account)
    {
        if (account.Type != "microsoft" ||
            string.IsNullOrEmpty(account.RefreshToken) ||
            string.IsNullOrEmpty(account.ClientId))
        {
            return false;
        }

        return account.ExpiresAt is null || account.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    public static bool IsExpired(Account account) =>
        account.Type == "microsoft" &&
        account.ExpiresAt is { } expiry &&
        expiry <= DateTimeOffset.UtcNow;

    // 用 refresh_token 换新的微软 token，再走一遍 XBL/XSTS/Minecraft 换 mcToken。
    public static async Task<Account> RefreshAsync(Account account, Action<string>? log = null)
    {
        if (account.Type != "microsoft")
        {
            throw new InvalidOperationException("只有微软账号需要续期");
        }

        if (string.IsNullOrEmpty(account.RefreshToken))
        {
            throw new InvalidOperationException("没有保存 refresh token，请重新登录");
        }

        if (string.IsNullOrEmpty(account.ClientId))
        {
            throw new InvalidOperationException("没有保存 client id，请重新登录");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = account.ClientId,
            ["refresh_token"] = account.RefreshToken,
            ["scope"] = "XboxLive.signin offline_access",
        };

        (HttpStatusCode status, string body) = await PostFormAsync(TokenUrl, form);
        if (status != HttpStatusCode.OK)
        {
            TokenError? error = TryParse<TokenError>(body);
            if (error?.Error == "invalid_grant")
            {
                throw new InvalidOperationException("微软登录已过期（refresh token 失效），请重新登录");
            }

            throw new InvalidOperationException($"刷新微软 token 失败（{(int)status}）：{error?.ErrorDescription ?? body}");
        }

        TokenResponse? token = JsonSerializer.Deserialize<TokenResponse>(body, Json);
        if (token?.AccessToken is null)
        {
            throw new InvalidOperationException("刷新响应里没有 access_token");
        }

        log?.Invoke("微软 token 已刷新，正在重新获取 Minecraft 凭据…");
        XboxTokens xbl = await XboxLiveAsync(token.AccessToken);
        XboxTokens xsts = await XstsAsync(xbl.Token, xbl.Uhs);
        (string mcToken, int expiresIn) = await MinecraftLoginAsync(xsts.Uhs, xsts.Token);

        return account with
        {
            AccessToken = mcToken,
            RefreshToken = token.RefreshToken ?? account.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn, 60)),
        };
    }

    private static async Task<DeviceCodeResponse> RequestDeviceCodeAsync(string clientId)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "XboxLive.signin offline_access",
        };

        (HttpStatusCode status, string body) = await PostFormAsync(DeviceCodeUrl, form);
        if (status != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"申请 device code 失败（{(int)status}）：{body}");
        }

        return JsonSerializer.Deserialize<DeviceCodeResponse>(body, Json)
               ?? throw new InvalidOperationException("device code 响应解析失败");
    }

    private static async Task<TokenResponse> PollForTokenAsync(string clientId, DeviceCodeResponse device)
    {
        if (device.DeviceCode is null)
        {
            throw new InvalidOperationException("device code 响应里没有 device_code");
        }

        int interval = Math.Max(device.Interval, 1);
        DateTime deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval));

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = clientId,
                ["device_code"] = device.DeviceCode,
            };

            (HttpStatusCode status, string body) = await PostFormAsync(TokenUrl, form);
            if (status == HttpStatusCode.OK)
            {
                TokenResponse? token = JsonSerializer.Deserialize<TokenResponse>(body, Json);
                if (token?.AccessToken is not null)
                {
                    return token;
                }
            }

            TokenError? error = TryParse<TokenError>(body);
            switch (error?.Error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                case "expired_token":
                    throw new InvalidOperationException("授权超时，请重新登录");
                case "authorization_declined":
                    throw new InvalidOperationException("你在浏览器里拒绝了授权");
                default:
                    throw new InvalidOperationException($"换 token 失败：{error?.ErrorDescription ?? body}");
            }
        }

        throw new InvalidOperationException("授权超时，请重新登录");
    }

    private static async Task<XboxTokens> XboxLiveAsync(string msToken)
    {
        var payload = new
        {
            Properties = new Dictionary<string, object>
            {
                ["AuthMethod"] = "RPS",
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RpsTicket"] = "d=" + msToken,
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        };

        (HttpStatusCode status, string body) = await PostJsonAsync(XblUrl, payload);
        if (status != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"XBL 认证失败（{(int)status}）：{body}");
        }

        XboxResponse? response = JsonSerializer.Deserialize<XboxResponse>(body, Json);
        string? token = response?.Token;
        string? uhs = response?.DisplayClaims?.Xui?.FirstOrDefault()?.Uhs;
        if (token is null || uhs is null)
        {
            throw new InvalidOperationException("XBL 响应里没有 token/uhs");
        }

        return new XboxTokens(token, uhs);
    }

    private static async Task<XboxTokens> XstsAsync(string xblToken, string uhs)
    {
        var payload = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken },
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        };

        (HttpStatusCode status, string body) = await PostJsonAsync(XstsUrl, payload);
        if (status != HttpStatusCode.OK)
        {
            XstsError? error = TryParse<XstsError>(body);
            throw new InvalidOperationException(DescribeXstsError(status, error?.XErr, body));
        }

        XboxResponse? response = JsonSerializer.Deserialize<XboxResponse>(body, Json);
        string? token = response?.Token;
        if (token is null)
        {
            throw new InvalidOperationException("XSTS 响应里没有 token");
        }

        return new XboxTokens(token, uhs);
    }

    private static string DescribeXstsError(HttpStatusCode status, long? xerr, string body)
    {
        return xerr switch
        {
            2148916233 => "这个微软账号还没有 Xbox 档案，先去 xbox.com 登录一次创建一个",
            2148916235 => "你的账号所在地区不支持 Xbox Live",
            2148916236 => "需要先完成成人验证",
            2148916237 => "需要先完成成人验证",
            2148916238 => "这是未成年账号，需要家长把它加入 Microsoft 家庭组",
            _ => $"XSTS 认证失败（{(int)status}，XErr={xerr}）：{body}",
        };
    }

    private static async Task<(string Token, int ExpiresIn)> MinecraftLoginAsync(string uhs, string xstsToken)
    {
        var payload = new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" };

        (HttpStatusCode status, string body) = await PostJsonAsync(McLoginUrl, payload);
        if (status != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Minecraft 登录失败（{(int)status}）：{body}");
        }

        TokenResponse? token = JsonSerializer.Deserialize<TokenResponse>(body, Json);
        if (token?.AccessToken is null)
        {
            throw new InvalidOperationException("Minecraft 登录响应里没有 access_token");
        }

        return (token.AccessToken, Math.Max(token.ExpiresIn, 60));
    }

    private static async Task<(string Id, string Name)> MinecraftProfileAsync(string mcToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, McProfileUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);

        using HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("这个账号没有购买 Minecraft（档案接口返回 404）");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"查询档案失败（{(int)response.StatusCode}）：{body}");
        }

        ProfileResponse? profile = JsonSerializer.Deserialize<ProfileResponse>(body, Json);
        if (profile?.Id is null || profile.Name is null)
        {
            throw new InvalidOperationException("档案响应里没有 id/name");
        }

        return (profile.Id, profile.Name);
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostFormAsync(string url, Dictionary<string, string> form)
    {
        using var content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await Http.PostAsync(url, content).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostJsonAsync(string url, object payload)
    {
        using StringContent content = new(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.PostAsync(url, content).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static T? TryParse<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record XboxTokens(string Token, string Uhs);

    private sealed class DeviceCodeResponse
    {
        public string? DeviceCode { get; set; }
        public string? UserCode { get; set; }
        public string? VerificationUri { get; set; }
        public int ExpiresIn { get; set; }
        public int Interval { get; set; }
        public string? Message { get; set; }
    }

    private sealed class TokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
    }

    private sealed class TokenError
    {
        public string? Error { get; set; }
        public string? ErrorDescription { get; set; }
    }

    private sealed class XboxResponse
    {
        public string? Token { get; set; }
        public DisplayClaims? DisplayClaims { get; set; }
    }

    private sealed class DisplayClaims
    {
        public List<Xui>? Xui { get; set; }
    }

    private sealed class Xui
    {
        public string? Uhs { get; set; }
    }

    private sealed class XstsError
    {
        public long? XErr { get; set; }
        public string? Message { get; set; }
    }

    private sealed class ProfileResponse
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }
}
