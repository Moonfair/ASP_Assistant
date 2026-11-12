using System.Net.Http;

namespace ASPAssistant.App.Services;

internal sealed partial class LineupSiteClient
{
    private readonly object _voteCacheLock = new();
    private readonly Dictionary<string, (DateTimeOffset Expiry, Task<OperatorVoteResults> Request)> _voteResults = [];
    private readonly Dictionary<(string Path, string Token), (DateTimeOffset Expiry, Task<OperatorVoteStatus> Request)> _voteStatus = [];

    internal Task<OperatorVoteResults> GetOperatorVotesAsync(string season, string name)
    {
        var path = VotePath(season, name);
        return CacheVoteRead(_voteResults, path,
            () => SendAsync<OperatorVoteResults>(HttpMethod.Get, path, null, CancellationToken.None));
    }

    internal Task<OperatorVoteStatus> GetOperatorVoteStatusAsync(string season, string name)
    {
        var token = VoteToken();
        var path = VotePath(season, name, mine: true);
        return CacheVoteRead(_voteStatus, (path, token),
            () => SendAsync<OperatorVoteStatus>(HttpMethod.Get, path, null, CancellationToken.None, token));
    }

    internal Task VoteForSkillAsync(string season, string name, int skill, CancellationToken cancellationToken) =>
        SubmitOperatorVoteAsync(season, name, new { skill }, cancellationToken);

    internal Task VoteForEquipmentAsync(string season, string name, string[] equipment, CancellationToken cancellationToken) =>
        SubmitOperatorVoteAsync(season, name, new { equipment }, cancellationToken);

    private async Task SubmitOperatorVoteAsync(string season, string name, object vote, CancellationToken cancellationToken)
    {
        var token = VoteToken();
        try
        {
            var result = await PostAsync<VoteReceipt>(VotePath(season, name), vote, cancellationToken, token);
            if (!result.Recorded) throw new LineupSiteException("作业站未确认收到投票，请重试。");
        }
        finally
        {
            // A timeout can arrive after the server committed the ballot.
            lock (_voteCacheLock) _voteStatus.Remove((VotePath(season, name, mine: true), token));
        }
    }

    private Task<T> CacheVoteRead<TKey, T>(Dictionary<TKey, (DateTimeOffset Expiry, Task<T> Request)> cache,
        TKey key, Func<Task<T>> fetch) where TKey : notnull
    {
        lock (_voteCacheLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (cache.TryGetValue(key, out var entry) && (!entry.Request.IsCompleted ||
                entry.Expiry > now && entry.Request.IsCompletedSuccessfully))
                return entry.Request;

            foreach (var expired in cache.Where(pair => pair.Value.Expiry <= now && pair.Value.Request.IsCompleted).ToArray())
                cache.Remove(expired.Key);
            var request = fetch();
            cache[key] = (now.AddSeconds(45), request);
            return request;
        }
    }

    private string VoteToken() => Email is not null && _session is { } session
        ? session.Token : throw new LineupSiteException("请先登录卫戍阵容库。");

    private static string VotePath(string season, string name, bool mine = false) =>
        $"api/operator-votes{(mine ? "/mine" : "")}?season={Uri.EscapeDataString(season)}&operator={Uri.EscapeDataString(name)}";

    private sealed record VoteReceipt(bool Recorded);
    internal sealed record OperatorVoteStatus(bool SkillVoted, bool EquipmentVoted);
    internal sealed record OperatorVoteResults(SkillVotes[] Skills, EquipmentVotes[] Equipment);
    internal sealed record SkillVotes(int Choice, int Votes);
    internal sealed record EquipmentVotes(string Choice, int Votes);
}
