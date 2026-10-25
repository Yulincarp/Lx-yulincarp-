using System.Diagnostics;
using System.Text;

namespace Lx.Probe.Diagnostics;

// 启动器日志：按天写文件、5 MB 轮转、保留 14 天。
// 铁律：写日志失败绝不能影响主流程（退到 Debug 输出继续跑）。
public static class Log
{
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int KeepDays = 14;

    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static string _directory = Path.Combine(Path.GetTempPath(), "Lx", "logs");
    private static string? _currentFile;

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
            candidates.Add(Path.Combine(localAppData, "Lx", "logs"));
        }

        candidates.Add(Path.Combine(Path.GetTempPath(), "Lx", "logs"));
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
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                string path = FileFor(DateTime.Now);
                RotateIfTooLarge(path);

                var builder = new StringBuilder(line).Append(Environment.NewLine);
                if (exception is not null)
                {
                    builder.Append(exception).Append(Environment.NewLine);
                }

                File.AppendAllText(path, builder.ToString(), Utf8);
                _currentFile = path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 日志写不进去也不能让启动器崩
                Debug.WriteLine(line);
            }
        }
    }

    private static string FileFor(DateTime now) =>
        Path.Combine(_directory, $"launcher-{now:yyyy-MM-dd}.log");

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
            string archive = Path.Combine(_directory, $"launcher-{stamp}.log");
            int suffix = 1;
            while (File.Exists(archive))
            {
                archive = Path.Combine(_directory, $"launcher-{stamp}-{suffix++}.log");
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
            foreach (string file in Directory.EnumerateFiles(_directory, "launcher-*.log"))
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 扫描失败也不影响启动
        }
    }
}
