using ASPAssistant.App.Services;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

public sealed class BobVoiceCaptionTests
{
    [Theory]
    [InlineData(BobVoiceCue.Opening, "NewGame_03", "嘿，我最喜欢的指挥官回来了！准备来一局吗？")]
    [InlineData(BobVoiceCue.BeforeBattle, "Ahead_03", "这局我觉得你能赢。")]
    [InlineData(BobVoiceCue.BattleLoss, "CombatLoss_01", "没关系，人生难免不如意。")]
    [InlineData(BobVoiceCue.PreparationChatter, "Idle_02", "花钱要慎重。但……你也得花呀！")]
    [InlineData(BobVoiceCue.PreparationChatter, "Idle_03", "看你打牌是种享受。")]
    public void VoiceFilesCarryTheirOwnCaption(BobVoiceCue cue, string suffix, string caption)
    {
        var clip = Assert.Single(BobVoicePlayer.GetClips(cue), clip => clip.Suffix == suffix);
        Assert.Equal(caption, clip.Caption);
    }

    [Fact]
    public void EveryCueHasDistinctClipsWithoutHearthstoneTerminology()
    {
        var clips = Enum.GetValues<BobVoiceCue>().SelectMany(cue =>
        {
            var pool = BobVoicePlayer.GetClips(cue);
            Assert.NotEmpty(pool);
            return pool;
        }).ToArray();

        Assert.Equal(36, clips.Length);
        Assert.Equal(clips.Length, clips.Select(clip => clip.Suffix).Distinct().Count());
        string[] terms = ["随从", "死亡骑士", "酒馆", "三连", "达拉然", "探险者协会", "怪盗军团"];
        Assert.All(clips, clip => Assert.DoesNotContain(terms, clip.Caption.Contains));
    }

    [Fact]
    public void UnavailableAudioDoesNotEmitACaption()
    {
        using var player = new BobVoicePlayer(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var captions = new List<string>();
        player.Spoken += (caption, _) => captions.Add(caption);
        Assert.False(player.Play(BobVoiceCue.Opening, preview: true));
        Assert.Empty(captions);
    }
}
