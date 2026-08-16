using System.Net;
using Lones.SptManager.Forge;

namespace Lones.SptManager.Tests;

public sealed class ForgeClientTests
{
    [Fact]
    public void Constructor_RejectsDeadHosts()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://forge.sp-mod.com/api/v0/") };
        Assert.Throws<InvalidOperationException>(() => new ForgeClient(http));
    }

    [Fact]
    public async Task Ping_RetriesOn429ThenSucceeds()
    {
        var handler = new QueueHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero) }
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true,"data":{"message":"pong"}}""")
            });
        using var http = new HttpClient(handler) { BaseAddress = new Uri(ForgeEndpoints.ApiBase) };
        using var client = new ForgeClient(http);
        Assert.True(await client.PingAsync());
        Assert.Equal(2, handler.SendCount);
    }

    [Fact]
    public void LooksLikeArchive_AcceptsZip()
    {
        var zip = Path.Combine(Path.GetTempPath(), "lones-arc-" + Guid.NewGuid().ToString("N") + ".zip");
        ZipFixture.WriteZip(zip, [("BepInEx/plugins/A/a.dll", "dll")]);
        try
        {
            Assert.True(ForgeClient.LooksLikeArchive(zip));
            File.WriteAllText(zip, "not a zip");
            Assert.False(ForgeClient.LooksLikeArchive(zip));
        }
        finally
        {
            File.Delete(zip);
        }
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            Assert.Equal("ping", request.RequestUri?.ToString().TrimEnd('/').Split('/').Last());
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
