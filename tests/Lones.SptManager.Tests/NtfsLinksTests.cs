using Lones.SptManager.Native;

namespace Lones.SptManager.Tests;

public sealed class NtfsLinksTests
{
    [Fact]
    public void Junction_WriteThrough_LandsInTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-junc-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            NtfsLinks.CreateJunction(link, target);
            Assert.True(NtfsLinks.IsJunction(link));
            Assert.False(NtfsLinks.IsJunction(target));
            var resolved = NtfsLinks.TryGetJunctionTarget(link);
            Assert.NotNull(resolved);
            Assert.True(SafeFileSystem.SamePath(resolved, target));
            File.WriteAllText(Path.Combine(link, "write-through.txt"), "ok");
            Assert.True(File.Exists(Path.Combine(target, "write-through.txt")));
            NtfsLinks.RemoveJunction(link);
            Assert.False(Directory.Exists(link));
            Assert.True(Directory.Exists(target));
            Assert.True(File.Exists(Path.Combine(target, "write-through.txt")));
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
