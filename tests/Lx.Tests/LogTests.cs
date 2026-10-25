using Lx.Probe.Diagnostics;

namespace Lx.Tests;

public class LogTests
{
    [Fact]
    public void Info_WritesToDailyFile()
    {
        string dir = NewTempDir();
        try
        {
            Log.Initialize(dir);
            Log.Info("hello-log");

            string? file = Log.CurrentFile;
            Assert.NotNull(file);
            Assert.StartsWith(Path.GetFullPath(dir), file!);

            string text = File.ReadAllText(file!);
            Assert.Contains("hello-log", text);
            Assert.Contains("[INFO]", text);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Error_IncludesExceptionStack()
    {
        string dir = NewTempDir();
        try
        {
            Log.Initialize(dir);
            Log.Error("boom", new InvalidOperationException("bad-state"));

            string text = File.ReadAllText(Log.CurrentFile!);
            Assert.Contains("boom", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("bad-state", text);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    // 不用 Path.GetTempPath()：CI/沙箱里 TEMP 可能指向一个不可写的用户目录。
    private static string NewTempDir() =>
        Path.Combine(AppContext.BaseDirectory, "log-tests", Guid.NewGuid().ToString("N"));

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清不掉不影响测试结果
        }
    }
}
