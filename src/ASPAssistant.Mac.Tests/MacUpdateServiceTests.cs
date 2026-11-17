using System.Net;
using System.Net.Http.Json;
using ASPAssistant.Mac;

namespace ASPAssistant.Mac.Tests;

public class MacUpdateServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyOffersMacArm64ReleaseAsset(bool includeMacAsset)
    {
        using var http = new HttpClient(new Handler(() => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                tag_name = "v99.0.0",
                assets = includeMacAsset
                    ? new[] { new { name = "ASPAssistant-v99-win-x64.zip", browser_download_url = "https://example.test/win" },
                        new { name = "ASPAssistant-v99-osx-arm64.zip", browser_download_url = "https://example.test/mac" } }
                    : new[] { new { name = "ASPAssistant-v99-win-x64.zip", browser_download_url = "https://example.test/win" } }
            })
        }));
        var update = await new MacUpdateService(http).CheckAsync();
        Assert.Equal(includeMacAsset, update != null);
        if (includeMacAsset) Assert.Equal("https://example.test/mac", update!.DownloadUrl);
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            return Task.FromResult(response());
        }
    }
}
