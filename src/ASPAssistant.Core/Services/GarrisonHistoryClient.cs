using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ASPAssistant.Core.Services;

public sealed class GarrisonHistoryClient
{
    private static readonly HttpClient SharedHttp = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = 8 * 1024 * 1024
    };

    private static readonly Uri GrantExchangeUri = new("https://zonai.skland.com/web/v1/user/auth/generate_cred_by_code");
    private static readonly Uri RoleBindingsUri = new("https://zonai.skland.com/api/v1/game/player/binding");
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    public GarrisonHistoryClient() : this(SharedHttp, TimeProvider.System) { }

    internal GarrisonHistoryClient(HttpClient http, TimeProvider clock) => (_http, _clock) = (http, clock);

    public async Task<GarrisonSession> AuthorizeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new GarrisonAuthorizationException("请先在鹰角官网登录。");
        using var request = NewRequest(HttpMethod.Post, GrantExchangeUri);
        request.Content = JsonContent.Create(new { kind = 1, code });
        using var response = await SendAsync(request, cancellationToken);
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var access = await GarrisonHistory.ReadDataAsync<GrantResponse>(responseStream, cancellationToken);
        if (!ValidHeader(access.Cred) || string.IsNullOrWhiteSpace(access.Token))
            throw new GarrisonAuthorizationException("未取得战绩授权，请重新登录鹰角官网后再试。");

        using var bindingRequest = SignedRequest(RoleBindingsUri, access.Cred!, access.Token!);
        using var bindingResponse = await SendAsync(bindingRequest, cancellationToken);
        using var bindingStream = await bindingResponse.Content.ReadAsStreamAsync(cancellationToken);
        var data = await GarrisonHistory.ReadDataAsync<BindingResponse>(bindingStream, cancellationToken);
        var roles = (data.List ?? [])
            .Where(game => game?.AppCode == "arknights")
            .SelectMany(game => game!.BindingList ?? [])
            .OfType<RoleBinding>()
            .Select(binding => new GarrisonRole(ReadUid(binding.Uid), binding.NickName ?? "", binding.GameId ?? 1, binding.IsDefault))
            .Where(role => role.Uid.Length > 0 && role.Uid.All(char.IsAsciiDigit) && role.GameId > 0)
            .OrderByDescending(role => role.IsDefault)
            .DistinctBy(role => role.Header)
            .ToArray();
        return new GarrisonSession(access.Cred!, access.Token!, Array.AsReadOnly(roles));
    }

    public async Task<GarrisonHistoryResult> FetchAsync(GarrisonSession session, GarrisonRole role, CancellationToken cancellationToken)
    {
        if (!session.Roles.Contains(role))
            throw new GarrisonAuthorizationException("请选择此鹰角通行证绑定的角色。");

        var catalog = await ReadCatalogAsync(null, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(catalog.SeasonId))
            throw new FormatException("战绩服务没有返回当前赛季，请稍后再试。");
        using var request = SignedRequest(GarrisonHistory.HistoryUri(catalog.SeasonId), session.Credential, session.SigningKey);
        request.Headers.Add("sk-game-role", role.Header);
        using var response = await SendAsync(request, cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new GarrisonHistoryResult(await GarrisonHistory.ReadRecordsAsync(stream, cancellationToken), catalog);
    }

    public async Task<GarrisonCatalog> ReadCatalogAsync(string? season, string? version, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GarrisonHistory.CatalogUri(season, version));
        using var response = await SendAsync(request, cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await GarrisonHistory.ReadCatalogAsync(stream, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return response;

        var status = response.StatusCode;
        response.Dispose();
        if (status == HttpStatusCode.Unauthorized)
            throw new GarrisonAuthorizationException("战绩授权已失效，请重新授权。");
        if (status == HttpStatusCode.Forbidden)
            throw new GarrisonAuthorizationException("战绩服务拒绝了授权请求，请检查鹰角登录状态或稍后重试。");
        throw new HttpRequestException("战绩服务请求失败。", null, status);
    }

    private HttpRequestMessage NewRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("platform", "3");
        request.Headers.Add("vName", "1.0.0");
        request.Headers.Add("timestamp", _clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return request;
    }

    private HttpRequestMessage SignedRequest(Uri uri, string credential, string signingKey)
    {
        var request = NewRequest(HttpMethod.Get, uri);
        var timestamp = request.Headers.GetValues("timestamp").Single();
        var headers = JsonSerializer.Serialize(new { platform = "3", timestamp, dId = "", vName = "1.0.0" });
        var message = uri.AbsolutePath + uri.Query.TrimStart('?') + timestamp + headers;
        // The service expects MD5 of the hex-encoded HMAC for its request signature.
        var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(message));
        var signature = MD5.HashData(Encoding.ASCII.GetBytes(Convert.ToHexString(hmac).ToLowerInvariant()));
        request.Headers.Add("cred", credential);
        request.Headers.Add("sign", Convert.ToHexString(signature).ToLowerInvariant());
        return request;
    }

    private static bool ValidHeader(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 && !value.Any(char.IsControl);

    private static string ReadUid(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        _ => ""
    };

    private sealed record GrantResponse(string? Cred, string? Token);
    private sealed record BindingResponse(List<GameBinding?>? List);
    private sealed record GameBinding(string? AppCode, List<RoleBinding?>? BindingList);
    private sealed record RoleBinding(JsonElement Uid, string? NickName, int? GameId, bool IsDefault);
}

public sealed class GarrisonSession
{
    internal GarrisonSession(string credential, string signingKey, IReadOnlyList<GarrisonRole> roles)
        => (Credential, SigningKey, Roles) = (credential, signingKey, roles);

    internal string Credential { get; }
    internal string SigningKey { get; }
    public IReadOnlyList<GarrisonRole> Roles { get; }
}

public sealed record GarrisonRole(string Uid, string Name, int GameId, bool IsDefault)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Uid : $"{Name} · {Uid}";
    internal string Header => $"{GameId}_{Uid}_0";
}

public sealed record GarrisonHistoryResult(IReadOnlyList<GarrisonRecord> Records, GarrisonCatalog Catalog);

public sealed class GarrisonAuthorizationException(string message) : Exception(message);
