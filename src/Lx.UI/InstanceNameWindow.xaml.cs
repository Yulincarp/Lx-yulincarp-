using System.Windows;

namespace Lx.UI;

// 一个输入框的小对话框：改名、复制实例都用它，省得为每个动作各写一个窗口。
public partial class InstanceNameWindow : Window
{
    public string Value { get; private set; } = string.Empty;

    public InstanceNameWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        NameBox.Text = initial;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string value = NameBox.Text.Trim();
        if (value.Length == 0)
        {
            return;
        }

        Value = value;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
