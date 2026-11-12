using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class UpdateServiceTests : IDisposable
{
    private const string GitHubUrl = "https://github.com/Moonfair/ASP_Assistant/releases/download/v99.1.2/ASPAssistant-v99.1.2-win-x64.zip";
    private const string OssUrl = "https://aspa.oss-cn-beijing.aliyuncs.com/releases/ASPAssistant-v99.1.2-win-x64.zip";
    private static readonly byte[] Package = "update package"u8.ToArray();
    private static readonly string Digest = $"sha256:{Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant()}";
    private readonly string _zipPath = Path.Combine(Path.GetTempPath(), $"asp_update_test_{Guid.NewGuid():N}.zip");

    [Theory]
    [InlineData("valid", false)]
    [InlineData("missing", true)]
    [InlineData("corrupt", true)]
    [InlineData("timeout", true)]
    public async Task Download_PrefersOssAndFallsBackOnlyOnFailure(string mirrorResult, bool fallsBack)
    {
        using var handler = new ReleaseHandler(Digest, _ => mirrorResult switch
        {
            "missing" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "corrupt" => PackageResponse("an invalid package longer than the good one"u8.ToArray()),
            "timeout" => throw new TaskCanceledException("HTTP request timed out"),
            _ => PackageResponse(Package)
        });
        using var http = new HttpClient(handler);
        var service = new UpdateService(http);
        var info = await service.CheckForUpdateAsync(userInitiated: true);
        info.Should().NotBeNull();

        await service.DownloadPackageAsync(info!, _zipPath, null, CancellationToken.None);

        handler.Downloads.Should().Equal(fallsBack ? new[] { OssUrl, GitHubUrl } : new[] { OssUrl });
        (await File.ReadAllBytesAsync(_zipPath)).Should().Equal(Package);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:invalid")]
    public async Task Download_UsesGitHubWhenReleaseHasNoUsableDigest(string? digest)
    {
        using var handler = new ReleaseHandler(digest, _ => PackageResponse(Package));
        using var http = new HttpClient(handler);
        var service = new UpdateService(http);
        var info = await service.CheckForUpdateAsync(userInitiated: true);

        await service.DownloadPackageAsync(info!, _zipPath, null, CancellationToken.None);

        handler.Downloads.Should().Equal(GitHubUrl);
        (await File.ReadAllBytesAsync(_zipPath)).Should().Equal(Package);
    }

    [Fact]
    public async Task Download_RejectsCorruptGitHubFallback()
    {
        using var handler = new ReleaseHandler(Digest, _ => new HttpResponseMessage(HttpStatusCode.NotFound))
        {
            GitHubPackage = "corrupt"u8.ToArray()
        };
        using var http = new HttpClient(handler);
        var service = new UpdateService(http);
        var info = await service.CheckForUpdateAsync(userInitiated: true);

        var download = () => service.DownloadPackageAsync(info!, _zipPath, null, CancellationToken.None);
        await download.Should().ThrowAsync<InvalidDataException>();
        handler.Downloads.Should().Equal(OssUrl, GitHubUrl);
    }

    [Fact]
    public async Task Download_UserCancellationDoesNotTryGitHub()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new ReleaseHandler(Digest, token =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The request token was not canceled.");
        });
        using var http = new HttpClient(handler);
        var service = new UpdateService(http);
        var info = await service.CheckForUpdateAsync(userInitiated: true);

        var download = () => service.DownloadPackageAsync(info!, _zipPath, null, cts.Token);
        await download.Should().ThrowAsync<OperationCanceledException>();
        handler.Downloads.Should().Equal(OssUrl);
    }

    private static HttpResponseMessage PackageResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
    };

    private sealed class ReleaseHandler(string? digest, Func<CancellationToken, HttpResponseMessage> ossResponse)
        : HttpMessageHandler
    {
        public List<string> Downloads { get; } = [];
        public byte[] GitHubPackage { get; init; } = Package;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == "https://api.github.com/repos/Moonfair/ASP_Assistant/releases/latest")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        tag_name = "v99.1.2", body = "Release notes",
                        assets = new[] { new { name = "ASPAssistant-v99.1.2-win-x64.zip", browser_download_url = GitHubUrl, digest } }
                    })
                });
            }

            Downloads.Add(url);
            return Task.FromResult(url switch
            {
                OssUrl => ossResponse(ct),
                GitHubUrl => PackageResponse(GitHubPackage),
                _ => throw new InvalidOperationException($"Unexpected URL: {url}")
            });
        }
    }

    public void Dispose() => File.Delete(_zipPath);
}
