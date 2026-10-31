using Lx.Probe.Localization;

namespace Lx.Tests;

public class L10nTests
{
    [Fact]
    public void DefaultLanguage_IsChinese()
    {
        L10n.SetLanguage(null);

        Assert.Equal("zh-CN", L10n.Language);
        Assert.Equal("首页", L10n.T("S181"));
        Assert.Equal("启动游戏", L10n.T("S172"));
    }

    [Fact]
    public void English_TranslatesKnownKeys()
    {
        L10n.SetLanguage("en");

        Assert.Equal("Home", L10n.T("S181"));
        Assert.Equal("Launch", L10n.T("S172"));
        Assert.Equal("Copy failures ({0})", L10n.T("S166"));
    }

    [Fact]
    public void UnknownKey_ReturnsKeyItself()
    {
        L10n.SetLanguage("en");

        Assert.Equal("S999", L10n.T("S999"));
    }

    [Fact]
    public void Normalize_UnknownFallsBackToChinese()
    {
        Assert.Equal("zh-CN", L10n.Normalize(null));
        Assert.Equal("zh-CN", L10n.Normalize("fr"));
        Assert.Equal("en", L10n.Normalize("EN"));
        Assert.Equal("zh-CN", L10n.Normalize("zh-CN"));
    }

    [Fact]
    public void SetLanguage_RaisesChanged()
    {
        int raised = 0;
        void Handler() => raised++;

        L10n.Changed += Handler;
        try
        {
            L10n.SetLanguage("en");
            L10n.SetLanguage("en");
        }
        finally
        {
            L10n.Changed -= Handler;
            L10n.SetLanguage("zh-CN");
        }

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Format_SubstitutesArguments()
    {
        L10n.SetLanguage("zh-CN");

        Assert.Equal("复制失败清单 (3)", L10n.T("S166", 3));
    }

    [Fact]
    public void EveryUsedKey_Exists()
    {
        // XAML 里引用的 id 必须都在词条表里，避免出现 {ui:T S999} 这种空文案
        string uiDir = RepoPath("src", "Lx.UI");
        if (uiDir.Length == 0)
        {
            return;
        }

        var missing = new List<string>();
        foreach (string file in Directory.EnumerateFiles(uiDir, "*.xaml"))
        {
            string text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(text, @"\{ui:T (S\d{3})\}"))
            {
                string key = match.Groups[1].Value;
                if (!L10n.Has(key))
                {
                    missing.Add($"{Path.GetFileName(file)}:{key}");
                }
            }
        }

        Assert.True(missing.Count == 0, "缺少词条：" + string.Join(", ", missing));
    }

    [Fact]
    public void EveryEntry_HasBothLanguages()
    {
        string uiDir = RepoPath("src", "Lx.Core", "Localization");
        if (uiDir.Length == 0)
        {
            return;
        }

        string json = File.ReadAllText(Path.Combine(uiDir, "strings.json"));
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);

        foreach (System.Text.Json.JsonElement entry in document.RootElement.EnumerateObject().Select(p => p.Value))
        {
            string zh = entry.GetProperty("zh").GetString() ?? string.Empty;
            string en = entry.GetProperty("en").GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(zh));
            Assert.False(string.IsNullOrWhiteSpace(en));
        }
    }

    private static string RepoPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && directory is not null; i++)
        {
            string candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }
}
