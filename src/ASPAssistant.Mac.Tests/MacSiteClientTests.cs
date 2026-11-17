using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using ASPAssistant.Mac;

namespace ASPAssistant.Mac.Tests;

public class MacSiteClientTests
{
    [Fact]
    public async Task ChallengeAndCodeUseExistingDesktopApi()
    {
        var requests = new List<string>();
        using var site = new LineupSiteClient(Path.GetTempPath(), new Handler(async (request, token) =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync(token);
            if (request.RequestUri.AbsolutePath.EndsWith("captcha"))
            {
                Assert.Contains("login", body);
                return new(HttpStatusCode.OK)
                { Content = JsonContent.Create(new { id = "challenge", image = "data:image/png;base64,AA==" }) };
            }
            Assert.Contains("player@example.test", body);
            Assert.Contains("challenge", body);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "email-code" }) };
        }));
        Assert.Equal("challenge", (await site.GetChallengeAsync(CancellationToken.None)).Id);
        Assert.Equal("email-code", await site.SendCodeAsync("player@example.test", "challenge", "12345", CancellationToken.None));
        Assert.Equal(["/api/captcha", "/api/auth/code"], requests);
    }

    [Fact]
    public async Task FeedbackOnlySendsDiagnosticLogs()
    {
        var settings = Path.Combine(Path.GetTempPath(), "aspa-mac-feedback-" + Guid.NewGuid().ToString("N"));
        var logs = Directory.CreateDirectory(Path.Combine(settings, "logs"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(logs.FullName, "aspa-2026-11-13.log"), "diagnostics");
            await File.WriteAllTextAsync(Path.Combine(logs.FullName, "private.log"), "private");
            using var site = new LineupSiteClient(settings, new Handler(async (request, token) =>
            {
                Assert.Equal("/api/desktop/feedback", request.RequestUri!.AbsolutePath);
                using var archive = new ZipArchive(await request.Content!.ReadAsStreamAsync(token), ZipArchiveMode.Read);
                var entry = Assert.Single(archive.Entries);
                Assert.Equal("aspa-2026-11-13.log", entry.FullName);
                return new(HttpStatusCode.Created)
                { Content = JsonContent.Create(new { id = "37b099dc-0ed2-49e2-8494-45994a57a7ab" }) };
            }));
            Assert.Equal("37b099dc-0ed2-49e2-8494-45994a57a7ab", await site.SendFeedbackAsync());
        }
        finally { Directory.Delete(settings, recursive: true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            send(request, token);
    }
}
