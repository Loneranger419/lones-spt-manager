using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Mapping;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Tests;

public sealed class LeftoverImportTests
{
    [Fact]
    public void ImportPluginFolder_WritesStore_RemovesLeftover_DeployJunctions()
    {
        using var fx = new DeployFixture();
        Directory.CreateDirectory(fx.Install("BepInEx/plugins/OrphanMod"));
        File.WriteAllText(fx.Install("BepInEx/plugins/OrphanMod/x.dll"), "disk");

        var imported = new InstallMapper().ImportInstallTree(
            fx.GameRoot,
            "BepInEx/plugins/OrphanMod",
            fx.ManagerData,
            new MapperOptions { Version = "1.0", ModKey = "OrphanMod" });
        Assert.NotNull(imported.Document);
        Assert.False(Directory.Exists(fx.Install("BepInEx/plugins/OrphanMod")));
        Assert.True(File.Exists(Path.Combine(ModStore.FilesDirectory(fx.ManagerData, "OrphanMod", "1.0"), "BepInEx", "plugins", "OrphanMod", "x.dll")));

        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.DoesNotContain(snap.Items, item => item.Kind == InstallInventory.LeftoverKind && item.Key == "OrphanMod");
        Assert.Contains(snap.Items, item => item.Kind == InstallInventory.StoreKind && item.Key == "OrphanMod");

        var result = fx.Engine.Deploy(fx.Request(fx.Enable(("OrphanMod", "1.0", 0))));
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/OrphanMod")));
        Assert.Equal("disk", File.ReadAllText(fx.Install("BepInEx/plugins/OrphanMod/x.dll")));
    }

    [Fact]
    public void ImportLoosePluginDll_WrapsAndJunctions()
    {
        using var fx = new DeployFixture();
        File.WriteAllText(fx.Install("BepInEx/plugins/CoolMod.dll"), "dll");

        var imported = new InstallMapper().ImportInstallTree(
            fx.GameRoot,
            "BepInEx/plugins/CoolMod.dll",
            fx.ManagerData,
            new MapperOptions { AllowLowConfidence = true, Version = "1.0", ModKey = "CoolMod" });
        Assert.NotNull(imported.Document);
        Assert.False(File.Exists(fx.Install("BepInEx/plugins/CoolMod.dll")));
        Assert.Contains(imported.Document!.Files, file => file.CanonicalPath == "BepInEx/plugins/CoolMod/CoolMod.dll");

        var result = fx.Engine.Deploy(fx.Request(fx.Enable(("CoolMod", "1.0", 0))));
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/CoolMod")));
        Assert.Equal("dll", File.ReadAllText(fx.Install("BepInEx/plugins/CoolMod/CoolMod.dll")));
    }

    [Fact]
    public void ImportUserModsFolder_AllowsDeployJunction()
    {
        using var fx = new DeployFixture();
        Directory.CreateDirectory(fx.Install(SptLayout.UserMods + "/Talk"));
        File.WriteAllText(fx.Install(SptLayout.UserMods + "/Talk/mod.dll"), "talk");

        var imported = new InstallMapper().ImportInstallTree(
            fx.GameRoot,
            SptLayout.UserMods + "/Talk",
            fx.ManagerData,
            new MapperOptions { Version = "1.0", ModKey = "Talk" });
        Assert.NotNull(imported.Document);
        Assert.False(Directory.Exists(fx.Install(SptLayout.UserMods + "/Talk")));

        var result = fx.Engine.Deploy(fx.Request(fx.Enable(("Talk", "1.0", 0))));
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install(SptLayout.UserMods)));
        Assert.Equal("talk", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Talk/mod.dll")));
    }

    [Fact]
    public void ImportSptOwnedPath_IsRefused()
    {
        using var fx = new DeployFixture();
        var imported = new InstallMapper().ImportInstallTree(fx.GameRoot, SptLayout.BepInExPluginsSpt, fx.ManagerData);
        Assert.Null(imported.Document);
        Assert.Contains("SPT-owned", imported.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(fx.Install(SptLayout.BepInExPluginsSpt)));
    }
}
