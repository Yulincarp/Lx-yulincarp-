using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Lx.Probe.Auth;
using Lx.Probe.Model;

namespace Lx.UI;

public partial class MicrosoftLoginWindow : Window
{
    private readonly string _installRoot;
    private string _verificationUri = "https://microsoft.com/devicelogin";

    public Account? Result { get; private set; }

    public MicrosoftLoginWindow(string installRoot)
    {
        InitializeComponent();
        _installRoot = installRoot;

        string remembered = Path.Combine(installRoot, "client-id.txt");
        if (File.Exists(remembered))
        {
            ClientIdBox.Text = File.ReadAllText(remembered).Trim();
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        string clientId = ClientIdBox.Text.Trim();
        if (clientId.Length == 0)
        {
            StatusText.Text = "先填 client id";
            return;
        }

        StartButton.IsEnabled = false;
        StatusText.Text = "等待授权…";
        try
        {
            Directory.CreateDirectory(_installRoot);
            File.WriteAllText(Path.Combine(_installRoot, "client-id.txt"), clientId);

            Account account = await MicrosoftAuth.LoginAsync(
                clientId,
                message => Dispatcher.Invoke(() =>
                {
                    OutputBox.AppendText(message + Environment.NewLine);
                    if (message.Contains("https://", StringComparison.Ordinal))
                    {
                        int start = message.IndexOf("https://", StringComparison.Ordinal);
                        _verificationUri = message[start..].Trim();
                    }
                }));

            AccountStore.Save(_installRoot, account);
            Result = account;
            DialogResult = true;
            Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            StatusText.Text = "失败";
            OutputBox.AppendText("[错误] " + ex.Message + Environment.NewLine);
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_verificationUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
