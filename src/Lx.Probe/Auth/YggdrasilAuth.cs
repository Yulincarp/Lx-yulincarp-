using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lx.Probe.Model;

namespace Lx.Probe.Auth;

// 外置登录（authlib-injector / Yggdrasil 协议），LittleSkin 这类站点都走这套。
public static class YggdrasilAuth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<Account> LoginAsync(string apiRoot, string username, string password, bool debug = false, string? preferredRole = null)
    {
        apiRoot = apiRoot.TrimEnd('/');
        string url = apiRoot + "/authserver/authenticate";

        var payload = new
        {
            username,
            password,
            clientToken = Guid.NewGuid().ToString("N"),
            agent = new { name = "Minecraft", version = 1 },
            requestUser = false,
        };

        using HttpResponseMessage response = await Http.PostAsJsonAsync(url, payload).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (debug)
        {
            Console.WriteLine("[debug] 响应：" + Redact(body));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeError(body, response.StatusCode));
        }

        AuthenticateResponse? result = JsonSerializer.Deserialize<AuthenticateResponse>(body, JsonOptions);
        if (result?.AccessToken is null)
        {
            throw new InvalidOperationException("登录成功但响应里没有 accessToken");
        }

        Profile? profile = result.SelectedProfile ?? result.AvailableProfiles?.FirstOrDefault();
        if (profile?.Id is null || profile.Name is null)
        {
            throw new InvalidOperationException("这个账号在站点上没有任何角色，先去 LittleSkin 建一个角色");
        }

        if (result.SelectedProfile is null)
        {
            Console.WriteLine($"[提示] 服务端没返回默认角色，先用 availableProfiles 里的第一个：{profile.Name}");
        }

        return new Account(
            "yggdrasil",
            profile.Name,
            profile.Id.Replace("-", string.Empty, StringComparison.Ordinal),
            result.AccessToken,
            apiRoot);
    }

    // 用 refresh 把 token 重新绑定到指定角色（Yggdrasil 标准做法）。
    public static async Task<Account> SwitchRoleAsync(Account account, string roleName)
    {
        if (account.ApiRoot is null || account.AvailableRoles is null || account.AvailableRoles.Count == 0)
        {
            throw new InvalidOperationException("这个账号没有角色列表，先重新登录一次（--relogin）");
        }

        AccountRole? role = account.AvailableRoles.FirstOrDefault(item =>
            string.Equals(item.Name, roleName, StringComparison.OrdinalIgnoreCase));

        if (role is null)
        {
            string names = string.Join("、", account.AvailableRoles.Select(item => item.Name));
            throw new InvalidOperationException($"没有角色 {roleName}（可选：{names}）");
        }

        if (string.Equals(account.Name, role.Name, StringComparison.OrdinalIgnoreCase))
        {
            return account;
        }

        string token = await RefreshTokenAsync(account.ApiRoot, account.AccessToken, role).ConfigureAwait(false);
        return account with
        {
            Name = role.Name,
            Uuid = role.Id.Replace("-", string.Empty, StringComparison.Ordinal),
            AccessToken = token,
        };
    }

    private static async Task<string> RefreshTokenAsync(string apiRoot, string accessToken, AccountRole role)
    {
        string api = apiRoot.TrimEnd('/');
        string url = api + "/authserver/refresh";

        object payload = new
        {
            accessToken,
            selectedProfile = new { id = role.Id, name = role.Name },
            requestUser = false,
        };

        (HttpStatusCode status, string body) = await PostJsonAsync(url, payload).ConfigureAwait(false);

        if (!status.Equals(HttpStatusCode.OK))
        {
            // 有些实现只认 uuid 字符串
            payload = new { accessToken, selectedProfile = role.Id };
            (status, body) = await PostJsonAsync(url, payload).ConfigureAwait(false);
        }

        if (!status.Equals(HttpStatusCode.OK))
        {
            throw new InvalidOperationException($"切换角色失败（{(int)status}）：{DescribeError(body, status)}");
        }

        AuthenticateResponse? result = JsonSerializer.Deserialize<AuthenticateResponse>(body, JsonOptions);
        return result?.AccessToken
               ?? throw new InvalidOperationException("切换角色成功但没拿到新 token");
    }

    private static async Task<(HttpStatusCode Status, string Body)> PostJsonAsync(string url, object payload)
    {
        using HttpResponseMessage response = await Http.PostAsJsonAsync(url, payload).ConfigureAwait(false);
        return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static string DescribeError(string body, HttpStatusCode status)
    {
        try
        {
            ErrorResponse? error = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions);
            if (error?.ErrorMessage is { Length: > 0 } message)
            {
                return $"登录失败（{(int)status}）：{message}";
            }

            if (error?.Error is { Length: > 0 } code)
            {
                return $"登录失败（{(int)status}）：{code}";
            }
        }
        catch (JsonException)
        {
            // 不是 JSON 就退回原文
        }

        return $"登录失败（{(int)status}）：{body}";
    }

    private sealed class AuthenticateResponse
    {
        public string? AccessToken { get; set; }
        public string? ClientToken { get; set; }
        public Profile? SelectedProfile { get; set; }
        public List<Profile>? AvailableProfiles { get; set; }
    }

    private sealed class Profile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    // 把 accessToken 打码，方便把响应贴出来排查。
    private static string Redact(string json) =>
        Regex.Replace(json, @"(""accessToken""\s*:\s*"")[^""]+", "$1***");

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
