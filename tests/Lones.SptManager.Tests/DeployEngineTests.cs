using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Tests;

public sealed class DeployEngineTests
{
    [Fact]
    public void Deploy_HybridMod_JunctionsUserModsAndPluginDir()
    {
        using var fx = new DeployFixture();
        fx.PutMod("TrashTalk", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/TrashTalk/mod.dll"] = "server",
            ["BepInEx/plugins/TrashTalk/plugin.dll"] = "client"
        });

        var result = fx.Engine.Deploy(fx.Request());
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install(SptLayout.UserMods)));
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/TrashTalk")));
        Assert.False(NtfsLinks.IsJunction(fx.Install(SptLayout.BepInExPluginsSpt)));
        Assert.Equal("server", File.ReadAllText(fx.Install(SptLayout.UserMods + "/TrashTalk/mod.dll")));
        Assert.Equal("client", File.ReadAllText(fx.Install("BepInEx/plugins/TrashTalk/plugin.dll")));
        Assert.Equal("spt-core", File.ReadAllText(fx.Install(SptLayout.BepInExPluginsSpt + "/spt-core.dll")));
        Assert.True(Directory.Exists(fx.Install(SptLayout.UserProfiles)));
        Assert.False(File.Exists(ProfilePaths.Journal(fx.ManagerData, ProfilePaths.DefaultProfileId)));
        Assert.True(File.Exists(ProfilePaths.Manifest(fx.ManagerData, ProfilePaths.DefaultProfileId)));
        Assert.True(File.Exists(ProfilePaths.DeployLog(fx.ManagerData, ProfilePaths.DefaultProfileId)));
        Assert.True(File.Exists(ProfilePaths.DeployHumanLog(fx.ManagerData, ProfilePaths.DefaultProfileId)));
        Assert.Contains("Deployed", File.ReadAllText(ProfilePaths.DeployHumanLog(fx.ManagerData, ProfilePaths.DefaultProfileId)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deploy_RootLeftoversAndReshade_AreCopiedToGameRoot()
    {
        using var fx = new DeployFixture();
        fx.PutMod("DynamicMaps", "1.2.1", new Dictionary<string, string>
        {
            ["BepInEx/plugins/mpstark-dynamicmaps/DynamicMaps.dll"] = "client",
            ["SPT_Runtime/user/mods/mpstark-dynamicmaps/mod.dll"] = "server",
            ["EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll"] = "unity-vg"
        });
        fx.PutMod("Sharper", "1.1.3", new Dictionary<string, string>
        {
            ["dxgi.dll"] = "reshade-dll",
            ["ReShade.ini"] = "ini",
            ["reshade-shaders/Shaders/CAS.fx"] = "fx"
        });

        var result = fx.Engine.Deploy(fx.Request(fx.Enable(("DynamicMaps", "1.2.1", 0), ("Sharper", "1.1.3", 1))));
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/mpstark-dynamicmaps")));
        Assert.Equal("unity-vg", File.ReadAllText(fx.Install("EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll")));
        Assert.Equal("reshade-dll", File.ReadAllText(fx.Install("dxgi.dll")));
        Assert.Equal("ini", File.ReadAllText(fx.Install("ReShade.ini")));
        Assert.Equal("fx", File.ReadAllText(fx.Install("reshade-shaders/Shaders/CAS.fx")));
        Assert.False(NtfsLinks.IsJunction(fx.Install("EscapeFromTarkov_Data")));
        Assert.False(Directory.Exists(fx.Install("BepInEx/plugins/dxgi")));
        Assert.False(new FileInfo(fx.Install("ReShade.ini")).IsReadOnly);

        var disabled = fx.Engine.Deploy(fx.Request(fx.Enable(("DynamicMaps", "1.2.1", 0))));
        Assert.Equal(DeployStatus.Success, disabled.Status);
        Assert.False(File.Exists(fx.Install("dxgi.dll")));
        Assert.False(Directory.Exists(fx.Install("reshade-shaders")));
        Assert.True(File.Exists(fx.Install("EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll")));
    }

    [Fact]
    public void Redeploy_PromotesReshade2IniTutorial()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Sharper", "1.1.3", new Dictionary<string, string>
        {
            ["dxgi.dll"] = "reshade-dll",
            ["ReShade.ini"] = "[OVERLAY]\nTutorialProgress=0\n"
        });

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request()).Status);
        File.WriteAllText(fx.Install("ReShade2.ini"), "[OVERLAY]\nTutorialProgress=4\n");

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request()).Status);
        Assert.Contains("TutorialProgress=4", File.ReadAllText(fx.Install("ReShade.ini")));
        Assert.False(new FileInfo(fx.Install("ReShade.ini")).IsReadOnly);
    }

    [Fact]
    public void Redeploy_Unchanged_IsIdempotent()
    {
        using var fx = new DeployFixture();
        fx.PutMod("TrashTalk", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/TrashTalk/plugin.dll"] = "client"
        });

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request()).Status);
        var firstTarget = NtfsLinks.TryGetJunctionTarget(fx.Install("BepInEx/plugins/TrashTalk"));
        var second = fx.Engine.Deploy(fx.Request());
        Assert.Equal(DeployStatus.Idempotent, second.Status);
        Assert.True(SafeFileSystem.SamePath(
            firstTarget!,
            NtfsLinks.TryGetJunctionTarget(fx.Install("BepInEx/plugins/TrashTalk"))!));
        Assert.False(File.Exists(ProfilePaths.Journal(fx.ManagerData, ProfilePaths.DefaultProfileId)));
    }

    [Fact]
    public void DisableMod_RemovesOverlay_KeepsStoreAndSptOwned()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        fx.PutMod("Drop", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Drop/drop.dll"] = "drop",
            ["SPT_Runtime/user/mods/Drop/mod.dll"] = "drop-server"
        });

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Keep", "1.0", 0), ("Drop", "1.0", 1)))).Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/Drop")));

        var disabled = fx.Engine.Deploy(fx.Request(fx.Enable(("Keep", "1.0", 0))));
        Assert.Equal(DeployStatus.Success, disabled.Status);
        Assert.False(Directory.Exists(fx.Install("BepInEx/plugins/Drop")));
        Assert.False(NtfsLinks.IsJunction(fx.Install(SptLayout.UserMods)));
        Assert.True(File.Exists(fx.Install("BepInEx/plugins/Keep/keep.dll")));
        Assert.True(File.Exists(Path.Combine(ModStore.FilesDirectory(fx.ManagerData, "Drop", "1.0"), "BepInEx", "plugins", "Drop", "drop.dll")));
        Assert.Equal("spt-core", File.ReadAllText(fx.Install(SptLayout.BepInExPluginsSpt + "/spt-core.dll")));
        Assert.True(Directory.Exists(fx.Install(SptLayout.UserMods)));
        Assert.False(Directory.Exists(fx.Install(SptLayout.UserMods + "/Drop")));
    }

    [Fact]
    public void DisableServerMod_SkipsOverwriteLeftovers_AndRemovesFolder()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/Keep/mod.dll"] = "keep"
        });
        fx.PutMod("WeekendDrops", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/WeekendDrops/package.json"] = "{ }",
            ["SPT_Runtime/user/mods/WeekendDrops/mod.dll"] = "drops"
        });

        Assert.Equal(
            DeployStatus.Success,
            fx.Engine.Deploy(fx.Request(fx.Enable(("Keep", "1.0", 0), ("WeekendDrops", "1.0", 1)))).Status);

        var leftover = GamePath.Combine(
            ProfilePaths.Overwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            "SPT_Runtime/user/mods/WeekendDrops/state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(leftover)!);
        File.WriteAllText(leftover, "{ }");

        var disabled = fx.Engine.Deploy(fx.Request(fx.Enable(("Keep", "1.0", 0))));
        Assert.Equal(DeployStatus.Success, disabled.Status);
        Assert.False(Directory.Exists(fx.Install(SptLayout.UserMods + "/WeekendDrops")));
        Assert.True(File.Exists(fx.Install(SptLayout.UserMods + "/Keep/mod.dll")));
        Assert.Contains(disabled.Warnings, text => text.Contains("WeekendDrops", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DisableLastServerMod_RemovesLeftoverEmptyUserModsFolder()
    {
        using var fx = new DeployFixture();
        fx.PutMod("WeekendDrops", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/WeekendDrops/package.json"] = "{ }"
        });
        Directory.CreateDirectory(fx.Install(SptLayout.UserMods + "/WeekendDrops"));
        Directory.CreateDirectory(fx.Install(SptLayout.UserMods + "/Hollow/nested"));

        var result = fx.Engine.Deploy(fx.Request([]));
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.False(NtfsLinks.IsJunction(fx.Install(SptLayout.UserMods)));
        Assert.True(Directory.Exists(fx.Install(SptLayout.UserMods)));
        Assert.False(Directory.Exists(fx.Install(SptLayout.UserMods + "/WeekendDrops")));
        Assert.False(Directory.Exists(fx.Install(SptLayout.UserMods + "/Hollow")));
    }

    [Fact]
    public void EmptySavedProfile_DeploysNoStoreMods()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        new ProfileStore().Save(fx.ManagerData, ProfilePaths.DefaultProfileId, []);

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request()).Status);
        Assert.False(Directory.Exists(fx.Install("BepInEx/plugins/Keep")));
    }

    [Fact]
    public void Deploy_BlockedWhileSptProcessReported()
    {
        using var fx = new DeployFixture();
        fx.Lock.Running.Add("SPT.Server");
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });

        var result = fx.Engine.Deploy(fx.Request());
        Assert.Equal(DeployStatus.BlockedProcesses, result.Status);
        Assert.Contains("SPT.Server", result.RunningProcesses);
        Assert.False(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/Keep")));
    }

    [Fact]
    public void Reconcile_MidDeployJournal_RestoresLastManifest_RemovesOrphan()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request()).Status);

        var staging = ProfilePaths.Staging(fx.ManagerData, ProfilePaths.DefaultProfileId);
        var orphanTarget = Path.Combine(staging, "orphan-target");
        Directory.CreateDirectory(orphanTarget);
        File.WriteAllText(Path.Combine(orphanTarget, "x.txt"), "orphan");
        var orphanLink = fx.Install("BepInEx/plugins/Orphan");
        NtfsLinks.CreateJunction(orphanLink, orphanTarget);

        var journal = new DeployManifest
        {
            ProfileId = ProfilePaths.DefaultProfileId,
            GameRoot = fx.GameRoot,
            Fingerprint = "in-flight",
            WrittenAtUtc = DateTimeOffset.UtcNow,
            Enabled = fx.Enable(("Keep", "1.0", 0)),
            Junctions =
            [
                new JunctionRecord
                {
                    InstallRelative = "BepInEx/plugins/Orphan",
                    StagingRelative = "orphan-target",
                    TargetFull = orphanTarget
                }
            ]
        };
        File.WriteAllText(
            ProfilePaths.Journal(fx.ManagerData, ProfilePaths.DefaultProfileId),
            JsonSerializer.Serialize(journal, DeployFixture.JsonOptions));

        var recovered = fx.Engine.Reconcile(fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Recovered, recovered.Status);
        Assert.False(File.Exists(ProfilePaths.Journal(fx.ManagerData, ProfilePaths.DefaultProfileId)));
        Assert.False(Directory.Exists(orphanLink));
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/Keep")));
        Assert.Equal("keep", File.ReadAllText(fx.Install("BepInEx/plugins/Keep/keep.dll")));
        Assert.Equal("spt-core", File.ReadAllText(fx.Install(SptLayout.BepInExPluginsSpt + "/spt-core.dll")));
    }

    [Fact]
    public void Reconcile_JournalWithoutManifest_PurgesOrphans()
    {
        using var fx = new DeployFixture();
        var staging = ProfilePaths.Staging(fx.ManagerData, ProfilePaths.DefaultProfileId);
        Directory.CreateDirectory(staging);
        var target = Path.Combine(staging, "mods");
        Directory.CreateDirectory(target);
        var mods = fx.Install(SptLayout.UserMods);
        Directory.CreateDirectory(Path.GetDirectoryName(mods)!);
        NtfsLinks.CreateJunction(mods, target);

        File.WriteAllText(
            ProfilePaths.Journal(fx.ManagerData, ProfilePaths.DefaultProfileId),
            JsonSerializer.Serialize(new DeployManifest
            {
                ProfileId = ProfilePaths.DefaultProfileId,
                GameRoot = fx.GameRoot,
                Fingerprint = "crash",
                WrittenAtUtc = DateTimeOffset.UtcNow,
                Junctions =
                [
                    new JunctionRecord
                    {
                        InstallRelative = SptLayout.UserMods,
                        StagingRelative = SptLayout.UserMods,
                        TargetFull = target
                    }
                ]
            }, DeployFixture.JsonOptions));

        var recovered = fx.Engine.Reconcile(fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Recovered, recovered.Status);
        Assert.False(NtfsLinks.IsJunction(mods));
        Assert.True(Directory.Exists(mods));
        Assert.False(File.Exists(ProfilePaths.Journal(fx.ManagerData, ProfilePaths.DefaultProfileId)));
    }

    [Fact]
    public void Overlay_HigherPriorityWins()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Low", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/Shared/file.txt"] = "low",
            ["SPT_Runtime/user/mods/Low/only.txt"] = "low-only"
        });
        fx.PutMod("High", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/Shared/file.txt"] = "high"
        });

        var result = fx.Engine.Deploy(fx.Request(fx.Enable(("Low", "1.0", 0), ("High", "1.0", 1))));
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.Equal("high", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Shared/file.txt")));
        Assert.Equal("low-only", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Low/only.txt")));
        Assert.Contains(result.Conflicts, item => item.CanonicalPath == "SPT_Runtime/user/mods/Shared/file.txt" && item.WinnerModKey == "High");
    }

    [Fact]
    public void NonEmptyUserMods_ThatAreNotOurJunction_Fails()
    {
        using var fx = new DeployFixture();
        Directory.CreateDirectory(fx.Install(SptLayout.UserMods + "/Leftover"));
        File.WriteAllText(fx.Install(SptLayout.UserMods + "/Leftover/x.txt"), "nope");
        fx.PutMod("Talk", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/Talk/mod.dll"] = "talk"
        });

        var result = fx.Engine.Deploy(fx.Request());
        Assert.Equal(DeployStatus.Failed, result.Status);
        Assert.Contains("empty or already a manager junction", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Import the leftover", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(NtfsLinks.IsJunction(fx.Install(SptLayout.UserMods)));
        Assert.Equal("nope", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Leftover/x.txt")));
    }

    [Fact]
    public void LoosePluginDll_IsWrappedAndJunctioned()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Cool", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/CoolMod.dll"] = "dll"
        });

        var result = fx.Engine.Deploy(fx.Request());
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/CoolMod")));
        Assert.False(NtfsLinks.IsJunction(fx.Install(OverlayPlanner.BepInExPlugins)));
        Assert.False(File.Exists(fx.Install("BepInEx/plugins/CoolMod.dll")));
        Assert.Equal("dll", File.ReadAllText(fx.Install("BepInEx/plugins/CoolMod/CoolMod.dll")));
        Assert.Equal("spt-core", File.ReadAllText(fx.Install(SptLayout.BepInExPluginsSpt + "/spt-core.dll")));
    }

    [Fact]
    public void LeftoverPluginFolder_IsClaimedAndJunctioned()
    {
        using var fx = new DeployFixture();
        Directory.CreateDirectory(fx.Install("BepInEx/plugins/Keep"));
        File.WriteAllText(fx.Install("BepInEx/plugins/Keep/old.dll"), "leftover");
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });

        var result = fx.Engine.Deploy(fx.Request());
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.True(NtfsLinks.IsJunction(fx.Install("BepInEx/plugins/Keep")));
        Assert.Equal("keep", File.ReadAllText(fx.Install("BepInEx/plugins/Keep/keep.dll")));
        Assert.Equal("leftover", File.ReadAllText(fx.Install("BepInEx/plugins/Keep/old.dll")));
        Assert.False(NtfsLinks.IsJunction(fx.Install(SptLayout.BepInExPluginsSpt)));
    }

    [Fact]
    public void DenylistFileInStore_IsNotStaged()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Evil", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Evil/ok.dll"] = "ok",
            ["BepInEx/plugins/spt/spt-core.dll"] = "evil"
        });

        var result = fx.Engine.Deploy(fx.Request());
        Assert.Equal(DeployStatus.Success, result.Status);
        Assert.Equal("ok", File.ReadAllText(fx.Install("BepInEx/plugins/Evil/ok.dll")));
        Assert.Equal("spt-core", File.ReadAllText(fx.Install(SptLayout.BepInExPluginsSpt + "/spt-core.dll")));
        Assert.False(File.Exists(Path.Combine(ProfilePaths.Staging(fx.ManagerData, ProfilePaths.DefaultProfileId), "BepInEx", "plugins", "spt", "spt-core.dll")));
    }
}

internal sealed class StubProcessLock : IProcessLock
{
    public List<string> Running { get; } = [];

    public IReadOnlyList<string> RunningSptProcesses() => Running;
}

internal sealed class DeployFixture : IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Root { get; }
    public string GameRoot { get; }
    public string ManagerData { get; }
    public StubProcessLock Lock { get; } = new();
    public DeployEngine Engine { get; }

    public DeployFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "lones-deploy-" + Guid.NewGuid().ToString("N"));
        GameRoot = Path.Combine(Root, "game");
        ManagerData = Path.Combine(Root, "manager");
        Directory.CreateDirectory(ManagerData);
        GameRootFixture.Create41Layout(GameRoot);
        Engine = new DeployEngine(Lock);
    }

    public DeployRequest Request(IReadOnlyList<EnabledMod>? enabled = null, string profileId = ProfilePaths.DefaultProfileId)
        => new()
        {
            GameRoot = GameRoot,
            ManagerData = ManagerData,
            ProfileId = profileId,
            Enabled = enabled,
            Baseline = new SptOwnedBaselineBuilder().Build(GameRoot)
        };

    public IReadOnlyList<EnabledMod> Enable(params (string Key, string Version, int Priority)[] mods)
        => mods.Select(mod => new EnabledMod { ModKey = mod.Key, Version = mod.Version, Priority = mod.Priority }).ToArray();

    public string Install(string relative) => GamePath.Combine(GameRoot, relative);

    public void PutMod(string key, string version, Dictionary<string, string> files)
    {
        var filesDir = ModStore.FilesDirectory(ManagerData, key, version);
        var records = new List<ModFileRecord>();
        foreach (var (canonical, content) in files)
        {
            var dest = GamePath.Combine(filesDir, canonical);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, content);
            records.Add(new ModFileRecord
            {
                CanonicalPath = GamePath.Normalize(canonical),
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest)))
            });
        }

        var document = new ModDocument
        {
            ModKey = key,
            Version = version,
            Kind = "Hybrid",
            Deployable = true,
            Files = records,
            ImportedAtUtc = DateTimeOffset.UtcNow
        };
        Directory.CreateDirectory(ModStore.PackageDirectory(ManagerData, key, version));
        File.WriteAllText(
            Path.Combine(ModStore.PackageDirectory(ManagerData, key, version), "mod.json"),
            JsonSerializer.Serialize(document, JsonOptions));
    }

    public void Dispose()
    {
        try
        {
            SafeFileSystem.DeleteDirectoryNoFollow(Root);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
    }
}
