using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class LineupPublicLinkTests
{
    [Theory]
    [InlineData("https://aspa.moonfair.cn/s/AbCd1234AbCd1234AbCd12", true)]
    [InlineData("https://aspa.moonfair.cn/s/demo-kjerag", true)]
    [InlineData("https://aspa.moonfair.cn/s/u-AbCd1234AbCd1234AbCd12", true)]
    [InlineData("https://aspa.moonfair.cn.evil.test/s/demo-kjerag", false)]
    [InlineData("https://aspa.moonfair.cn:444/s/demo-kjerag", false)]
    [InlineData("https://name@aspa.moonfair.cn/s/demo-kjerag", false)]
    [InlineData("http://aspa.moonfair.cn/s/demo-kjerag", false)]
    [InlineData("https://aspa.moonfair.cn/s/demo-kjerag?edit=1", false)]
    public void RecognizesOnlySiteShortLinks(string input, bool expected)
        => LineupPublicLink.TryGetToken(input, out _).Should().Be(expected);

    [Fact]
    public async Task ResolvesToShareTextUsingFixedOrigin()
    {
        const string link = "https://aspa.moonfair.cn/s/demo-kjerag";
        const string share = "ASPA-LINEUP/1\n{\"Name\":\"测试阵容\"}";
        using var client = new HttpClient(new Resolver());
        (await LineupPublicLink.ResolveAsync(link, client)).Should().Be(share);
    }

    [Theory]
    [InlineData("/s/u-AbCd1234AbCd1234AbCd12", true)]
    [InlineData("//evil.test/s/u-AbCd1234AbCd1234AbCd12", false)]
    [InlineData("/s/demo-kjerag", false)]
    public async Task CreatesShortLinkUsingShareTextAndAcceptsOnlySiteSnapshotPaths(string shortPath, bool valid)
    {
        using var client = new HttpClient(new Creator(shortPath));
        Func<Task<string>> create = () => LineupPublicLink.CreateAsync("ASPA-LINEUP/1\n{}", client);
        if (valid) (await create()).Should().Be("https://aspa.moonfair.cn" + shortPath);
        else await create.Should().ThrowAsync<FormatException>();
    }

    private sealed class Creator(string shortPath) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsoluteUri.Should().Be("https://aspa.moonfair.cn/api/share");
            var body = await request.Content!.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            body.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("share", "ASPA-LINEUP/1\n{}"));
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new { shortPath }) };
        }
    }

    private sealed class Resolver : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsoluteUri.Should().Be("https://aspa.moonfair.cn/api/share/demo-kjerag");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    share = "ASPA-LINEUP/1\n{\"Name\":\"测试阵容\"}", shortPath = "/s/demo-kjerag"
                })
            });
        }
    }
}
