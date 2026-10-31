using System.Text.Json;
using Lx.Probe;
using Lx.Probe.Diagnostics;
using Lx.Probe.Model;

namespace Lx.Tests;

public class CrashReporterTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Build_CapturesContextAndStack()
    {
        Exception captured;
        try
        {
            throw new InvalidOperationException("下载失败", new IOException("磁盘写入失败"));
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        CrashReport report = CrashReporter.Build(captured, "启动游戏", logTailLines: 0);

        Assert.Equal("启动游戏", report.Context);
        Assert.Equal(8, report.TraceId.Length);
        Assert.Equal(typeof(InvalidOperationException).FullName, report.ExceptionType);
        Assert.Equal("下载失败", report.ExceptionMessage);
        Assert.Contains("CrashReporterTests", report.StackTrace);
        Assert.Equal(AppInfo.Version, report.Version);
    }

    [Fact]
    public void Build_RedactsSecretsInMessage()
    {
        var exception = new InvalidOperationException("请求 https://x/api?access_token=SECRET1234567890 失败");

        CrashReport report = CrashReporter.Build(exception, "同步");

        Assert.DoesNotContain("SECRET1234567890", report.ExceptionMessage);
        Assert.Contains("access_token", report.ExceptionMessage);
    }

    [Fact]
    public void WriteLocal_CreatesJsonFile()
    {
        string root = TempDir();
        try
        {
            CrashReport report = CrashReporter.Build(new Exception("boom"), "测试", logTailLines: 0);

            string path = CrashReporter.WriteLocal(report, root);

            Assert.True(File.Exists(path));
            Assert.StartsWith(CrashReporter.DirectoryFor(root), path, StringComparison.OrdinalIgnoreCase);

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(report.TraceId, doc.RootElement.GetProperty("TraceId").GetString());
            Assert.Equal("测试", doc.RootElement.GetProperty("Context").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShouldUpload_DefaultsToOff()
    {
        var settings = new LaunchSettings();

        Assert.False(CrashReporter.ShouldUpload(settings, out _));
        Assert.False(settings.TelemetryEnabled);
    }

    [Fact]
    public void ShouldUpload_RequiresEnabledAndValidEndpoint()
    {
        var enabled = new LaunchSettings(TelemetryEnabled: true, CrashReportUrl: "https://crash.example.com/report");
        Assert.True(CrashReporter.ShouldUpload(enabled, out string endpoint));
        Assert.Equal("https://crash.example.com/report", endpoint);

        var noUrl = new LaunchSettings(TelemetryEnabled: true);
        Assert.False(CrashReporter.ShouldUpload(noUrl, out _));

        var badUrl = new LaunchSettings(TelemetryEnabled: true, CrashReportUrl: "not-a-url");
        Assert.False(CrashReporter.ShouldUpload(badUrl, out _));

        var disabledWithUrl = new LaunchSettings(TelemetryEnabled: false, CrashReportUrl: "https://crash.example.com/report");
        Assert.False(CrashReporter.ShouldUpload(disabledWithUrl, out _));
    }

    [Fact]
    public void PrivacyNotice_MentionsScopeAndDefaultOff()
    {
        string notice = CrashReporter.PrivacyNotice;

        Assert.Contains("不包含", notice);
        Assert.Contains("默认", notice);
        Assert.Contains("token", notice);
    }
}
