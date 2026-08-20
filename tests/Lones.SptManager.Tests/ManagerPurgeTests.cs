using Lones.SptManager.Core;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Tests;

public sealed class ManagerPurgeTests
{
    [Fact]
    public void Purge_RemovesStoreAndJunctions_KeepsGame()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Talk", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Talk/talk.dll"] = "client",
            ["SPT_Runtime/user/mods/Talk/mod.dll"] = "server"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request()).Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/Talk")));
        Assert.True(Directory.Exists(ModStore.StoreRoot(fx.ManagerData)));

        var result = ManagerPurge.Run(fx.ManagerData, fx.GameRoot, fx.Lock);
        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(ModStore.StoreRoot(fx.ManagerData)));
        Assert.False(Directory.Exists(ProfilePaths.ProfilesRoot(fx.ManagerData)));
        Assert.False(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/Talk")));
        Assert.False(Directory.Exists(fx.Install("BepInEx/plugins/Talk")));
        Assert.True(File.Exists(fx.Install(SptLayout.EscapeFromTarkovExe)));
        Assert.True(File.Exists(fx.Install(SptLayout.BepInExPluginsSpt + "/spt-core.dll")));
        Assert.True(Directory.Exists(fx.Install(SptLayout.UserProfiles)));
        Assert.True(Directory.Exists(fx.Install(SptLayout.BepInExConfig)));
        Assert.True(Directory.Exists(fx.ManagerData));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fx.ManagerData));
    }

    [Fact]
    public void Purge_KeepsExeInManagerDataFolder()
    {
        using var fx = new DeployFixture();
        Directory.CreateDirectory(Path.Combine(fx.ManagerData, "store"));
        Directory.CreateDirectory(Path.Combine(fx.ManagerData, "profiles"));
        File.WriteAllText(Path.Combine(fx.ManagerData, ProductInfo.ExeFileName), "exe");
        File.WriteAllText(Path.Combine(fx.ManagerData, "mods.json.example"), "{}");
        File.WriteAllText(Path.Combine(fx.ManagerData, AppSettings.FileName), """{ "theme": "dark" }""");
        File.WriteAllText(Path.Combine(fx.ManagerData, "scratch.txt"), "gone");

        var result = ManagerPurge.Run(fx.ManagerData, fx.GameRoot, fx.Lock);
        Assert.True(result.Success, result.Message);
        Assert.False(Directory.Exists(ModStore.StoreRoot(fx.ManagerData)));
        Assert.False(Directory.Exists(ProfilePaths.ProfilesRoot(fx.ManagerData)));
        Assert.False(File.Exists(Path.Combine(fx.ManagerData, AppSettings.FileName)));
        Assert.False(File.Exists(Path.Combine(fx.ManagerData, "scratch.txt")));
        Assert.Equal("exe", File.ReadAllText(Path.Combine(fx.ManagerData, ProductInfo.ExeFileName)));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(fx.ManagerData, "mods.json.example")));
        Assert.True(Directory.Exists(fx.ManagerData));
    }

    [Fact]
    public void Purge_RefusesGameInsideManagerData()
    {
        using var fx = new DeployFixture();
        var nestedGame = Path.Combine(fx.ManagerData, "game");
        Directory.CreateDirectory(nestedGame);
        var result = ManagerPurge.Run(fx.ManagerData, nestedGame, fx.Lock);
        Assert.False(result.Success);
        Assert.Contains("inside manager data", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(fx.ManagerData));
    }

    [Fact]
    public void Purge_RefusesUnrelatedFolder()
    {
        var unrelated = Path.Combine(Path.GetTempPath(), "lones-purge-unrelated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "notes.txt"), "keep");
        try
        {
            var result = ManagerPurge.Run(unrelated, gameRoot: null);
            Assert.False(result.Success);
            Assert.Contains("does not look like", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(unrelated, "notes.txt")));
        }
        finally
        {
            Directory.Delete(unrelated, recursive: true);
        }
    }

    [Fact]
    public void Purge_BlockedWhileSptRunning()
    {
        using var fx = new DeployFixture();
        Directory.CreateDirectory(Path.Combine(fx.ManagerData, "store"));
        fx.Lock.Running.Add("SPT.Server");
        var result = ManagerPurge.Run(fx.ManagerData, fx.GameRoot, fx.Lock);
        Assert.False(result.Success);
        Assert.Contains("SPT.Server", result.Message);
        Assert.True(Directory.Exists(Path.Combine(fx.ManagerData, "store")));
    }
}
