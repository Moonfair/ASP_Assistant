using System.Net.Http;
using System.Net.Http.Json;

namespace ASPAssistant.App.Services;

internal sealed partial class LineupSiteClient
{
    internal Task<MatchSnapshot> MatchAsync(string action, string ticket, object body, CancellationToken ct) =>
        SendContentAsync<MatchSnapshot>(HttpMethod.Post, $"api/desktop/match/live/{action}", JsonContent.Create(body),
            ct, ticket, TimeSpan.FromSeconds(4));
}

internal sealed record MatchPoint(double X, double Y);
internal sealed record MatchStroke(string Id, string PlayerId, MatchPoint[] Points, long ExpiresAt);
internal sealed record MatchMember(string Id, string Nickname, string Strategy, string Covenant, string Tactic,
    string AssignedTactic, string Color, bool Confirmed, bool Ready, bool HasPlugin = true);

internal sealed record MatchSnapshot
{
    public string Phase { get; init; } = "";
    public string SelfId { get; init; } = "";
    public string? RoomId { get; init; }
    public string? GameRoomId { get; init; }
    public bool Recruiting { get; init; }
    public long ServerNow { get; init; }
    public string? CaptainId { get; init; }
    public bool CanForceStart { get; init; }
    public string? Invitation { get; init; }
    public string? Message { get; init; }
    public MatchMember[] Members { get; init; } = [];
    public string[] Muted { get; init; } = [];
    public MatchStroke[] Strokes { get; init; } = [];
}
