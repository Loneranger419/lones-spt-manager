using System.Net;
using System.Text;
using Lones.SptManager.Core;
using Lones.SptManager.Core.Update;

namespace Lones.SptManager.Tests;

public sealed class AppUpdateApplyTests
{
    [Theory]
    [InlineData("https://github.com/Loneranger419/lones-spt-manager/releases/download/V0.1.4/LonesSptManager-win-x64.zip", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/123/file", true)]
    [InlineData("https://evil.example/LonesSptManager-win-x64.zip", false)]
    [InlineData("http://github.com/Loneranger419/lones-spt-manager/releases/download/V0.1.4/LonesSptManager.exe", false)]
    [InlineData("https://github.com/other/repo/releases/download/V0.1.4/LonesSptManager.exe", false)]
    public void IsTrustedDownloadUrl_AllowsGitHubOnly(string url, bool expected)
        => Assert.Equal(expected, AppUpdateApply.IsTrustedDownloadUrl(url));

    [Fact]
    public void PickAsset_PrefersZipOverExe_IgnoresUntrusted()
    {
        var picked = AppUpdateCheck.PickAsset(
        [
            new("mods.json.example", "https://github.com/Loneranger419/lones-spt-manager/releases/download/V0.1.4/mods.json.example", 10),
            new(ProductInfo.ExeFileName, "https://evil.example/" + ProductInfo.ExeFileName, 20),
            new(ProductInfo.ExeFileName, "https://github.com/Loneranger419/lones-spt-manager/releases/download/V0.1.4/" + ProductInfo.ExeFileName, 30),
            new(ProductInfo.ReleaseZipAsset, "https://github.com/Loneranger419/lones-spt-manager/releases/download/V0.1.4/" + ProductInfo.ReleaseZipAsset, 40)
        ]);
        Assert.NotNull(picked);
        Assert.Equal(ProductInfo.ReleaseZipAsset, picked.Value.Name);
        Assert.Equal(40, picked.Value.Size);
    }

    [Fact]
    public void UnpackRelease_KeepsAllowlistedFiles_DropsTraversalAndExtras()
    {
        var root = NewTemp();
        try
        {
            var zip = Path.Combine(root, ProductInfo.ReleaseZipAsset);
            ZipFixture.WriteZip(zip,
            [
                ("readme.txt", "nope"),
                ("nested/" + ProductInfo.ExeFileName, "exe-bytes"),
                ("../evil.exe", "bad"),
                ("mods.json.example", "{ }")
            ]);
            var staging = Path.Combine(root, "staging");
            var exe = AppUpdateApply.UnpackRelease(zip, staging);
            Assert.Equal(Path.Combine(staging, ProductInfo.ExeFileName), exe);
            Assert.Equal("exe-bytes", File.ReadAllText(exe));
            Assert.Equal("{ }", File.ReadAllText(Path.Combine(staging, "mods.json.example")));
            Assert.False(File.Exists(Path.Combine(staging, "readme.txt")));
            Assert.False(File.Exists(Path.Combine(root, "evil.exe")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UnpackRelease_LooseExe_Copies()
    {
        var root = NewTemp();
        try
        {
            var source = Path.Combine(root, ProductInfo.ExeFileName);
            File.WriteAllText(source, "single");
            var staging = Path.Combine(root, "staging");
            var exe = AppUpdateApply.UnpackRelease(source, staging);
            Assert.Equal("single", File.ReadAllText(exe));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteApplyScript_EmbedsPidAndPaths()
    {
        var root = NewTemp();
        try
        {
            var staging = Path.Combine(root, "staging");
            var dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(dest);
            var plan = AppUpdateApply.WriteApplyScript(4242, staging, dest);
            var text = File.ReadAllText(plan.ScriptPath);
            Assert.Contains("4242", text, StringComparison.Ordinal);
            Assert.Contains(ProductInfo.ExeFileName, text, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(staging), text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(dest), text, StringComparison.OrdinalIgnoreCase);
            File.Delete(plan.ScriptPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryGetInstallDirectory_RequiresPublishedExeName()
    {
        Assert.Null(AppUpdateApply.TryGetInstallDirectory(@"C:\dotnet\dotnet.exe"));
        Assert.Equal(
            @"C:\Tools",
            AppUpdateApply.TryGetInstallDirectory(@"C:\Tools\" + ProductInfo.ExeFileName));
    }

    [Fact]
    public async Task DownloadAsync_WritesTrustedAsset()
    {
        var root = NewTemp();
        try
        {
            var body = Encoding.UTF8.GetBytes("zip-bytes");
            var handler = new BytesHandler(
                HttpStatusCode.OK,
                body,
                "https://github.com/Loneranger419/lones-spt-manager/releases/download/V0.1.4/" + ProductInfo.ReleaseZipAsset);
            using var http = new HttpClient(handler);
            var dest = Path.Combine(root, ProductInfo.ReleaseZipAsset);
            var update = new AppUpdateInfo
            {
                CurrentVersion = "0.1.3",
                LatestVersion = "0.1.4",
                ReleaseUrl = ProductInfo.ReleasesUrl,
                DownloadUrl = "https://github.com/Loneranger419/lones-spt-manager/releases/download/V0.1.4/" + ProductInfo.ReleaseZipAsset,
                AssetName = ProductInfo.ReleaseZipAsset,
                AssetSize = body.Length
            };
            await AppUpdateApply.DownloadAsync(http, update, dest);
            Assert.Equal(body, File.ReadAllBytes(dest));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-app-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class BytesHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public BytesHandler(HttpStatusCode status, byte[] body, string url)
        {
            _response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, url)
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}
