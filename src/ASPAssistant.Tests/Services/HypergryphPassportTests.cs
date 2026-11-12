using ASPAssistant.App.Services;

namespace ASPAssistant.Tests.Services;

public sealed class HypergryphPassportTests
{
    [Theory]
    [InlineData("https://web-api.hypergryph.com/account/info/hg", "POST", 200, true)]
    [InlineData("https://web-api.hypergryph.com/account/info/hg", "GET", 200, false)]
    [InlineData("https://web-api.hypergryph.com/account/info/hg", "POST", 401, false)]
    [InlineData("https://web-api.hypergryph.com/account/info/hg/check", "POST", 200, false)]
    [InlineData("https://web-api.hypergryph.com.example.com/account/info/hg", "POST", 200, false)]
    public void OnlySuccessfulPassportSessionSaveTriggersAutomaticPull(string url, string method, int status, bool expected)
        => Assert.Equal(expected, HypergryphPassport.IsSessionSaved(url, method, status));
}
