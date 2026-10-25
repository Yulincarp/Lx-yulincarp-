using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Lx.Probe;
using Lx.Probe.Diagnostics;
using Lx.Probe.Model;

namespace Lx.UI;

public partial class App : Application
{
    private const string InstanceName = @"Local\Lx.Launcher.SingleInstance";

    private static bool _showingCrash;
    private SingleInstanceGuard? _instanceGuard;
    private Thread? _activationThread;
    private volatile bool _shuttingDown;
    private int _activationRetries;

    protected override void OnStartup(StartupEventArgs e)
    {
        string installRoot = AppPaths.ResolveInstallRoot();

        _instanceGuard = new SingleInstanceGuard(InstanceName);
        if (!_instanceGuard.IsFirstInstance)
        {
            bool signaled = _instanceGuard.SignalExisting();
            _instanceGuard.Dispose();
            _instanceGuard = null;

            if (!signaled)
            {
                MessageBox.Show("Lx Launcher 已经在运行。", "Lx Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // 第二个实例没有状态要保存；窗口由第一个实例负责唤醒。
            Environment.Exit(0);
            return;
        }

        Log.Initialize(Path.Combine(installRoot, "logs"));
        Log.Info($"Lx Launcher {AppInfo.Version} 启动；数据目录 {installRoot}（{(AppPaths.IsPortable ? "便携模式" : "标准模式")}）");
        ThemeManager.Apply(SettingsStore.Load(installRoot).Theme);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _activationThread = new Thread(ActivationLoop)
        {
            IsBackground = true,
            Name = "Lx.Activation",
        };
        _activationThread.Start();

        base.OnStartup(e);

        // 不再用 App.xaml 的 StartupUri：第二个实例要在创建窗口之前就退出。
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        if (_instanceGuard is not null)
        {
            try
            {
                _instanceGuard.Dispose();
            }
            catch (Exception)
            {
                // 退出阶段不再让清理异常影响进程
            }

            _instanceGuard = null;
            Log.Info($"Lx Launcher 退出，退出码 {e.ApplicationExitCode}");
        }

        base.OnExit(e);
    }

    // 后台等"第二个实例"发来的激活通知，再回到 UI 线程操作窗口。
    private void ActivationLoop()
    {
        while (!_shuttingDown && _instanceGuard is not null)
        {
            if (!_instanceGuard.WaitForActivation(TimeSpan.FromSeconds(1)))
            {
                continue;
            }

            Dispatcher.BeginInvoke(new Action(BringToFront), DispatcherPriority.Normal);
        }
    }

    private void BringToFront()
    {
        if (MainWindow is not Window window)
        {
            // 第二个实例来得太早，窗口还没创建：稍后再试，最多 10 次。
            if (_activationRetries++ < 10)
            {
                Dispatcher.BeginInvoke(new Action(BringToFront), DispatcherPriority.ApplicationIdle);
            }

            return;
        }

        _activationRetries = 0;

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
        Log.Info("收到第二个实例的激活请求，窗口已前置");
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
