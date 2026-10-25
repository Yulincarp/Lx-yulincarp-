using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lx.Probe;
using Lx.Probe.Diagnostics;

namespace Lx.UI;

public partial class App : Application
{
    private static bool _showingCrash;

    protected override void OnStartup(StartupEventArgs e)
    {
        string installRoot = AppPaths.ResolveInstallRoot();
        Log.Initialize(Path.Combine(installRoot, "logs"));
        Log.Info($"Lx Launcher {typeof(App).Assembly.GetName().Version} 启动；安装目录 {installRoot}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"Lx Launcher 退出，退出码 {e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("界面线程未处理异常", e.Exception);
        ShowCrash(e.Exception, "启动器还能继续操作，但状态可能已经不对，建议重启。");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Error($"未处理异常（进程{(e.IsTerminating ? "即将退出" : "继续运行")}）", exception);
        if (exception is not null)
        {
            ShowCrash(exception, e.IsTerminating ? "进程即将退出，请重新启动。" : "进程会继续运行。");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("未观察的后台任务异常", e.Exception);
        e.SetObserved();
    }

    // 弹窗只做三件事：说人话、给日志路径、能一键打开目录。弹窗里绝不能再抛异常。
    private static void ShowCrash(Exception exception, string hint)
    {
        if (_showingCrash)
        {
            return;
        }

        _showingCrash = true;
        try
        {
            string file = Log.CurrentFile ?? Log.DirectoryPath;
            string text = "启动器遇到错误：" + exception.Message
                + Environment.NewLine + Environment.NewLine + hint
                + Environment.NewLine + Environment.NewLine + "日志：" + file
                + Environment.NewLine + Environment.NewLine + "是否打开日志文件夹？";

            MessageBoxResult result = MessageBox.Show(
                text,
                "Lx Launcher",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                OpenLogFolder();
            }
        }
        catch
        {
            // 崩溃处理本身失败时不再级联
        }
        finally
        {
            _showingCrash = false;
        }
    }

    private static void OpenLogFolder()
    {
        try
        {
            ShellHelper.OpenFolder(Log.DirectoryPath);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Log.Warn($"打开日志目录失败：{ex.Message}");
        }
    }
}
