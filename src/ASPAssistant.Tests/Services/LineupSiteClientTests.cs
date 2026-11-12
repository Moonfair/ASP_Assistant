using System.IO.Compression;
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

    [Fact]
    public async Task FeedbackUploadsOnlyDiagnosticLogsAsZipWithoutSigningIn()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"ASPAssistant.FeedbackTests.{Guid.NewGuid():N}");
        var logs = Directory.CreateDirectory(Path.Combine(settings, "logs")).FullName;
        const string dailyLog = "aspa-2026-11-12.log";
        const string feedbackId = "37b099dc-0ed2-49e2-8494-45994a57a7ab";
        try
        {
            await File.WriteAllTextAsync(Path.Combine(logs, dailyLog), "识别诊断");
            await File.WriteAllTextAsync(Path.Combine(logs, "update-errors.log"), "更新失败");
            await File.WriteAllTextAsync(Path.Combine(logs, "aspa-private.log"), "unrelated");
            await File.WriteAllTextAsync(Path.Combine(settings, "lineup-site-session.dat"), "unrelated");
            using var client = new LineupSiteClient(settings, new SiteHandler(async (request, ct) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://aspa.moonfair.cn/api/desktop/feedback", request.RequestUri!.AbsoluteUri);
                Assert.Null(request.Headers.Authorization);
                Assert.Equal("application/zip", request.Content!.Headers.ContentType!.MediaType);
                using var archive = new ZipArchive(await request.Content.ReadAsStreamAsync(ct), ZipArchiveMode.Read);
                Assert.Equal(new[] { dailyLog, "update-errors.log" },
                    archive.Entries.Select(entry => entry.FullName).OrderBy(name => name));
                using var log = new StreamReader(archive.GetEntry(dailyLog)!.Open());
                Assert.Equal("识别诊断", await log.ReadToEndAsync(ct));
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(new { id = feedbackId }) };
            }));
            Assert.Equal(feedbackId, await client.SendFeedbackAsync());
        }
        finally
        {
            Directory.Delete(settings, recursive: true);
        }
    }

    [Fact]
    public async Task FeedbackKeepsTheEndOfAnOversizedLog()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"ASPAssistant.FeedbackTests.{Guid.NewGuid():N}");
        var logs = Directory.CreateDirectory(Path.Combine(settings, "logs")).FullName;
        try
        {
            const int sourceLimit = 8 * 1024 * 1024;
            var path = Path.Combine(logs, "aspa-2026-11-12.log");
            using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            writer.WriteByte(1);
            writer.SetLength(sourceLimit + 1);
            writer.Position = sourceLimit;
            writer.WriteByte(2);
            writer.Flush();
            using var client = new LineupSiteClient(settings, new SiteHandler(async (request, ct) =>
            {
                using var archive = new ZipArchive(await request.Content!.ReadAsStreamAsync(ct), ZipArchiveMode.Read);
                var entry = Assert.Single(archive.Entries);
                Assert.Equal(sourceLimit, entry.Length);
                using var contents = entry.Open();
                using var output = new MemoryStream();
                await contents.CopyToAsync(output, ct);
                Assert.Equal(0, output.GetBuffer()[0]);
                Assert.Equal(2, output.GetBuffer()[sourceLimit - 1]);
                return new(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new { id = "37b099dc-0ed2-49e2-8494-45994a57a7ab" })
                };
            }));
            await client.SendFeedbackAsync();
        }
        finally
        {
            Directory.Delete(settings, recursive: true);
        }
    }

    [Fact]
    public async Task FeedbackWithNoLogsDoesNotSendARequest()
    {
        var settings = Path.Combine(Path.GetTempPath(), $"ASPAssistant.FeedbackTests.{Guid.NewGuid():N}");
        using var client = new LineupSiteClient(settings, new SiteHandler((_, _) =>
            throw new InvalidOperationException("No feedback request should be sent.")));
        await Assert.ThrowsAsync<LineupSiteException>(() => client.SendFeedbackAsync());
    }

    private sealed record Upload(Guid LocalId, string Share);

    private sealed class SiteHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
