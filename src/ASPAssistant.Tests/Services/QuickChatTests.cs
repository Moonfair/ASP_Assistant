using ASPAssistant.App.Services;
using ASPAssistant.Core.Data;

namespace ASPAssistant.Tests.Services;

public class QuickChatTests
{
    [Fact]
    public void SelectionUsesOnlyASharedTemplateAndACatalogWord()
    {
        Assert.Contains("当心□！", QuickChatCatalog.Templates);
        var phrase = new QuickPhrase("当心□！", "浊心斯卡蒂");
        Assert.True(QuickChatCatalog.IsValid(phrase));
        Assert.Equal("当心浊心斯卡蒂！", phrase.Text);
        Assert.False(QuickChatCatalog.IsValid(phrase with { Word = "输入的聊天消息" }));
        Assert.False(QuickChatCatalog.IsValid(phrase with { Template = "未定义□" }));
    }

    [Fact]
    public void SearchMatchesOrderedFragmentsIgnoringCaseAndDoesNotLeakAcrossCategories()
    {
        Assert.Contains("浊心斯卡蒂", QuickChatCatalog.Search("干员", "浊蒂"));
        Assert.DoesNotContain("浊心斯卡蒂", QuickChatCatalog.Search("干员", "蒂浊"));
        Assert.Contains("Mon3tr", QuickChatCatalog.Search(null, " mOn3T "));
        Assert.DoesNotContain("Mon3tr", QuickChatCatalog.Search("装备", "mon3"));
        Assert.Empty(QuickChatCatalog.Search("未知分类", ""));
        var all = QuickChatCatalog.Search(null, "");
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Contains("当心□！", QuickChatCatalog.SearchTemplates(null, "当□"));
        var matchingGroup = QuickChatCatalog.TemplateGroups.First(p => p.Value.Contains("当心□！"));
        Assert.Contains("当心□！", QuickChatCatalog.SearchTemplates(matchingGroup.Key, "心！"));
        Assert.Empty(QuickChatCatalog.SearchTemplates("未知分类", "当"));
    }

    [Fact]
    public void RecentPhrasesMoveToFrontDeduplicateByRenderedTextAndEvictTheOldest()
    {
        QuickPhrase[] previous = [new("□！", "甲"), new("当心□！", "乙"), new("□！", "丙"), new("□！", "丁"), new("□！", "戊")];
        var recent = QuickChatCatalog.Remember(previous, new("□！", "当心乙"));
        Assert.Equal(["当心乙！", "甲！", "丙！", "丁！", "戊！"], recent.Select(p => p.Text));
        Assert.Equal("□！", recent[0].Template);
        Assert.Equal(["己！", "当心乙！", "甲！", "丙！", "丁！"],
            QuickChatCatalog.Remember(recent, new("□！", "己")).Select(p => p.Text));
    }

    [Fact]
    public void RecentShoutsSurviveRestartAndRejectBrokenStorage()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var settings = new SettingsManager(dir.FullName);
            Assert.Empty(settings.LoadRecentShouts());
            RecentShout[] sent = [new("当心□！", "浊心斯卡蒂"), new("当心□！", "Mon3tr")];
            settings.SaveRecentShouts(sent);
            Assert.Equal(sent, new SettingsManager(dir.FullName).LoadRecentShouts());
            File.WriteAllText(Path.Combine(dir.FullName, "recent-shouts.json"), "{");
            Assert.Empty(settings.LoadRecentShouts());
        }
        finally { dir.Delete(true); }
    }
}
