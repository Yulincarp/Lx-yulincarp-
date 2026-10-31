using Lx.Probe.Localization;

namespace Lx.Tests;

public class L10nTests
{
    [Fact]
    public void DefaultLanguage_IsChinese()
    {
        L10n.SetLanguage(null);

        Assert.Equal("zh-CN", L10n.Language);
        Assert.Equal("首页", L10n.T("Nav_Home"));
        Assert.Equal("启动游戏", L10n.T("Btn_Launch"));
    }

    [Fact]
    public void English_TranslatesKnownKeys()
    {
        L10n.SetLanguage("en");

        Assert.Equal("Home", L10n.T("Nav_Home"));
        Assert.Equal("Launch", L10n.T("Btn_Launch"));
        Assert.Equal("Copy failures ({0})", L10n.T("Btn_CopyFailuresCount"));
    }

    [Fact]
    public void UnknownKey_ReturnsKeyItself()
    {
        L10n.SetLanguage("en");

        Assert.Equal("Nav_DoesNotExist", L10n.T("Nav_DoesNotExist"));
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

        Assert.Equal("复制失败清单 (3)", L10n.T("Btn_CopyFailuresCount", 3));
    }
}
