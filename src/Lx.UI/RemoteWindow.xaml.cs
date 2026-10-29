using System.Windows;
using System.Windows.Controls;
using Lx.Probe.Remote;

namespace Lx.UI;

public partial class RemoteWindow : Window
{
    private readonly RemoteControlServer _server;

    public RemoteWindow(RemoteControlServer server)
    {
        _server = server;
        InitializeComponent();
        Loaded += async (_, _) => await StartServerAsync();
        Closed += async (_, _) => await _server.DisposeAsync();
    }

    private async Task StartServerAsync()
    {
        try
        {
            await _server.StartAsync();
            StatusText.Text = "服务运行中";
            UrlBox.Text = _server.Url;
            TokenBox.Text = _server.Token;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "启动失败";
            UrlBox.Text = ex.Message;
            TokenBox.Text = string.Empty;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await StartServerAsync();

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await _server.StopAsync();
        StatusText.Text = "服务已停止";
        UrlBox.Text = string.Empty;
        TokenBox.Text = string.Empty;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e) => Copy(UrlBox.Text);

    private void CopyToken_Click(object sender, RoutedEventArgs e) => Copy(TokenBox.Text);

    private static void Copy(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板被其他程序占用时忽略
        }
    }
}
