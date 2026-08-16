using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Tests;

public sealed class ThumbnailCacheTests
{
    [Theory]
    [InlineData("https://files.sp-mod.com/mods/1861.png", true)]
    [InlineData("https://sp-mod.com/mod/1343/skipper", true)]
    [InlineData("http://files.sp-mod.com/mods/1861.png", false)]
    [InlineData("https://evil.example/thumb.png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedUrl_OnlyForgeCdnHttps(string? url, bool allowed)
        => Assert.Equal(allowed, ThumbnailCache.IsAllowedUrl(url));

    [Fact]
    public void TryLocalPath_IsNullUntilFileExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-thumb-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string url = "https://files.sp-mod.com/mods/1861.png";
            Assert.Null(ThumbnailCache.TryLocalPath(root, url));
            var dest = ThumbnailCache.LocalPathFor(root, url);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, [1, 2, 3]);
            Assert.Equal(dest, ThumbnailCache.TryLocalPath(root, url));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
