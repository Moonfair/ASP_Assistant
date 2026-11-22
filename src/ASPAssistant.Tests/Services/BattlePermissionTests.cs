using ASPAssistant.App.Services;
using System.IO.Pipes;

namespace ASPAssistant.Tests.Services;

public class BattlePermissionTests
{
    [Fact]
    public async Task ClosingParentPipeCancelsIdleWorkerWithoutAnotherSample()
    {
        var name = $"asp-test-{Guid.NewGuid():N}";
        using var parent = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var child = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connected = parent.WaitForConnectionAsync();
        await child.ConnectAsync(3000);
        await connected;
        CancellationToken workerToken = default;
        var worker = BattleDataSession.RunUntilParentClosesAsync(child, token =>
        {
            workerToken = token;
            return Task.Delay(Timeout.Infinite, token);
        });
        parent.Dispose();
        await worker.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(workerToken.IsCancellationRequested);
    }

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
