using System.Windows;

namespace Lx.UI;

// F6：只做应用级浅色/深色资源切换；复杂控件模板留给后续。
public static class ThemeManager
{
    public static string Current { get; private set; } = "light";

    public static void Apply(string? theme)
    {
        string normalized = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        Application? app = Application.Current;
        if (app is null)
        {
            Current = normalized;
            return;
        }

        ResourceDictionary? old = app.Resources.MergedDictionaries
            .FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("/Themes/", StringComparison.OrdinalIgnoreCase) == true);
        var next = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Lx.UI;component/Themes/{normalized}.xaml", UriKind.Absolute),
        };

        if (old is not null)
        {
            app.Resources.MergedDictionaries.Remove(old);
        }

        app.Resources.MergedDictionaries.Add(next);
        Current = normalized;
    }
}
