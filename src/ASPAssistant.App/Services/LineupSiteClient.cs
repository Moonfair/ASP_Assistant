using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ASPAssistant.Core;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace ASPAssistant.App.Services;

internal sealed partial class LineupSiteClient : IDisposable
{
    private static readonly Uri Site = new("https://aspa.moonfair.cn/");
    private readonly HttpClient _http;
    private readonly string _sessionPath;
    private readonly string _logDirectory;
    private Session? _session;

    internal LineupSiteClient(string settingsDirectory, HttpMessageHandler? handler = null)
    {
        _sessionPath = Path.Combine(settingsDirectory, "lineup-site-session.dat");
        _logDirectory = Path.Combine(settingsDirectory, "logs");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = Site,
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    internal string? Email => _session is { } session && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ? session.Email : null;

    internal async Task RestoreSessionAsync()
    {
        if (!File.Exists(_sessionPath)) return;
        try
        {
            var encrypted = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(_sessionPath));
            var decrypted = await new DataProtectionProvider().UnprotectAsync(encrypted);
            CryptographicBuffer.CopyToByteArray(decrypted, out var bytes);
            _session = JsonSerializer.Deserialize<Session>(bytes);
            if (Email is null) await StoreSessionAsync(null);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LineupSite", $"Could not restore saved sign-in: {ex.GetType().Name}");
        }
    }

    internal Task<Challenge> GetChallengeAsync(CancellationToken cancellationToken) =>
        PostAsync<Challenge>("api/captcha", new { action = "login" }, cancellationToken);

    internal async Task<string> SendCodeAsync(string email, string challengeId, string answer, CancellationToken cancellationToken)
    {
        var result = await PostAsync<Identifier>("api/auth/code",
            new { email, captcha = new { id = challengeId, answer } }, cancellationToken);
        return result.Id;
    }

    internal async Task SignInAsync(string id, string code, CancellationToken cancellationToken)
    {
        var result = await PostAsync<SignedIn>("api/auth/desktop/verify", new { id, code }, cancellationToken);
        if (result.User is null || string.IsNullOrWhiteSpace(result.User.Email) || result.Token is not { Length: 64 } ||
            result.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new LineupSiteException("阵容库返回的登录信息无效，请稍后重试。");

        cancellationToken.ThrowIfCancellationRequested();
        await StoreSessionAsync(new(result.User.Email, result.Token, result.ExpiresAt));
    }

    internal async Task SignOutAsync(CancellationToken cancellationToken)
    {
        if (_session is { } session)
            await PostAsync<JsonElement>("api/auth/desktop/logout", new { }, cancellationToken, session.Token);
        await StoreSessionAsync(null);
    }

    internal async Task<Uri> UploadAsync(Guid localId, string share)
    {
        if (Email is null || _session is not { } session)
            throw new LineupSiteException("请先登录卫戍阵容库。");

        var result = await PostAsync<Identifier>("api/desktop/lineups", new { localId, share },
            CancellationToken.None, session.Token);
        if (!Guid.TryParseExact(result.Id, "D", out var id))
            throw new LineupSiteException("阵容库返回的阵容链接无效，请稍后重试。");
        return new Uri(Site, $"?lineup={id:D}");
    }

    private Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken, string? token = null) =>
        SendAsync<T>(HttpMethod.Post, path, body, cancellationToken, token);

    private Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, string? token = null) =>
        SendContentAsync<T>(method, path, body is null ? null : JsonContent.Create(body), cancellationToken, token);

    private async Task<T> SendContentAsync<T>(HttpMethod method, string path, HttpContent? content,
        CancellationToken cancellationToken, string? token = null, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                if (token is not null && response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (_session?.Token == token) await StoreSessionAsync(null);
                    throw new LineupSiteException("登录已失效，请重新登录卫戍阵容库。");
                }
                var error = await response.Content.ReadFromJsonAsync<ApiError>(deadline.Token);
                throw new LineupSiteException(error?.Error ?? "阵容库暂时无法处理请求，请稍后重试。");
            }
            return await response.Content.ReadFromJsonAsync<T>(deadline.Token)
                ?? throw new LineupSiteException("阵容库响应为空，请稍后重试。");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException ||
                                    ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new LineupSiteException("暂时无法连接卫戍阵容库，请检查网络后重试。");
        }
    }

    private async Task StoreSessionAsync(Session? session)
    {
        _session = session;
        lock (_voteCacheLock) _voteStatus.Clear();
        var temporary = _sessionPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            if (session is null)
            {
                File.Delete(_sessionPath);
                return;
            }

            var buffer = CryptographicBuffer.CreateFromByteArray(JsonSerializer.SerializeToUtf8Bytes(session));
            var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(buffer);
            CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, _sessionPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LineupSite", $"Could not update saved sign-in: {ex.GetType().Name}");
            try { File.Delete(temporary); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose() => _http.Dispose();

    internal sealed record Challenge(string Id, string Image);
    private sealed record Identifier(string Id);
    private sealed record ApiError(string Error);
    private sealed record User(string Email);
    private sealed record SignedIn(User User, string Token, long ExpiresAt);
    private sealed record Session(string Email, string Token, long ExpiresAt);
}

internal sealed class LineupSiteException(string message) : Exception(message);
