using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lx.Probe;
using Lx.Probe.Model;
using Lx.Probe.Modpacks;
using Lx.Probe.Resolve;

namespace Lx.UI.ViewModels;

// H3 第一步：设置摘要从 MainWindow 的字符串拼接移出来。
// 只负责展示数据，不碰控件。
public sealed class SettingsSummaryViewModel : INotifyPropertyChanged
{
    private string _summary = string.Empty;
    private string _dataDirectory = string.Empty;
    private string _curseForgeKeyStatus = string.Empty;

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public string DataDirectory
    {
        get => _dataDirectory;
        private set => Set(ref _dataDirectory, value);
    }

    public string CurseForgeKeyStatus
    {
        get => _curseForgeKeyStatus;
        private set => Set(ref _curseForgeKeyStatus, value);
    }

    public void Refresh(string installRoot)
    {
        LaunchSettings settings = SettingsStore.Load(installRoot);
        var source = new DownloadSourceSettings(settings.DownloadSource, settings.CustomDownloadBase);
        Summary =
            $"启动器版本：v{AppInfo.Version}{Environment.NewLine}" +
            $"内存：{settings.MinMemoryMb}–{settings.MaxMemoryMb} MB{Environment.NewLine}" +
            $"Java：{settings.JavaPath ?? "自动"}{Environment.NewLine}" +
            $"JVM 参数：{settings.JvmArgs ?? "（默认）"}{Environment.NewLine}" +
            $"窗口：{settings.WindowWidth}×{settings.WindowHeight}{(settings.Fullscreen ? " 全屏" : string.Empty)}{Environment.NewLine}" +
            $"下载源：{DownloadUrlRewriter.Describe(source)}{Environment.NewLine}" +
            $"并发：{settings.DownloadConcurrency}　代理：{settings.ProxyMode ?? "system"}{Environment.NewLine}" +
            $"更新通道：{settings.UpdateChannel ?? "stable"}　主题：{settings.Theme ?? "light"}";
        DataDirectory = $"数据目录：{installRoot}";
        CurseForgeKeyStatus = CurseForgeClient.ResolveKey(installRoot) is null
            ? "未保存 Key"
            : "已保存 Key（DPAPI 加密）";
    }

    public void SetCurseForgeKeyStatus(string status) => CurseForgeKeyStatus = status;

    public void SetDataDirectory(string dataRoot) => DataDirectory = $"数据目录：{dataRoot}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
