using System.Windows;
using System.Windows.Markup;
using Lx.Probe.Localization;

namespace Lx.UI;

// XAML 里写 Content="{ui:T Nav_Home}"；切换语言时把值重新推给控件，不用重建窗口。
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        string value = L10n.T(Key);

        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
            target.TargetObject is DependencyObject dependencyObject &&
            target.TargetProperty is DependencyProperty property)
        {
            void Handler()
            {
                if (dependencyObject.Dispatcher.CheckAccess())
                {
                    dependencyObject.SetValue(property, L10n.T(Key));
                }
                else
                {
                    dependencyObject.Dispatcher.BeginInvoke(new Action(() => dependencyObject.SetValue(property, L10n.T(Key))));
                }
            }

            L10n.Changed += Handler;
        }

        return value;
    }
}
