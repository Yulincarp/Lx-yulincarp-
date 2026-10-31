using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lx.Probe.Model;
using Lx.Probe.Net;

namespace Lx.Probe.Diagnostics;

// 一份崩溃报告：版本、系统、异常链、最近的日志尾巴（已脱敏）。
// 默认只写到本地 logs\crash\，用户明确同意并填了地址才会上传。
public sealed record CrashReport(
    string Version,
    string Os,
    string Runtime,
    DateTimeOffset Time,
    string Context,
    string TraceId,
    string ExceptionType,
    string ExceptionMessage,
    string StackTrace,
    IReadOnlyList<string> LogTail,
    bool Uploaded,
    string? UploadError = null);

public static class CrashReporter
{
    public const int DefaultLogTailLines = 80;
    public const string EndpointEnvironmentVariable = "LX_CRASH_ENDPOINT";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static HttpClient Http => ProxyConfig.GetClient(TimeSpan.FromSeconds(10));

    public static string DirectoryFor(string installRoot) => Path.Combine(installRoot, "logs", "crash");

    public static CrashReport Build(Exception exception, string context, int logTailLines = DefaultLogTailLines)
    {
        LxException wrapped = LxException.From(exception, context);

        return new CrashReport(
            Version: AppInfo.Version,
            Os: RuntimeInformation.OSDescription,
            Runtime: RuntimeInformation.FrameworkDescription,
            Time: DateTimeOffset.Now,
            Context: context,
            TraceId: wrapped.TraceId,
            ExceptionType: exception.GetType().FullName ?? exception.GetType().Name,
            ExceptionMessage: LogRedactor.Redact(exception.Message),
            StackTrace: LogRedactor.Redact(exception.StackTrace ?? string.Empty),
            LogTail: ReadLogTail(logTailLines),
            Uploaded: false);
    }

    // 只读最近 N 行，整份日志可能有几 MB。
    private static List<string> ReadLogTail(int lines)
    {
        var tail = new List<string>();
        string? file = Log.CurrentFile;
        if (file is null || !File.Exists(file) || lines <= 0)
        {
            return tail;
        }

        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new Queue<string>(lines);
            while (reader.ReadLine() is { } line)
            {
                if (buffer.Count == lines)
                {
                    buffer.Dequeue();
                }

                buffer.Enqueue(LogRedactor.Redact(line));
            }

            tail.AddRange(buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 日志读不到也不影响崩溃处理
        }

        return tail;
    }

    public static string WriteLocal(CrashReport report, string installRoot)
    {
        string dir = DirectoryFor(installRoot);
        Directory.CreateDirectory(dir);

        string name = $"crash-{report.Time:yyyyMMdd-HHmmss}-{report.TraceId}.json";
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
        return path;
    }

    // 只有"用户勾了同意"并且"有合法地址"才上传。
    public static bool ShouldUpload(LaunchSettings settings, out string endpoint)
    {
        endpoint = ResolveEndpoint(settings);
        if (!settings.TelemetryEnabled || endpoint.Length == 0)
        {
            return false;
        }

        return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static string ResolveEndpoint(LaunchSettings settings)
    {
        string fromEnv = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable) ?? string.Empty;
        string value = fromEnv.Length > 0 ? fromEnv : settings.CrashReportUrl ?? string.Empty;
        return value.Trim();
    }

    // 上传失败只记结果，不抛：崩溃处理路径里不能再炸一次。
    public static async Task<CrashReport> UploadAsync(CrashReport report, string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            string json = JsonSerializer.Serialize(report, JsonOptions);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await Http.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? report with { Uploaded = true }
                : report with { UploadError = $"HTTP {(int)response.StatusCode}" };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return report with { UploadError = LogRedactor.Redact(ex.Message) };
        }
    }

    // 隐私说明：设置页和错误卡片共用同一段文字。
    public static string PrivacyNotice =>
        "崩溃报告包含：" + Environment.NewLine +
        "· 启动器版本、Windows 版本、.NET 版本" + Environment.NewLine +
        "· 异常类型、异常消息、调用堆栈" + Environment.NewLine +
        "· 最近约 80 行日志（token、密钥已自动打码）" + Environment.NewLine +
        Environment.NewLine +
        "不包含：账号密码、存档、mods 内容、浏览记录。" + Environment.NewLine +
        "默认只保存在 logs\\crash\\ 本地；只有你勾选同意并填写上传地址后才会发送。";
}
