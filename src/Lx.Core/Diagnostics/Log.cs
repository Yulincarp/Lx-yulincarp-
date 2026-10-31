using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lx.Probe.Diagnostics;

// 启动器日志：按天写文件、5 MB 轮转、保留 14 天。
// 铁律：写日志失败绝不能影响主流程（退到 Debug 输出继续跑）。
public static class Log
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int KeepDays = 14;

    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string _directory = Path.Combine(Path.GetTempPath(), AppInfo.DataDirectoryName, "logs");
    private static string? _currentFile;
    private static string? _currentJsonFile;

    public static string DirectoryPath => _directory;

    public static string? CurrentFile
    {
        get
        {
            lock (Gate)
            {
                return _currentFile;
            }
        }
    }

    // 结构化日志文件（JSON Lines），和文本日志同目录、同名不同后缀。
    public static string? CurrentJsonFile
    {
        get
        {
            lock (Gate)
            {
                return _currentJsonFile;
            }
        }
    }

    // 由 App 在启动时调用；按"安装目录 -> LocalAppData -> TEMP -> exe 目录"挑第一个真正可写的。
    public static void Initialize(string directory)
    {
        lock (Gate)
        {
            _directory = ResolveWritableDirectory(directory);
            _currentFile = null;
            CleanupOldFiles();
        }

        Info($"日志已启用：{DirectoryPath}");
    }

    // 只 CreateDirectory 不够：目录存在但不可写时，第一次写日志才会炸。
    // 所以这里真的写一个探针文件，写不进去就换下一个候选。
    private static string ResolveWritableDirectory(string preferred)
    {
        var candidates = new List<string> { preferred };

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length > 0)
        {
            candidates.Add(Path.Combine(localAppData, AppInfo.DataDirectoryName, "logs"));
        }

        candidates.Add(Path.Combine(Path.GetTempPath(), AppInfo.DataDirectoryName, "logs"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "logs"));

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidate);
                string probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 换下一个候选
            }
        }

        // 全都不行：返回 exe 旁边的 logs，后续写入失败只会落到 Debug 输出，不会崩。
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "logs"));
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public static void Write(string level, string message, Exception? exception)
    {
        DateTime now = DateTime.Now;
        string safeMessage = LogRedactor.Redact(message);
        string? safeDetail = exception is null ? null : LogRedactor.Redact(exception.ToString());
        string line = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {safeMessage}";

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);

                string path = FileFor(now);
                RotateIfTooLarge(path);

                var builder = new StringBuilder(line).Append(Environment.NewLine);
                if (safeDetail is not null)
                {
                    builder.Append(safeDetail).Append(Environment.NewLine);
                }

                File.AppendAllText(path, builder.ToString(), Utf8);
                _currentFile = path;

                // 结构化副本：一行一个 JSON，方便 jq/grep；文本日志保持原样给人看
                string jsonPath = JsonFileFor(now);
                RotateIfTooLarge(jsonPath);
                File.AppendAllText(jsonPath, FormatJsonLine(level, safeMessage, exception, now) + Environment.NewLine, Utf8);
                _currentJsonFile = jsonPath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 日志写不进去也不能让启动器崩
                Debug.WriteLine(line);
            }
        }
    }

    // 单行 JSON：ts / level / msg / trace（LxException）/ ex{type,msg}。测试直接调这个。
    public static string FormatJsonLine(string level, string message, Exception? exception, DateTime now)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ts"] = now.ToString("O"),
            ["level"] = level,
            ["msg"] = LogRedactor.Redact(message),
        };

        if (exception is LxException lx)
        {
            payload["trace"] = lx.TraceId;
        }

        if (exception is not null)
        {
            payload["ex"] = new Dictionary<string, object?>
            {
                ["type"] = exception.GetType().Name,
                ["msg"] = LogRedactor.Redact(exception.Message),
            };
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string FileFor(DateTime now) =>
        Path.Combine(_directory, $"launcher-{now:yyyy-MM-dd}.log");

    private static string JsonFileFor(DateTime now) =>
        Path.Combine(_directory, $"launcher-{now:yyyy-MM-dd}.jsonl");

    private static void RotateIfTooLarge(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxFileBytes)
            {
                return;
            }

            string stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            string archive = Path.Combine(
                _directory,
                $"{Path.GetFileNameWithoutExtension(path)}-{stamp}{Path.GetExtension(path)}");
            int suffix = 1;
            while (File.Exists(archive))
            {
                archive = Path.Combine(
                    _directory,
                    $"{Path.GetFileNameWithoutExtension(path)}-{stamp}-{suffix++}{Path.GetExtension(path)}");
            }

            File.Move(path, archive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 轮转失败就继续写原文件
        }
    }

    private static void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            DateTime cutoff = DateTime.Now.AddDays(-KeepDays);
            foreach (string pattern in new[] { "launcher-*.log", "launcher-*.jsonl" })
            {
                foreach (string file in Directory.EnumerateFiles(_directory, pattern))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 单个文件删不掉不影响其他文件
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 扫描失败也不影响启动
        }
    }
}
