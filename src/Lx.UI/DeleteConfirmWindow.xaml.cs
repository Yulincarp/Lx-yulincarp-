using System.Windows;
using System.Windows.Controls;

namespace Lx.UI;

// 删实例文件前的最后一道确认：必须手敲实例名，"连文件删除"才可点。
// 只防手滑：真正执行时目标名仍然来自界面里选中的实例，不会用输入框里的字符串拼路径。
public partial class DeleteConfirmWindow : Window
{
    private readonly string _expectedName;

    public DeleteConfirmWindow(string instanceName, string directory)
    {
        InitializeComponent();
        _expectedName = instanceName;
        NameRun.Text = instanceName;
        PathText.Text = directory;
        NameBox.TextChanged += OnNameChanged;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) =>
        DeleteButton.IsEnabled = Matches();

    private bool Matches() =>
        string.Equals(NameBox.Text.Trim(), _expectedName.Trim(), StringComparison.OrdinalIgnoreCase);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!Matches())
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
