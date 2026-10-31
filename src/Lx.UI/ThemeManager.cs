using System.IO;
using System.Windows;
using Lx.Probe.Model;
using Microsoft.Win32;

namespace Lx.UI;

// 主题切换：light / dark / system。
// 设置里存"模式"，实际加载哪套皮肤由 ThemeChoice.Resolve 决定（system 时读注册表）。
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static string Current { get; private set; } = "light";

    // 设置里保存的模式（light / dark / system）
    public static string Mode { get; private set; } = "light";

    public static void Apply(string? theme)
    {
        Mode = ThemeChoice.Normalize(theme);
        string effective = ThemeChoice.Resolve(Mode, SystemPrefersDark());

        Application? app = Application.Current;
        if (app is null)
        {
            Current = effective;
            return;
        }

        ResourceDictionary? old = app.Resources.MergedDictionaries
            .FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("/Themes/", StringComparison.OrdinalIgnoreCase) == true);

        // 已经是这套皮肤就不重建，避免切换时闪一下
        if (old is not null &&
            old.Source?.OriginalString.EndsWith($"/Themes/{effective}.xaml", StringComparison.OrdinalIgnoreCase) == true)
        {
            Current = effective;
            return;
        }

        var next = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Lx.UI;component/Themes/{effective}.xaml", UriKind.Absolute),
        };

        if (old is not null)
        {
            app.Resources.MergedDictionaries.Remove(old);
        }

        app.Resources.MergedDictionaries.Add(next);
        Current = effective;
    }

    // 系统"应用模式"是不是深色；读不到就当浅色。
    public static bool SystemPrefersDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int raw && raw == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
