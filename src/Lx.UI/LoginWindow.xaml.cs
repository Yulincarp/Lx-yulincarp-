using System.IO;
using System.Net.Http;
using System.Windows;
using Lx.Probe.Auth;
using Lx.Probe.Model;

namespace Lx.UI;

public partial class LoginWindow : Window
{
    private readonly string _installRoot;

    public Account? Result { get; private set; }

    public LoginWindow(string installRoot)
    {
        InitializeComponent();
        _installRoot = installRoot;
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        string api = ApiBox.Text.Trim();
        string email = EmailBox.Text.Trim();
        string password = PasswordBox.Password;

        if (api.Length == 0 || email.Length == 0 || password.Length == 0)
        {
            StatusText.Text = "三样都要填";
            return;
        }

        LoginButton.IsEnabled = false;
        StatusText.Text = "登录中…";
        try
        {
            Account account = await YggdrasilAuth.LoginAsync(api, email, password);
            AccountStore.Save(_installRoot, account);

            Result = account;
            DialogResult = true;
            Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
