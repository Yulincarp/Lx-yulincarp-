using System.Net.Http;
using System.Text.Json;

namespace Lx.Probe.Diagnostics;

// 错误的类别：界面按这个决定说人话的方式，日志按这个统计。
public enum LxErrorKind
{
    Cancelled,      // 用户主动取消
    Timeout,        // 等太久
    Network,        // 连不上 / 服务器报错 / 断流
    NotFound,       // 文件或目录不存在
    Io,             // 磁盘读写、权限
    InvalidConfig,  // 配置/清单格式不对
    InvalidState,   // 状态不对（没登录、没装好）
    Unknown,
}

// 全启动器统一的异常：类别 + 人话 + 短 trace id。
// trace id 会写进日志、错误卡片和剪贴板文本，用户报错时对得上同一件事。
public sealed class LxException : Exception
{
    public LxException(
        LxErrorKind kind,
        string context,
        string userMessage,
        string? traceId = null,
        Exception? inner = null)
        : base(userMessage, inner)
    {
        Kind = kind;
        Context = context;
        UserMessage = userMessage;
        TraceId = string.IsNullOrWhiteSpace(traceId) ? NewTraceId() : traceId!;
    }

    public LxErrorKind Kind { get; }

    // 当时在做什么（"启动游戏"、"同步整合包"…）
    public string Context { get; }

    // 给用户看的一句话；技术细节在 InnerException 和日志里
    public string UserMessage { get; }

    public string TraceId { get; }

    public static string NewTraceId() => Guid.NewGuid().ToString("N")[..8];

    public static string KindText(LxErrorKind kind) => kind switch
    {
        LxErrorKind.Cancelled => "已取消",
        LxErrorKind.Timeout => "超时",
        LxErrorKind.Network => "网络错误",
        LxErrorKind.NotFound => "文件不存在",
        LxErrorKind.Io => "磁盘/权限错误",
        LxErrorKind.InvalidConfig => "配置有误",
        LxErrorKind.InvalidState => "状态不对",
        _ => "未知错误",
    };

    // 把任意异常翻译成统一模型；已经是 LxException 的原样返回（保留它自己的 trace id）。
    public static LxException From(Exception exception, string context)
    {
        if (exception is LxException lx)
        {
            return lx;
        }

        string id = NewTraceId();

        return exception switch
        {
            // TaskCanceledException 是 OperationCanceledException 的子类，必须先匹配；
            // 只有内层是 TimeoutException 的才当超时，其余按"用户取消"处理。
            TaskCanceledException { InnerException: TimeoutException } =>
                new LxException(LxErrorKind.Timeout, context, "服务器长时间没有响应（超时）", id, exception),

            OperationCanceledException =>
                new LxException(LxErrorKind.Cancelled, context, "操作已取消", id, exception),

            TimeoutException =>
                new LxException(LxErrorKind.Timeout, context, $"{context}超时，检查网络后重试", id, exception),

            HttpRequestException http =>
                new LxException(LxErrorKind.Network, context, $"网络请求失败：{Short(http.Message)}", id, exception),

            FileNotFoundException or DirectoryNotFoundException =>
                new LxException(LxErrorKind.NotFound, context, $"文件或目录不存在：{Short(exception.Message)}", id, exception),

            UnauthorizedAccessException =>
                new LxException(LxErrorKind.Io, context, "没有权限读写文件，换个目录或以管理员运行试试", id, exception),

            IOException io =>
                new LxException(LxErrorKind.Io, context, $"读写文件失败：{Short(io.Message)}", id, exception),

            JsonException =>
                new LxException(LxErrorKind.InvalidConfig, context, $"配置文件格式不对：{Short(exception.Message)}", id, exception),

            InvalidOperationException =>
                new LxException(LxErrorKind.InvalidState, context, exception.Message, id, exception),

            _ => new LxException(LxErrorKind.Unknown, context, exception.Message, id, exception),
        };
    }

    // 错误卡片/剪贴板里显示的完整信息。
    public string Detail
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"【{KindText(Kind)}】{Context}");
            builder.AppendLine(UserMessage);
            builder.AppendLine($"trace: {TraceId}");
            builder.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            Exception? inner = InnerException;
            int depth = 0;
            while (inner is not null && depth < 5)
            {
                builder.AppendLine($"-- {inner.GetType().Name}: {inner.Message}");
                inner = inner.InnerException;
                depth++;
            }

            if (StackTrace is { Length: > 0 } stack)
            {
                builder.AppendLine();
                builder.AppendLine(stack);
            }

            return builder.ToString();
        }
    }

    public override string ToString() =>
        $"[{KindText(Kind)}] trace={TraceId} {Context}: {UserMessage}{Environment.NewLine}{base.ToString()}";

    private static string Short(string text) =>
        text.Length <= 160 ? text : text[..160] + "…";
}
