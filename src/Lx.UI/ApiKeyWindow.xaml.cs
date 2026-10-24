using System.Windows;

namespace Lx.UI;

public partial class ApiKeyWindow : Window
{
    public string? Key { get; private set; }

    public ApiKeyWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string key = KeyBox.Text.Trim();
        if (key.Length == 0)
        {
            return;
        }

        Key = key;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
