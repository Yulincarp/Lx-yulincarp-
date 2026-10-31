using Lx.Probe.Model;
using Lx.Probe.Resolve;
using Lx.Probe.ViewModels;

namespace Lx.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void From_MapsEveryField()
    {
        var settings = new LaunchSettings(
            MinMemoryMb: 2048,
            MaxMemoryMb: 8192,
            JavaPath: @"C:\Java\bin\java.exe",
            DownloadSource: DownloadSourceSettings.Bmclapi,
            CustomDownloadBase: null,
            JvmArgs: "-XX:+UseG1GC",
            WindowWidth: 1280,
            WindowHeight: 720,
            Fullscreen: true,
            DownloadConcurrency: 4,
            ProxyMode: "manual",
            ProxyUrl: "http://127.0.0.1:7890",
            UpdateChannel: "beta",
            Theme: "dark");

        SettingsViewModel vm = SettingsViewModel.From(settings);

        Assert.Equal("2048", vm.MinMemoryMb);
        Assert.Equal("8192", vm.MaxMemoryMb);
        Assert.Equal(@"C:\Java\bin\java.exe", vm.JavaPath);
        Assert.Equal(DownloadSourceSettings.Bmclapi, vm.DownloadSource);
        Assert.True(vm.Fullscreen);
        Assert.Equal("manual", vm.ProxyMode);
        Assert.Equal("beta", vm.UpdateChannel);
        Assert.Equal("dark", vm.Theme);

        Assert.True(vm.TryBuild(out LaunchSettings? rebuilt, out string error));
        Assert.Equal(string.Empty, error);
        Assert.NotNull(rebuilt);
        Assert.Equal(2048, rebuilt!.MinMemoryMb);
        Assert.Equal("dark", rebuilt.Theme);
        Assert.Equal("http://127.0.0.1:7890", rebuilt.ProxyUrl);
    }

    [Fact]
    public void TryBuild_RejectsNonNumericMemory()
    {
        var vm = new SettingsViewModel { MinMemoryMb = "abc", MaxMemoryMb = "4096" };

        Assert.False(vm.TryBuild(out LaunchSettings? settings, out string error));
        Assert.Null(settings);
        Assert.Contains("内存", error);
    }

    [Fact]
    public void TryBuild_RejectsMemoryRange()
    {
        var vm = new SettingsViewModel { MinMemoryMb = "8192", MaxMemoryMb = "1024" };

        Assert.False(vm.TryBuild(out _, out string error));
        Assert.Contains("最小不能大于最大", error);

        var tooSmall = new SettingsViewModel { MinMemoryMb = "1", MaxMemoryMb = "4096" };
        Assert.False(tooSmall.TryBuild(out _, out string error2));
        Assert.Contains("内存范围", error2);
    }

    [Fact]
    public void TryBuild_RejectsBadWindowAndConcurrency()
    {
        var badWindow = new SettingsViewModel { WindowWidth = "100", WindowHeight = "480" };
        Assert.False(badWindow.TryBuild(out _, out string windowError));
        Assert.Contains("窗口大小", windowError);

        var badConcurrency = new SettingsViewModel { DownloadConcurrency = "99" };
        Assert.False(badConcurrency.TryBuild(out _, out string concurrencyError));
        Assert.Contains("并发", concurrencyError);
    }

    [Fact]
    public void TryBuild_RequiresCustomSourceAndManualProxy()
    {
        var custom = new SettingsViewModel
        {
            DownloadSource = DownloadSourceSettings.Custom,
            CustomDownloadBase = "not-a-url",
        };
        Assert.False(custom.TryBuild(out _, out string customError));
        Assert.Contains("下载源", customError);

        custom.CustomDownloadBase = "https://mirror.example.com/mc/";
        Assert.True(custom.TryBuild(out LaunchSettings? built, out _));
        Assert.Equal("https://mirror.example.com/mc", built!.CustomDownloadBase?.TrimEnd('/'));

        var proxy = new SettingsViewModel { ProxyMode = "manual", ProxyUrl = "127.0.0.1:7890" };
        Assert.False(proxy.TryBuild(out _, out string proxyError));
        Assert.Contains("代理", proxyError);

        proxy.ProxyUrl = "http://127.0.0.1:7890";
        Assert.True(proxy.TryBuild(out LaunchSettings? withProxy, out _));
        Assert.Equal("http://127.0.0.1:7890", withProxy!.ProxyUrl);
    }

    [Fact]
    public void TryBuild_BlankJvmArgsBecomesNull_AndProxyLeftAloneWhenSystem()
    {
        var vm = new SettingsViewModel
        {
            JvmArgs = "   ",
            ProxyMode = "system",
            ProxyUrl = "http://ignored",
        };

        Assert.True(vm.TryBuild(out LaunchSettings? settings, out _));
        Assert.Null(settings!.JvmArgs);
        Assert.Null(settings.ProxyUrl);
    }

    [Fact]
    public void Telemetry_IsOffByDefault_AndUrlMayBeEmpty()
    {
        var vm = new SettingsViewModel();

        Assert.False(vm.TelemetryEnabled);
        Assert.True(vm.TryBuild(out LaunchSettings? settings, out _));
        Assert.False(settings!.TelemetryEnabled);
        Assert.Null(settings.CrashReportUrl);
    }

    [Fact]
    public void Telemetry_RejectsNonHttpUrl()
    {
        var vm = new SettingsViewModel { TelemetryEnabled = true, CrashReportUrl = "crash.example.com/report" };

        Assert.False(vm.TryBuild(out _, out string error));
        Assert.Contains("崩溃上报地址", error);
    }

    [Fact]
    public void Telemetry_KeepsValidUrlAndFlag()
    {
        var vm = new SettingsViewModel
        {
            TelemetryEnabled = true,
            CrashReportUrl = " https://crash.example.com/report ",
        };

        Assert.True(vm.TryBuild(out LaunchSettings? settings, out _));
        Assert.True(settings!.TelemetryEnabled);
        Assert.Equal("https://crash.example.com/report", settings.CrashReportUrl);
    }
}
