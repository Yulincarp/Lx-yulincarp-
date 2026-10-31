using System.Diagnostics;
using System.Windows;
using Lx.Probe.Diagnostics;
using Lx.Probe.Localization;

namespace Lx.UI;

// 统一的错误卡片：类别 + 人话 + trace id + 可复制的详情。
// 不管是取消、超时、网络失败还是别的异常，用户看到的都是这一个窗口。
public partial class ErrorWindow : Window
{
    private readonly LxException _error;

    public ErrorWindow(LxException error, string? extra = null)
    {
        InitializeComponent();
        _error = error;

        string kind = KindLabel(error.Kind);
        Title = $"{kind}：{error.Context}";
        KindText.Text = $"{kind} · {error.Context}";
        MessageText.Text = string.IsNullOrEmpty(extra) ? error.UserMessage : $"{error.UserMessage}\n\n{extra}";
        TraceText.Text = $"trace {error.TraceId}  ·  {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        DetailBox.Text = error.Detail;
    }

    // 错误类别按当前语言显示；日志里仍用 LxException.KindText（中文）。
    private static string KindLabel(LxErrorKind kind) => kind switch
    {
        LxErrorKind.Cancelled => L10n.T("S271"),
        LxErrorKind.Timeout => L10n.T("S261"),
        LxErrorKind.Network => L10n.T("S262"),
        LxErrorKind.NotFound => L10n.T("S263"),
        LxErrorKind.Io => L10n.T("S264"),
        LxErrorKind.InvalidConfig => L10n.T("S265"),
        LxErrorKind.InvalidState => L10n.T("S266"),
        _ => L10n.T("S267"),
    };

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_error.Detail);
            TraceText.Text = $"已复制 · trace {_error.TraceId}";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            MessageBox.Show(this, "复制失败，请手动选中详情文本。", "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        string? file = Log.CurrentFile;
        string dir = Log.DirectoryPath;

        try
        {
            if (file is not null && System.IO.File.Exists(file))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"打不开日志目录：{dir}", "打开失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
