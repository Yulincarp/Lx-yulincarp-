using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    public static async Task<Account> LoginAsync(string apiRoot, string username, string password)
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

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(DescribeError(body, response.StatusCode));
        }

        AuthenticateResponse? result = JsonSerializer.Deserialize<AuthenticateResponse>(body, JsonOptions);
        if (result?.AccessToken is null ||
            result.SelectedProfile?.Id is null ||
            result.SelectedProfile.Name is null)
        {
            throw new InvalidOperationException("登录成功但没拿到 selectedProfile（账号可能还没创建角色）");
        }

        return new Account(
            "yggdrasil",
            result.SelectedProfile.Name,
            result.SelectedProfile.Id.Replace("-", string.Empty, StringComparison.Ordinal),
            result.AccessToken,
            apiRoot);
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
    }

    private sealed class Profile
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
