using ASPAssistant.Core.Services;

if (args.Length != 2 || !int.TryParse(args[0], out int instance) || !int.TryParse(args[1], out int seconds) || seconds is < 1 or > 600)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/mumu-battle/smoke -- <MuMu instance index> <seconds:1..600>");
    return 1;
}
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
var targets = (await new EmulatorDiscovery().FindAsync(stop.Token))
    .Where(t => t.Kind == GameTargetKind.MuMu && t.Instance == instance).ToList();
if (targets.Count != 1) { Console.Error.WriteLine("Expected one running MuMu instance with that index."); return 1; }
var target = targets[0];
Console.WriteLine($"MuMu {target.Instance} {target.Serial}");
int connected = 0, live = 0;
try
{
    await MuMuBattleFeed.ConnectAsync(target, update =>
    {
        if (update.State is BattleFeedState.Live or BattleFeedState.WaitingForRound) connected++;
        if (update.Sample is {} sample)
        {
            live++;
            var stats = new BattleStatistics();
            stats.Update(sample);
            Console.WriteLine($"{update.State} round={sample.Round} frame={sample.Frame} gameSeconds={sample.GameSeconds:F2} ended={sample.Ended} chars={stats.Rows.Count} damage={stats.TotalDamage}");
        }
        else Console.WriteLine(update.State);
        return Task.CompletedTask;
    }, stop.Token);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
Console.WriteLine($"connectedSamples={connected} liveFrames={live}");
return live > 0 ? 0 : connected > 0 ? 2 : 3;
