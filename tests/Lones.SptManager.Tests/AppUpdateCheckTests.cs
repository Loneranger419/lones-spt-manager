using System.Net;
using Lones.SptManager.Core;
using Lones.SptManager.Core.Update;

namespace Lones.SptManager.Tests;

public sealed class AppUpdateCheckTests
{
    [Theory]
    [InlineData("V0.1.4", "0.1.3", 1)]
    [InlineData("0.1.3", "V0.1.3", 0)]
    [InlineData("v0.1.2", "0.1.3", -1)]
    [InlineData("0.2.0", "0.1.9", 1)]
    [InlineData("0.1", "0.1.0", 0)]
    public void CompareVersions_IgnoresVPrefix(string left, string right, int expectedSign)
    {
        var result = AppUpdateCheck.CompareVersions(left, right);
        Assert.NotNull(result);
        Assert.Equal(expectedSign, Math.Sign(result.Value));
    }

    [Fact]
    public void TryParseRelease_NewerTag_ReturnsInfo()
    {
        var result = AppUpdateCheck.TryParseRelease(
            """
            {
              "tag_name": "V0.1.4",
              "html_url": "https://github.com/Loneranger419/lones-spt-manager/releases/tag/V0.1.4",
              "prerelease": false,
              "draft": false
            }
            """,
            "0.1.3");

        Assert.Equal(AppUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Update);
        Assert.Equal("0.1.4", result.Update.LatestVersion);
        Assert.Equal("0.1.3", result.Update.CurrentVersion);
        Assert.Contains("V0.1.4", result.Update.ReleaseUrl, StringComparison.Ordinal);
        Assert.Contains("0.1.4 is available", result.Update.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseRelease_SameVersion_IsCurrent()
    {
        var result = AppUpdateCheck.TryParseRelease(
            """{ "tag_name": "V0.1.3", "html_url": "https://example.test/V0.1.3" }""",
            "0.1.3");
        Assert.Equal(AppUpdateCheckStatus.Current, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public void TryParseRelease_OlderOrDraft_IsCurrent()
    {
        Assert.Equal(
            AppUpdateCheckStatus.Current,
            AppUpdateCheck.TryParseRelease(
                """{ "tag_name": "V0.1.2", "html_url": "https://example.test/V0.1.2" }""",
                "0.1.3").Status);
        Assert.Equal(
            AppUpdateCheckStatus.Current,
            AppUpdateCheck.TryParseRelease(
                """{ "tag_name": "V0.9.0", "html_url": "https://example.test/V0.9.0", "draft": true }""",
                "0.1.3").Status);
        Assert.Equal(
            AppUpdateCheckStatus.Current,
            AppUpdateCheck.TryParseRelease(
                """{ "tag_name": "V0.9.0", "html_url": "https://example.test/V0.9.0", "prerelease": true }""",
                "0.1.3").Status);
    }

    [Fact]
    public async Task CheckLatestAsync_NewerRelease_ReturnsInfo()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """{ "tag_name": "V0.1.4", "html_url": "https://example.test/V0.1.4" }""");
        using var http = new HttpClient(handler);
        var result = await AppUpdateCheck.CheckLatestAsync(http, "0.1.3");
        Assert.Equal(AppUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.1.4", result.Update?.LatestVersion);
        Assert.Equal(ProductInfo.LatestReleaseApiUrl, handler.LastUrl);
        Assert.Contains("Lones-SPT-Manager", handler.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckLatestAsync_HttpError_IsUnavailable()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, """{"message":"rate limited"}""");
        using var http = new HttpClient(handler);
        var result = await AppUpdateCheck.CheckLatestAsync(http, "0.1.3");
        Assert.Equal(AppUpdateCheckStatus.Unavailable, result.Status);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHandler(HttpStatusCode status, string body)
        {
            _response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            };
        }

        public string? LastUrl { get; private set; }

        public string UserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(_response);
        }
    }
}
