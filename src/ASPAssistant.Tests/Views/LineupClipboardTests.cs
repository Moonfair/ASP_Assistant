using System.Runtime.InteropServices;
using ASPAssistant.App.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public class LineupClipboardTests
{
    [Fact]
    public async Task Copy_RetriesClipboardContentionAfterYielding()
    {
        var calls = 0;
        var task = LineupClipboard.RunWithRetryAsync(() =>
        {
            if (++calls == 1) throw new ExternalException("Busy clipboard", LineupClipboard.CannotOpenClipboard);
        });
        task.IsCompleted.Should().BeFalse();
        await task;
        calls.Should().Be(2);
    }
}
