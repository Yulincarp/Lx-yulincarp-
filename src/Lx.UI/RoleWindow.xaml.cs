using System.Windows;
using Lx.Probe.Model;

namespace Lx.UI;

public partial class RoleWindow : Window
{
    public string? SelectedRole { get; private set; }

    public RoleWindow(Account account)
    {
        InitializeComponent();

        List<AccountRole> roles = account.AvailableRoles ?? new List<AccountRole>();
        RoleList.ItemsSource = roles;
        RoleList.SelectedItem = roles.FirstOrDefault(role =>
            string.Equals(role.Name, account.Name, StringComparison.OrdinalIgnoreCase)) ?? roles.FirstOrDefault();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (RoleList.SelectedItem is not AccountRole role)
        {
            return;
        }

        SelectedRole = role.Name;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
