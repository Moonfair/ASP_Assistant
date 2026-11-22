using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class BetaChannelTests
{
    private const string Manifest = "https://aspa.oss-cn-beijing.aliyuncs.com/updates/beta/latest.json";
    private const string Stable = "https://api.github.com/repos/Moonfair/ASP_Assistant/releases/latest";
    private const string Tag = "v3.9.0-beta.10";
    private const string Asset = "ASPAssistant-" + Tag + "-win-x64-setup.exe";
    private static readonly byte[] Bytes = "beta installer fixture"u8.ToArray();
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Bytes));

    [Theory]
    [InlineData(false, "v3.9.0", false, true)]
    [InlineData(true, "v3.9.0", false, true)]
    [InlineData(false, Tag, false, false)]
    [InlineData(true, Tag, true, false)]
    [InlineData(true, "v3.9.0", true, false)]
    public async Task OrdinaryChecksUseOnlyStable(bool manual, string tag, bool prerelease, bool available)
    {
        using var handler = new Handler(url =>
        {
            url.Should().Be(Stable);
            return Json(new { tag_name = tag, prerelease, assets = new[] {
                new { name = $"ASPAssistant-{tag}-win-x64-setup.exe", browser_download_url = "https://example.invalid/stable" }
            } });
        });
        using var http = new HttpClient(handler);
        var service = new UpdateService(http, "3.9.0-beta.9+abc123");
        service.CurrentVersionLabel.Should().Be("3.9.0-beta.9");
        var update = await service.CheckForUpdateAsync(userInitiated: manual);
        (update is not null).Should().Be(available);
        handler.Urls.Should().Equal(Stable);
    }

    [Theory]
    [InlineData("3.8.11", true)]
    [InlineData("3.9.0-beta.9", true)]
    [InlineData("3.9.0-beta.10", false)]
    [InlineData("3.9.0-beta.11", false)]
    [InlineData("3.9.0", false)]
    [InlineData("3.10.0", false)]
    public async Task ManualCheckOffersOnlyStrictlyNewerBeta(string installed, bool available)
    {
        using var handler = new Handler(_ => Json(new { tag = Tag, sha256 = Hash, notes = "测试说明" }));
        using var http = new HttpClient(handler);
        var info = await new UpdateService(http, installed).CheckForBetaUpdateAsync();
        (info is not null).Should().Be(available);
        handler.Urls.Should().Equal(Manifest);
        if (info is null) return;
        info.IsBeta.Should().BeTrue();
        info.ReleaseNotes.Should().Be("测试说明");
        info.AssetName.Should().Be(Asset);
        info.DownloadUrl.Should().Be(Manifest.Replace("latest.json", Asset));
    }

    [Fact]
    public async Task MissingManifestIsNotAnError()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.NotFound)));
        (await new UpdateService(http, "3.8.11").CheckForBetaUpdateAsync()).Should().BeNull();
    }

    [Theory]
    [InlineData("v3.9.0", true)]
    [InlineData("v3.9.0-rc.1", true)]
    [InlineData("v3.9.0-beta.1/../../test", true)]
    [InlineData(Tag, false)]
    public async Task InvalidManifestFailsClosed(string tag, bool validHash)
    {
        using var http = new HttpClient(new Handler(_ => Json(new { tag, sha256 = validHash ? Hash : "abc" })));
        var check = () => new UpdateService(http, "3.8.11").CheckForBetaUpdateAsync();
        await check.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BetaPackageUsesOnlyItsOwnChannelAndRequiresMatchingHash(bool intact)
    {
        using var handler = new Handler(url => url == Manifest
            ? Json(new { tag = Tag, sha256 = Hash, url = "https://example.invalid/untrusted" })
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(intact ? Bytes : [0]) });
        using var http = new HttpClient(handler);
        var service = new UpdateService(http, "3.8.11");
        var info = await service.CheckForBetaUpdateAsync();
        var target = Path.GetTempFileName();
        try
        {
            var download = () => service.DownloadPackageAsync(info!, target, null, default);
            if (!intact) await download.Should().ThrowAsync<InvalidDataException>();
            else { await download(); File.ReadAllBytes(target).Should().Equal(Bytes); }
            handler.Urls.Should().Equal(Manifest, Manifest.Replace("latest.json", Asset));
        }
        finally { File.Delete(target); }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(respond(Urls[^1]));
        }
    }
}
