using System.Text.Json;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Tests;

public sealed class InstallInventoryTests
{
    [Fact]
    public void Scan_ListsStoreMods_AndLeftoverInstallFolder()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        Directory.CreateDirectory(fx.Install("BepInEx/plugins/OrphanMod"));
        File.WriteAllText(fx.Install("BepInEx/plugins/OrphanMod/x.dll"), "disk");

        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.Contains(snap.Items, item => item.Kind == InstallInventory.StoreKind && item.Key == "Keep" && item.Enabled);
        Assert.Contains(snap.Items, item => item.Kind == InstallInventory.LeftoverKind && item.Key == "OrphanMod");
        Assert.DoesNotContain(snap.Items, item => item.Key.Equals("spt", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, snap.LeftoverCount);
    }

    [Fact]
    public void Disable_ThenScan_ShowsOff()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        fx.PutMod("Drop", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Drop/drop.dll"] = "drop"
        });

        InstallInventory.SetEnabled(fx.ManagerData, "default", "Drop", "1.0", enabled: false);
        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.Contains(snap.Items, item => item.Key == "Keep" && item.Enabled);
        Assert.Contains(snap.Items, item => item.Key == "Drop" && !item.Enabled);
    }

    [Fact]
    public void Disable_LastMod_StaysOff()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        fx.PutMod("Drop", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Drop/drop.dll"] = "drop"
        });

        InstallInventory.SetEnabled(fx.ManagerData, "default", "Drop", "1.0", enabled: false);
        InstallInventory.SetEnabled(fx.ManagerData, "default", "Keep", "1.0", enabled: false);
        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.Contains(snap.Items, item => item.Key == "Keep" && !item.Enabled);
        Assert.Contains(snap.Items, item => item.Key == "Drop" && !item.Enabled);
        var listed = new ProfileStore().TryRead(fx.ManagerData, "default")!.Enabled;
        Assert.Equal(2, listed.Count);
        Assert.All(listed, item => Assert.False(item.IsOn));
    }

    [Fact]
    public void EmptySavedProfile_ShowsAllOff_AndEnablesOnlyThatMod()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        fx.PutMod("Drop", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Drop/drop.dll"] = "drop"
        });
        new ProfileStore().Save(fx.ManagerData, "empty", []);

        var before = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "empty");
        Assert.All(before.Items.Where(item => item.Kind == InstallInventory.StoreKind), item => Assert.False(item.Enabled));

        InstallInventory.SetEnabled(fx.ManagerData, "empty", "Keep", "1.0", enabled: true);
        var after = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "empty");
        Assert.Contains(after.Items, item => item.Key == "Keep" && item.Enabled);
        Assert.Contains(after.Items, item => item.Key == "Drop" && !item.Enabled);
    }

    [Fact]
    public void MovePriority_SwapsOrder()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0 },
            new EnabledMod { ModKey = "B", Version = "1.0", Priority = 1 }
        ]);

        InstallInventory.MovePriority(fx.ManagerData, "default", "B", "1.0", delta: -1);
        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        var a = snap.Items.Single(item => item.Key == "A");
        var b = snap.Items.Single(item => item.Key == "B");
        Assert.True(b.Priority < a.Priority);
    }

    [Fact]
    public void Scan_HidesRuntimePackage_WhenParentExists()
    {
        using var fx = new DeployFixture();
        fx.PutMod("fika-server", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/fika-server/FikaServer.dll"] = "server"
        });
        fx.PutMod("fika-server", HarvestRules.RuntimeVersion, new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/fika-server/assets/configs/fika.jsonc"] = "{ }"
        });

        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        var rows = snap.Items.Where(item => item.Kind == InstallInventory.StoreKind && item.Key == "fika-server").ToArray();
        Assert.Single(rows);
        Assert.Equal("1.0", rows[0].Version);
        Assert.Equal(1, rows[0].RuntimeFileCount);
    }

    [Fact]
    public void SetEnabled_ParentOff_DropsRuntimeFromProfile()
    {
        using var fx = new DeployFixture();
        fx.PutMod("fika-server", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/fika-server/FikaServer.dll"] = "server"
        });
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        fx.PutMod("fika-server", HarvestRules.RuntimeVersion, new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/fika-server/assets/configs/fika.jsonc"] = "{ }"
        });

        InstallInventory.SetEnabled(fx.ManagerData, "default", "Keep", "1.0", enabled: false);
        var profile = new ProfileStore().TryRead(fx.ManagerData, "default");
        Assert.Contains(profile!.Enabled, item => item.ModKey == "fika-server" && item.Version == "1.0");
        Assert.DoesNotContain(profile.Enabled, item => HarvestRules.IsRuntimeVersion(item.Version));

        InstallInventory.SetEnabled(fx.ManagerData, "default", "fika-server", "1.0", enabled: false);
        profile = new ProfileStore().TryRead(fx.ManagerData, "default");
        Assert.Contains(profile!.Enabled, item => item.ModKey == "fika-server" && item.Version == "1.0" && !item.IsOn);
    }

    [Fact]
    public void MovePriority_MovesRuntimeWithParent()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        fx.PutMod("B", HarvestRules.RuntimeVersion, new Dictionary<string, string>
        {
            ["BepInEx/plugins/B/generated.json"] = "{}"
        });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0 },
            new EnabledMod { ModKey = "B", Version = "1.0", Priority = 1 }
        ]);

        InstallInventory.SetEnabled(fx.ManagerData, "default", "B", "1.0", enabled: true);
        InstallInventory.MovePriority(fx.ManagerData, "default", "B", "1.0", delta: -1);
        var profile = new ProfileStore().TryRead(fx.ManagerData, "default")!;
        var ordered = profile.Enabled.OrderBy(item => item.Priority).Select(item => item.ModKey + "\0" + item.Version).ToArray();
        Assert.Equal(new[] { "B\01.0", "A\01.0" }, ordered);
        Assert.DoesNotContain(profile.Enabled, item => HarvestRules.IsRuntimeVersion(item.Version));
    }

    [Fact]
    public void DeployedJunction_IsNotLeftover()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        Assert.Equal(Core.Deploy.DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Keep", "1.0", 0)))).Status);

        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.DoesNotContain(snap.Items, item => item.Kind == InstallInventory.LeftoverKind && item.Key == "Keep");
        Assert.Contains(snap.Items, item => item.Kind == InstallInventory.StoreKind && item.Key == "Keep");
    }

    [Fact]
    public void Disable_KeepsPriority()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        fx.PutMod("C", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/C/c.dll"] = "c" });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0, Enabled = true },
            new EnabledMod { ModKey = "B", Version = "1.0", Priority = 1, Enabled = true },
            new EnabledMod { ModKey = "C", Version = "1.0", Priority = 2, Enabled = true }
        ]);

        InstallInventory.SetEnabled(fx.ManagerData, "default", "B", "1.0", enabled: false);
        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.Equal(1, snap.Items.Single(item => item.Key == "B").Priority);
        Assert.False(snap.Items.Single(item => item.Key == "B").Enabled);
        Assert.Equal(0, snap.Items.Single(item => item.Key == "A").Priority);
        Assert.Equal(2, snap.Items.Single(item => item.Key == "C").Priority);
    }

    [Fact]
    public void AddToLoadOrder_AppendsAtEnd()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0, Enabled = true }
        ]);

        InstallInventory.AddToLoadOrder(fx.ManagerData, "default", "B", "1.0");
        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.Equal(0, snap.Items.Single(item => item.Key == "A").Priority);
        Assert.Equal(1, snap.Items.Single(item => item.Key == "B").Priority);
        Assert.True(snap.Items.Single(item => item.Key == "B").Enabled);
    }

    [Fact]
    public void ReplaceLoadOrder_UsesListOrder()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        fx.PutMod("C", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/C/c.dll"] = "c" });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0, Enabled = true }
        ]);

        InstallInventory.ReplaceLoadOrder(fx.ManagerData, "default", [("C", "1.0"), ("A", "1.0")]);
        var ordered = new ProfileStore().TryRead(fx.ManagerData, "default")!.Enabled
            .OrderBy(item => item.Priority)
            .Select(item => item.ModKey)
            .ToArray();
        Assert.Equal(new[] { "C", "A" }, ordered);
        Assert.False(InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default").Items.Single(item => item.Key == "B").Enabled);
    }

    [Fact]
    public void ApplyPackLoadOrder_KeepsManualExtras_AbsorbsWhenPackListsThem()
    {
        using var fx = new DeployFixture();
        fx.PutMod("PackA", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/PackA/a.dll"] = "a" });
        fx.PutMod("Reticule", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/Reticule/r.dll"] = "r" });
        fx.PutMod("LaterPack", "2.0", new Dictionary<string, string> { ["BepInEx/plugins/LaterPack/l.dll"] = "l" });
        WriteForgeId(fx, "PackA", "1.0", 10);
        WriteForgeId(fx, "LaterPack", "2.0", 20);

        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "PackA", Version = "1.0", Priority = 0, Enabled = true }
        ]);
        Assert.Equal(0, InstallInventory.ApplyPackLoadOrder(fx.ManagerData, "default", [("PackA", "1.0")], [10]));

        InstallInventory.AddToLoadOrder(fx.ManagerData, "default", "Reticule", "1.0");
        var extras = InstallInventory.ApplyPackLoadOrder(
            fx.ManagerData,
            "default",
            [("PackA", "1.0"), ("LaterPack", "2.0")],
            [10, 20]);
        Assert.Equal(1, extras);
        var profile = new ProfileStore().TryRead(fx.ManagerData, "default")!;
        Assert.Equal(new[] { "PackA", "LaterPack", "Reticule" }, profile.Enabled.OrderBy(item => item.Priority).Select(item => item.ModKey).ToArray());
        Assert.Equal(new[] { 10, 20 }, profile.PackForgeIds.ToArray());
        Assert.Equal(new[] { "PackA", "LaterPack" }, profile.PackModKeys.ToArray());
        Assert.DoesNotContain("Reticule", profile.PackModKeys);

        WriteForgeId(fx, "Reticule", "1.0", 20);
        extras = InstallInventory.ApplyPackLoadOrder(
            fx.ManagerData,
            "default",
            [("PackA", "1.0"), ("LaterPack", "2.0")],
            [10, 20]);
        Assert.Equal(0, extras);
        profile = new ProfileStore().TryRead(fx.ManagerData, "default")!;
        Assert.Equal(new[] { "PackA", "LaterPack" }, profile.Enabled.OrderBy(item => item.Priority).Select(item => item.ModKey).ToArray());
        Assert.DoesNotContain(profile.Enabled, item => item.ModKey == "Reticule");
    }

    [Fact]
    public void ApplyPackLoadOrder_DropsRemovedPackMod_KeepsDisabledManual()
    {
        using var fx = new DeployFixture();
        fx.PutMod("PackA", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/PackA/a.dll"] = "a" });
        fx.PutMod("PackB", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/PackB/b.dll"] = "b" });
        fx.PutMod("Reticule", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/Reticule/r.dll"] = "r" });
        WriteForgeId(fx, "PackA", "1.0", 10);
        WriteForgeId(fx, "PackB", "1.0", 11);

        InstallInventory.ApplyPackLoadOrder(fx.ManagerData, "default", [("PackA", "1.0"), ("PackB", "1.0")], [10, 11]);
        InstallInventory.AddToLoadOrder(fx.ManagerData, "default", "Reticule", "1.0");
        InstallInventory.SetEnabled(fx.ManagerData, "default", "Reticule", "1.0", enabled: false);

        InstallInventory.ApplyPackLoadOrder(fx.ManagerData, "default", [("PackA", "1.0")], [10]);
        var profile = new ProfileStore().TryRead(fx.ManagerData, "default")!;
        Assert.Equal(new[] { "PackA", "Reticule" }, profile.Enabled.OrderBy(item => item.Priority).Select(item => item.ModKey).ToArray());
        Assert.False(profile.Enabled.Single(item => item.ModKey == "Reticule").IsOn);
        Assert.DoesNotContain(profile.Enabled, item => item.ModKey == "PackB");
    }

    private static void WriteForgeId(DeployFixture fx, string key, string version, int forgeId)
    {
        var document = ModStore.TryRead(fx.ManagerData, key, version)!;
        var updated = new ModDocument
        {
            ModKey = document.ModKey,
            DisplayName = document.DisplayName,
            Version = document.Version,
            Kind = document.Kind,
            Deployable = document.Deployable,
            ForgeModId = forgeId,
            Files = document.Files,
            ImportedAtUtc = document.ImportedAtUtc
        };
        File.WriteAllText(
            Path.Combine(ModStore.PackageDirectory(fx.ManagerData, key, version), "mod.json"),
            JsonSerializer.Serialize(updated, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public void MovePriority_WorksWhileDisabled()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0, Enabled = true },
            new EnabledMod { ModKey = "B", Version = "1.0", Priority = 1, Enabled = false }
        ]);

        InstallInventory.MovePriority(fx.ManagerData, "default", "B", "1.0", delta: -1);
        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.Equal(0, snap.Items.Single(item => item.Key == "B").Priority);
        Assert.False(snap.Items.Single(item => item.Key == "B").Enabled);
        Assert.Equal(1, snap.Items.Single(item => item.Key == "A").Priority);
    }

    [Fact]
    public void MoveTo_InsertsAfterTarget()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        fx.PutMod("C", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/C/c.dll"] = "c" });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0, Enabled = true },
            new EnabledMod { ModKey = "B", Version = "1.0", Priority = 1, Enabled = false },
            new EnabledMod { ModKey = "C", Version = "1.0", Priority = 2, Enabled = true }
        ]);

        InstallInventory.MoveTo(fx.ManagerData, "default", "A", "1.0", "C", "1.0", after: true);
        var ordered = new ProfileStore().TryRead(fx.ManagerData, "default")!.Enabled
            .OrderBy(item => item.Priority)
            .Select(item => item.ModKey)
            .ToArray();
        Assert.Equal(new[] { "B", "C", "A" }, ordered);
        Assert.False(new ProfileStore().TryRead(fx.ManagerData, "default")!.Enabled.Single(item => item.ModKey == "B").IsOn);
    }

    [Fact]
    public void MoveTo_InsertsBeforeTarget()
    {
        using var fx = new DeployFixture();
        fx.PutMod("A", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/A/a.dll"] = "a" });
        fx.PutMod("B", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/B/b.dll"] = "b" });
        fx.PutMod("C", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/C/c.dll"] = "c" });
        new ProfileStore().Save(fx.ManagerData, "default",
        [
            new EnabledMod { ModKey = "A", Version = "1.0", Priority = 0, Enabled = true },
            new EnabledMod { ModKey = "B", Version = "1.0", Priority = 1, Enabled = true },
            new EnabledMod { ModKey = "C", Version = "1.0", Priority = 2, Enabled = true }
        ]);

        InstallInventory.MoveTo(fx.ManagerData, "default", "C", "1.0", "A", "1.0", after: false);
        var ordered = new ProfileStore().TryRead(fx.ManagerData, "default")!.Enabled
            .OrderBy(item => item.Priority)
            .Select(item => item.ModKey)
            .ToArray();
        Assert.Equal(new[] { "C", "A", "B" }, ordered);
    }

    [Fact]
    public void Scan_SkipsUnreadableStorePackage()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string> { ["BepInEx/plugins/Keep/k.dll"] = "k" });
        var bad = ModStore.PackageDirectory(fx.ManagerData, "Broken", "1.0");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "mod.json"), "{not-json");

        var snap = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, "default");
        Assert.Contains(snap.Items, item => item.Key == "Keep");
        Assert.DoesNotContain(snap.Items, item => item.Key == "Broken");
    }
}
