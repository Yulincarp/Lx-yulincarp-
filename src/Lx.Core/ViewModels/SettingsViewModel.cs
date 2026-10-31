using Lx.Probe.Model;
using Lx.Probe.Resolve;

namespace Lx.Probe.ViewModels;

// 设置页的业务规则：把界面上的原始输入（都是字符串）校验成 LaunchSettings。
// 窗口只负责把控件值搬进来、把错误提示显示出去，规则本身可以单测。
public sealed class SettingsViewModel
{
    public string MinMemoryMb { get; set; } = "1024";

    public string MaxMemoryMb { get; set; } = "4096";

    public string JvmArgs { get; set; } = string.Empty;

    public string WindowWidth { get; set; } = "854";

    public string WindowHeight { get; set; } = "480";

    public bool Fullscreen { get; set; }

    public string DownloadConcurrency { get; set; } = "8";

    public string? JavaPath { get; set; }

    public string DownloadSource { get; set; } = DownloadSourceSettings.Official;

    public string CustomDownloadBase { get; set; } = string.Empty;

    public string ProxyMode { get; set; } = "system";

    public string ProxyUrl { get; set; } = string.Empty;

    public string UpdateChannel { get; set; } = "stable";

    public bool DarkTheme { get; set; }

    public static SettingsViewModel From(LaunchSettings settings) => new()
    {
        MinMemoryMb = settings.MinMemoryMb.ToString(),
        MaxMemoryMb = settings.MaxMemoryMb.ToString(),
        JvmArgs = settings.JvmArgs ?? string.Empty,
        WindowWidth = settings.WindowWidth.ToString(),
        WindowHeight = settings.WindowHeight.ToString(),
        Fullscreen = settings.Fullscreen,
        DownloadConcurrency = Math.Clamp(settings.DownloadConcurrency, 1, 16).ToString(),
        JavaPath = settings.JavaPath,
        DownloadSource = settings.DownloadSource ?? DownloadSourceSettings.Official,
        CustomDownloadBase = settings.CustomDownloadBase ?? string.Empty,
        ProxyMode = settings.ProxyMode ?? "system",
        ProxyUrl = settings.ProxyUrl ?? string.Empty,
        UpdateChannel = settings.UpdateChannel ?? "stable",
        DarkTheme = string.Equals(settings.Theme, "dark", StringComparison.OrdinalIgnoreCase),
    };

    // 校验通过就给出完整的 LaunchSettings；否则 settings 为 null，error 是给用户看的一句话。
    public bool TryBuild(out LaunchSettings? settings, out string error)
    {
        settings = null;
        error = string.Empty;

        if (!int.TryParse(MinMemoryMb.Trim(), out int min) ||
            !int.TryParse(MaxMemoryMb.Trim(), out int max))
        {
            error = "内存要填数字（单位 MB）";
            return false;
        }

        if (min < LaunchSettings.MinAllowedMb || max > LaunchSettings.MaxAllowedMb || min > max)
        {
            error = $"内存范围不对：最小 {LaunchSettings.MinAllowedMb}，最大 {LaunchSettings.MaxAllowedMb}，且最小不能大于最大";
            return false;
        }

        if (!int.TryParse(WindowWidth.Trim(), out int width) ||
            !int.TryParse(WindowHeight.Trim(), out int height) ||
            width < LaunchSettings.MinWindowWidth || height < LaunchSettings.MinWindowHeight ||
            width > LaunchSettings.MaxWindowSize || height > LaunchSettings.MaxWindowSize)
        {
            error = $"窗口大小不对：宽 ≥ {LaunchSettings.MinWindowWidth}，高 ≥ {LaunchSettings.MinWindowHeight}，最大 {LaunchSettings.MaxWindowSize}";
            return false;
        }

        if (!int.TryParse(DownloadConcurrency.Trim(), out int concurrency) || concurrency < 1 || concurrency > 16)
        {
            error = "并发数要填 1 ~ 16";
            return false;
        }

        string sourceKey = DownloadSource;
        string? customBase = null;
        if (sourceKey == DownloadSourceSettings.Custom)
        {
            customBase = DownloadUrlRewriter.NormalizeBase(CustomDownloadBase);
            if (customBase.Length == 0)
            {
                error = "自定义下载源要填 http/https 开头的地址";
                return false;
            }
        }

        string proxyMode = ProxyMode;
        string? proxyUrl = null;
        if (proxyMode == "manual")
        {
            proxyUrl = ProxyUrl.Trim();
            if (!Uri.TryCreate(proxyUrl, UriKind.Absolute, out Uri? proxyUri) ||
                (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps))
            {
                error = "手动代理要填 http:// 或 https:// 地址";
                return false;
            }
        }

        settings = new LaunchSettings(
            MinMemoryMb: min,
            MaxMemoryMb: max,
            JavaPath: JavaPath,
            DownloadSource: sourceKey,
            CustomDownloadBase: customBase,
            JvmArgs: string.IsNullOrWhiteSpace(JvmArgs) ? null : JvmArgs.Trim(),
            WindowWidth: width,
            WindowHeight: height,
            Fullscreen: Fullscreen,
            DownloadConcurrency: concurrency,
            ProxyMode: proxyMode,
            ProxyUrl: proxyUrl,
            UpdateChannel: UpdateChannel,
            Theme: DarkTheme ? "dark" : "light");

        return true;
    }
}
