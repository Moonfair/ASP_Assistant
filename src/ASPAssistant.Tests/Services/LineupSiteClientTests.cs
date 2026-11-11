using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using ASPAssistant.App.Services;

namespace ASPAssistant.Tests.Services;

public class LineupSiteClientTests
{
    [Fact]
    public async Task SavedSignInCanPublishAfterRestartAndIsRemovedAfterRevocation()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"ASPAssistant.SiteTests.{Guid.NewGuid():N}");
        var token = new string('a', 64);
        const string publishedId = "37b099dc-0ed2-49e2-8494-45994a57a7ab";
        var localId = Guid.NewGuid();
        const string share = "ASPA-LINEUP/2\n测试";
        var handler = new SiteHandler(async (request, ct) =>
        {
            Assert.Equal("/api/auth/desktop/verify", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            var json = await request.Content!.ReadAsStringAsync(ct);
            Assert.Contains("123456", json);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    user = new { email = "player@example.test" },
                    token,
                    expiresAt = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds()
                })
            };
        });

        try
        {
            using (var client = new LineupSiteClient(settings, handler))
            {
                await client.SignInAsync("email-request-id", "123456", CancellationToken.None);
                Assert.Equal("player@example.test", client.Email);
            }

            var stored = await File.ReadAllBytesAsync(Path.Combine(settings, "lineup-site-session.dat"));
            Assert.DoesNotContain(token, Encoding.UTF8.GetString(stored));

            var revoked = false;
            using var restored = new LineupSiteClient(settings, new SiteHandler(async (request, ct) =>
            {
                Assert.Equal("/api/desktop/lineups", request.RequestUri!.AbsolutePath);
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal(token, request.Headers.Authorization.Parameter);
                var body = await request.Content!.ReadFromJsonAsync<Upload>(ct);
                Assert.Equal(new Upload(localId, share), body);
                return new(revoked ? HttpStatusCode.Unauthorized : HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new { id = publishedId })
                };
            }));

            await restored.RestoreSessionAsync();
            Assert.Equal("player@example.test", restored.Email);
            Assert.Equal($"https://aspa.moonfair.cn/?lineup={publishedId}", (await restored.UploadAsync(localId, share)).AbsoluteUri);

            revoked = true;
            await Assert.ThrowsAsync<LineupSiteException>(() => restored.UploadAsync(localId, share));
            Assert.Null(restored.Email);
            Assert.Empty(Directory.GetFiles(settings));
        }
        finally
        {
            if (Directory.Exists(settings)) Directory.Delete(settings, recursive: true);
        }
    }

    private sealed record Upload(Guid LocalId, string Share);

    private sealed class SiteHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
