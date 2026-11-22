using ASPAssistant.App.Services;

namespace ASPAssistant.Tests.Services;

public class BattlePermissionTests
{
    [Theory]
    [InlineData(0, true, false, false, false, true)]
    [InlineData(45, true, false, true, true, true)]
    [InlineData(45, true, false, false, true, false)]
    [InlineData(0, false, false, true, true, false)]
    [InlineData(45, true, true, true, true, false)]
    public async Task StartupUsesExistingAccessOrRequestsItOnce(int pid, bool mayPrompt, bool elevated,
        bool denied, bool expectProbe, bool expectPrompt)
    {
        var attempts = 0;
        var prompts = 0;
        await BattleDataSession.ConnectWithPermissionAsync((uint)pid, mayPrompt, elevated,
            stopOnDenied =>
            {
                Assert.Equal(mayPrompt && !elevated, stopOnDenied);
                attempts++;
                return Task.FromResult(denied);
            },
            () => { prompts++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(expectProbe ? 1 : 0, attempts);
        Assert.Equal(expectPrompt ? 1 : 0, prompts);
    }
}
