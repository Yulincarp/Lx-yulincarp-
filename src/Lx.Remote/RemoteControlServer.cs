using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lx.Probe.Remote;

public sealed record RemoteInstanceInfo(
    string Name,
    string VersionId,
    string? Loader,
    string? LoaderVersion,
    string? AccountName);

public sealed record RemoteSnapshot(
    string Status,
    bool GameRunning,
    bool Launching,
    IReadOnlyList<RemoteInstanceInfo> Instances);

public sealed record RemoteLaunchRequest(string Name);

public interface IRemoteHost
{
    RemoteSnapshot Snapshot();
    string GetLogTail(int lines);
    Task<string> LaunchAsync(string instanceName);
}

public sealed class RemoteControlServer : IAsyncDisposable
{
    private readonly IRemoteHost _host;
    private readonly string _product;
    private readonly string _version;
    private WebApplication? _app;

    public RemoteControlServer(IRemoteHost host, string product, string version)
    {
        _host = host;
        _product = product;
        _version = version;
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }

    public int Port { get; private set; } = 47821;
    public string Token { get; }
    public string Url { get; private set; } = string.Empty;
    public bool IsRunning => _app is not null;

    public async Task StartAsync()
    {
        if (_app is not null)
        {
            return;
        }

        Exception? last = null;
        for (int port = 47821; port <= 47830; port++)
        {
            try
            {
                WebApplication app = Build(port);
                await app.StartAsync().ConfigureAwait(false);
                _app = app;
                Port = port;
                Url = $"http://{GetLanAddress()}:{port}/?token={Token}";
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                last = ex;
            }
        }

        throw new IOException("手机遥控端口 47821-47830 都被占用，或防火墙拒绝监听", last);
    }

    public async Task StopAsync()
    {
        WebApplication? app = _app;
        _app = null;
        Url = string.Empty;
        if (app is not null)
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private WebApplication Build(int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        WebApplication app = builder.Build();
        app.UseCors();

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                string? supplied = context.Request.Headers["X-Lx-Token"].FirstOrDefault()
                    ?? context.Request.Query["token"].FirstOrDefault();
                if (!string.Equals(supplied, Token, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { error = "token 无效" });
                    return;
                }
            }

            await next();
        });

        app.MapGet("/", () => Results.Content(MobilePage.Html, "text/html; charset=utf-8"));
        app.MapGet("/api/ping", () => Results.Json(new
        {
            ok = true,
            product = _product,
            version = _version,
            serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        }));
        app.MapGet("/api/snapshot", () => Results.Json(_host.Snapshot()));
        app.MapGet("/api/logs", (int? lines) => Results.Json(new
        {
            text = _host.GetLogTail(Math.Clamp(lines ?? 200, 20, 500)),
        }));

        app.MapPost("/api/launch", async (HttpContext context) =>
        {
            RemoteLaunchRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<RemoteLaunchRequest>();
            }
            catch
            {
                return Results.BadRequest(new { error = "请求格式错误" });
            }

            if (string.IsNullOrWhiteSpace(request?.Name))
            {
                return Results.BadRequest(new { error = "缺少实例名" });
            }

            try
            {
                string message = await _host.LaunchAsync(request.Name);
                return Results.Json(new { ok = true, message });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
            }
        });

        return app;
    }

    public static string GetLanAddress()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .ToList();

            string? preferred = candidates.FirstOrDefault(ip =>
                ip.StartsWith("192.168.", StringComparison.Ordinal) ||
                ip.StartsWith("10.", StringComparison.Ordinal) ||
                IsPrivate172(ip));
            return preferred ?? candidates.FirstOrDefault() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static bool IsPrivate172(string ip)
    {
        string[] parts = ip.Split('.');
        return parts.Length == 4 && parts[0] == "172" && int.TryParse(parts[1], out int second) && second is >= 16 and <= 31;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
