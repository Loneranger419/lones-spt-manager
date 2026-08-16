using System.Text.Json;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Tests;

public sealed class ProfileHarvestTests
{
    [Fact]
    public void ProfileSwitch_HidesOtherProfileSavesAndConfigs()
    {
        using var fx = new DeployFixture();
        WriteInstall(fx, SptLayout.BepInExConfig + "/BepInEx.cfg", "stock");

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([], "alpha")).Status);
        File.WriteAllText(fx.Install(SptLayout.UserProfiles + "/alpha.json"), "save-a");
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/com.alpha.cfg"), "cfg-a");

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([], "beta")).Status);
        File.WriteAllText(fx.Install(SptLayout.UserProfiles + "/beta.json"), "save-b");
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/com.beta.cfg"), "cfg-b");

        Assert.False(File.Exists(fx.Install(SptLayout.UserProfiles + "/alpha.json")));
        Assert.False(File.Exists(fx.Install(SptLayout.BepInExConfig + "/com.alpha.cfg")));
        Assert.Equal("save-b", File.ReadAllText(fx.Install(SptLayout.UserProfiles + "/beta.json")));
        Assert.Equal("cfg-b", File.ReadAllText(fx.Install(SptLayout.BepInExConfig + "/com.beta.cfg")));
        Assert.Equal("save-a", File.ReadAllText(Path.Combine(ProfilePaths.Saves(fx.ManagerData, "alpha"), "alpha.json")));
        Assert.Equal("cfg-a", File.ReadAllText(Path.Combine(ProfilePaths.BepInExConfig(fx.ManagerData, "alpha"), "com.alpha.cfg")));
        Assert.True(NtfsLinks.IsJunction(fx.Install(SptLayout.UserProfiles)));
        Assert.True(SafeFileSystem.SamePath(
            NtfsLinks.TryGetJunctionTarget(fx.Install(SptLayout.UserProfiles))!,
            ProfilePaths.Saves(fx.ManagerData, "beta")));
    }

    [Fact]
    public void Harvest_NewConfigCfg_GoesToOverwrite()
    {
        using var fx = new DeployFixture();
        WriteInstall(fx, SptLayout.BepInExConfig + "/BepInEx.cfg", "stock");
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([])).Status);

        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/com.example.mod.cfg"), "f12");
        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Success, harvest.Status);
        Assert.Contains(harvest.Files, file => file.CanonicalPath == "BepInEx/config/com.example.mod.cfg");
        Assert.Equal(
            "f12",
            File.ReadAllText(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, ProfilePaths.DefaultProfileId), "BepInEx/config/com.example.mod.cfg")));
        Assert.DoesNotContain(harvest.Files, file => file.CanonicalPath.Equals("BepInEx/config/BepInEx.cfg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CopyProfile_CopiesScopedTreesAndEnabledList()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Keep", "1.0", 0)), "alpha")).Status);
        File.WriteAllText(fx.Install(SptLayout.UserProfiles + "/alpha.json"), "save-a");
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/com.alpha.cfg"), "cfg-a");
        Directory.CreateDirectory(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, "alpha"), "BepInEx/config"));
        File.WriteAllText(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, "alpha"), "BepInEx/config/extra.cfg"), "ow");

        var copied = ProfileCopier.Copy(fx.ManagerData, "alpha", "copied");
        Assert.True(copied.Success);
        Assert.Equal("save-a", File.ReadAllText(Path.Combine(ProfilePaths.Saves(fx.ManagerData, "copied"), "alpha.json")));
        Assert.Equal("cfg-a", File.ReadAllText(Path.Combine(ProfilePaths.BepInExConfig(fx.ManagerData, "copied"), "com.alpha.cfg")));
        Assert.Equal("ow", File.ReadAllText(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, "copied"), "BepInEx/config/extra.cfg")));
        var dest = new ProfileStore().LoadOrCreate(fx.ManagerData, "copied");
        Assert.Contains(dest.Enabled, mod => mod.ModKey == "Keep");
        Assert.False(Directory.Exists(ProfilePaths.Staging(fx.ManagerData, "copied")));
    }

    [Fact]
    public void LastUsedProfile_RemembersAndFallsBackToNewestActivity()
    {
        using var fx = new DeployFixture();
        var store = new ProfileStore();
        store.LoadOrCreate(fx.ManagerData, "older");
        store.LoadOrCreate(fx.ManagerData, "newer");
        File.WriteAllText(
            ProfilePaths.ProfileJson(fx.ManagerData, "older"),
            JsonSerializer.Serialize(new ProfileDocument
            {
                ProfileId = "older",
                UpdatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
            }, DeployFixture.JsonOptions));
        File.WriteAllText(
            ProfilePaths.Manifest(fx.ManagerData, "older"),
            JsonSerializer.Serialize(new DeployManifest
            {
                ProfileId = "older",
                GameRoot = fx.GameRoot,
                Fingerprint = "old",
                WrittenAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
            }, DeployFixture.JsonOptions));
        File.WriteAllText(
            ProfilePaths.Manifest(fx.ManagerData, "newer"),
            JsonSerializer.Serialize(new DeployManifest
            {
                ProfileId = "newer",
                GameRoot = fx.GameRoot,
                Fingerprint = "new",
                WrittenAtUtc = DateTimeOffset.UtcNow
            }, DeployFixture.JsonOptions));

        Assert.Equal("newer", ProfileStore.TryLastUsedProfileId(fx.ManagerData));

        ProfileStore.RememberLastProfile(fx.ManagerData, "older");
        Assert.Equal("older", ProfileStore.TryLastUsedProfileId(fx.ManagerData));

        var renamed = ProfileStore.Rename(fx.ManagerData, "older", "renamed");
        Assert.True(renamed.Success);
        Assert.Equal("renamed", ProfileStore.TryLastUsedProfileId(fx.ManagerData));

        ProfileStore.Delete(fx.ManagerData, "renamed");
        Assert.Equal("newer", ProfileStore.TryLastUsedProfileId(fx.ManagerData));
    }

    [Fact]
    public void PackSource_IsSavedPreservedOnRenameAndCopied()
    {
        using var fx = new DeployFixture();
        var store = new ProfileStore();
        store.Save(
            fx.ManagerData,
            "alpha",
            [],
            packSource: "https://campdegen.com/spt-pack/data/mods.json");
        Assert.Equal(
            "https://campdegen.com/spt-pack/data/mods.json",
            store.TryRead(fx.ManagerData, "alpha")!.PackSource);

        store.Save(fx.ManagerData, "alpha", [new EnabledMod { ModKey = "Keep", Version = "1.0", Priority = 0 }]);
        Assert.Equal(
            "https://campdegen.com/spt-pack/data/mods.json",
            store.TryRead(fx.ManagerData, "alpha")!.PackSource);

        var renamed = ProfileStore.Rename(fx.ManagerData, "alpha", "renamed");
        Assert.True(renamed.Success);
        Assert.Equal(
            "https://campdegen.com/spt-pack/data/mods.json",
            store.TryRead(fx.ManagerData, "renamed")!.PackSource);

        var copied = ProfileCopier.Copy(fx.ManagerData, "renamed", "copy");
        Assert.True(copied.Success);
        Assert.Equal(
            "https://campdegen.com/spt-pack/data/mods.json",
            store.TryRead(fx.ManagerData, "copy")!.PackSource);
    }

    [Fact]
    public void CopyProfile_CanCopyOnlySaves()
    {
        using var fx = new DeployFixture();
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([], "alpha")).Status);
        File.WriteAllText(fx.Install(SptLayout.UserProfiles + "/alpha.json"), "save-a");
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/com.alpha.cfg"), "cfg-a");
        Directory.CreateDirectory(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, "alpha"), "BepInEx/config"));
        File.WriteAllText(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, "alpha"), "BepInEx/config/extra.cfg"), "ow");

        var copied = ProfileCopier.Copy(fx.ManagerData, "alpha", "saves-only", new ProfileCopyOptions { Saves = true });
        Assert.True(copied.Success);
        Assert.Equal("save-a", File.ReadAllText(Path.Combine(ProfilePaths.Saves(fx.ManagerData, "saves-only"), "alpha.json")));
        Assert.False(File.Exists(Path.Combine(ProfilePaths.BepInExConfig(fx.ManagerData, "saves-only"), "com.alpha.cfg")));
        Assert.False(File.Exists(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, "saves-only"), "BepInEx/config/extra.cfg")));
        Assert.Empty(new ProfileStore().LoadOrCreate(fx.ManagerData, "saves-only").Enabled);
    }

    [Fact]
    public void Harvest_FikaGeneratedFiles_ArePerProfile()
    {
        using var fx = new DeployFixture();
        fx.PutMod("fika-server", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/fika-server/FikaServer.dll"] = "server"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("fika-server", "1.0", 0)), "alpha")).Status);
        WriteInstall(fx, SptLayout.UserMods + "/fika-server/assets/configs/fika.jsonc", "{ \"alpha\": true }");
        Assert.Equal(DeployStatus.Success, new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, "alpha").Status);

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("fika-server", "1.0", 0)), "beta")).Status);
        Assert.False(File.Exists(fx.Install(SptLayout.UserMods + "/fika-server/assets/configs/fika.jsonc")));

        var copied = ProfileCopier.CopyRuntimeMod(fx.ManagerData, "alpha", "beta", "fika-server");
        Assert.True(copied.Success);
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("fika-server", "1.0", 0)), "beta")).Status);
        Assert.Equal("{ \"alpha\": true }", File.ReadAllText(fx.Install(SptLayout.UserMods + "/fika-server/assets/configs/fika.jsonc")));
    }

    [Fact]
    public void RenameProfile_MovesScopedFolderAndUpdatesId()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Keep", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Keep/keep.dll"] = "keep"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Keep", "1.0", 0)), "alpha")).Status);
        File.WriteAllText(fx.Install(SptLayout.UserProfiles + "/alpha.json"), "save-a");

        var renamed = ProfileStore.Rename(fx.ManagerData, "alpha", "renamed");
        Assert.True(renamed.Success);
        Assert.Equal("renamed", renamed.DestinationId);
        Assert.False(Directory.Exists(ProfilePaths.ProfileRoot(fx.ManagerData, "alpha")));
        Assert.Equal("save-a", File.ReadAllText(Path.Combine(ProfilePaths.Saves(fx.ManagerData, "renamed"), "alpha.json")));
        var dest = new ProfileStore().LoadOrCreate(fx.ManagerData, "renamed");
        Assert.Equal("renamed", dest.ProfileId);
        Assert.Contains(dest.Enabled, mod => mod.ModKey == "Keep");
    }

    [Fact]
    public void Delete_RemovesProfileButKeepsLast()
    {
        using var fx = new DeployFixture();
        new ProfileStore().LoadOrCreate(fx.ManagerData, "keep");
        new ProfileStore().LoadOrCreate(fx.ManagerData, "drop");

        var deleted = ProfileStore.Delete(fx.ManagerData, "drop");
        Assert.True(deleted.Success, deleted.Message);
        Assert.False(Directory.Exists(ProfilePaths.ProfileRoot(fx.ManagerData, "drop")));
        Assert.True(Directory.Exists(ProfilePaths.ProfileRoot(fx.ManagerData, "keep")));

        var last = ProfileStore.Delete(fx.ManagerData, "keep");
        Assert.False(last.Success);
        Assert.Contains("last profile", last.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(ProfilePaths.ProfileRoot(fx.ManagerData, "keep")));
    }

    [Fact]
    public void Overwrite_MergesOnNextDeploy()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Talk", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/Talk/mod.dll"] = "talk"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Talk", "1.0", 0)))).Status);
        File.WriteAllText(fx.Install(SptLayout.UserMods + "/Talk/generated.json"), "session");

        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Contains(harvest.Files, file => file.CanonicalPath == "SPT_Runtime/user/mods/Talk/generated.json");

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Talk", "1.0", 0)))).Status);
        Assert.Equal("session", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Talk/generated.json")));
        Assert.Equal("talk", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Talk/mod.dll")));
    }

    [Fact]
    public void Harvest_SkipsSptOwnedAndSecrets()
    {
        using var fx = new DeployFixture();
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([])).Status);
        File.WriteAllText(fx.Install(SptLayout.BepInExPluginsSpt + "/spt-core.dll"), "mutated");
        File.WriteAllText(fx.Install(SptLayout.UserCredentials + "/credentials.json"), "secret");
        File.WriteAllText(Path.Combine(ProfilePaths.BepInExConfig(fx.ManagerData, ProfilePaths.DefaultProfileId), "server.key"), "nope");

        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Success, harvest.Status);
        Assert.DoesNotContain(harvest.Files, file => file.CanonicalPath.Contains("spt-core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(harvest.Files, file => file.CanonicalPath.Contains("credentials", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(harvest.Files, file => file.CanonicalPath.Contains("server.key", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, ProfilePaths.DefaultProfileId), "server.key")));
    }

    [Fact]
    public void Harvest_MutatedBepInExCfg_IsNotOverwrite()
    {
        using var fx = new DeployFixture();
        WriteInstall(fx, SptLayout.BepInExConfig + "/BepInEx.cfg", "stock");
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([])).Status);
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/BepInEx.cfg"), "mutated");

        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Success, harvest.Status);
        Assert.DoesNotContain(harvest.Files, file => file.CanonicalPath.Equals("BepInEx/config/BepInEx.cfg", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GamePath.Combine(ProfilePaths.Overwrite(fx.ManagerData, ProfilePaths.DefaultProfileId), "BepInEx/config/BepInEx.cfg")));
    }

    [Fact]
    public void Harvest_ConfigurationManagerCfg_StaysOnProfile()
    {
        using var fx = new DeployFixture();
        WriteInstall(fx, SptLayout.BepInExConfig + "/BepInEx.cfg", "stock");
        WriteInstall(fx, SptLayout.BepInExConfig + "/com.bepis.bepinex.configurationmanager.cfg", "stock-cm");
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([])).Status);
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/com.bepis.bepinex.configurationmanager.cfg"), "hotkey");

        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Success, harvest.Status);
        Assert.DoesNotContain(
            harvest.Files,
            file => file.CanonicalPath.Equals("BepInEx/config/com.bepis.bepinex.configurationmanager.cfg", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GamePath.Combine(
            ProfilePaths.Overwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            "BepInEx/config/com.bepis.bepinex.configurationmanager.cfg")));
        Assert.Equal("hotkey", File.ReadAllText(fx.Install(SptLayout.BepInExConfig + "/com.bepis.bepinex.configurationmanager.cfg")));
    }

    [Fact]
    public void Harvest_ServerModConfig_GoesToProfileRuntime_GeneratedStaysInOverwrite()
    {
        using var fx = new DeployFixture();
        fx.PutMod("APBS - Acid's Progressive Bot System", "2.3.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/mod.dll"] = "apbs"
        });
        Assert.Equal(
            DeployStatus.Success,
            fx.Engine.Deploy(fx.Request(fx.Enable(("APBS - Acid's Progressive Bot System", "2.3.0", 0)))).Status);

        WriteInstall(fx, SptLayout.UserMods + "/acidphantasm-progressivebotsystem/config.json", "{ \"pin\": true }");
        WriteInstall(fx, SptLayout.UserMods + "/acidphantasm-progressivebotsystem/blacklists.json", "[]");
        WriteInstall(
            fx,
            SptLayout.UserMods + "/acidphantasm-progressivebotsystem/GeneratedVanillaMappings-DO_NOT_TOUCH/ArmorVest.json",
            "{}");
        WriteInstall(fx, SptLayout.UserMods + "/acidphantasm-progressivebotsystem/state.json", "{ \"season\": 1 }");
        WriteInstall(fx, SptLayout.UserMods + "/acidphantasm-progressivebotsystem/logs/debug.txt", "log");

        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Success, harvest.Status);
        Assert.Equal(2, harvest.AssignedToMods.Count);
        Assert.Contains(harvest.AssignedToMods, file => file.CanonicalPath.EndsWith("/config.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(harvest.AssignedToMods, file => file.CanonicalPath.EndsWith("/blacklists.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(harvest.Files, file => file.CanonicalPath.Contains("GeneratedVanillaMappings", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(harvest.Files, file => file.CanonicalPath.EndsWith("/state.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(harvest.Files, file => file.CanonicalPath.Contains("/logs/", StringComparison.OrdinalIgnoreCase));

        var runtime = ProfileRuntimeStore.TryRead(fx.ManagerData, ProfilePaths.DefaultProfileId, "APBS - Acid's Progressive Bot System");
        Assert.NotNull(runtime);
        Assert.Equal(2, runtime.Files.Count);
        Assert.DoesNotContain(
            HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            path => path.EndsWith("/config.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Harvest_BepInExCfg_AssignsByPluginFolder_WhenForgeGuidMissing()
    {
        using var fx = new DeployFixture();
        WriteInstall(fx, SptLayout.BepInExConfig + "/BepInEx.cfg", "stock");
        fx.PutMod("Auto Deposit", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/Tyfon.AutoDeposit/Tyfon.AutoDeposit.dll"] = "dll"
        });
        Assert.Equal(
            DeployStatus.Success,
            fx.Engine.Deploy(fx.Request(fx.Enable(("Auto Deposit", "1.0", 0)))).Status);

        WriteInstall(fx, SptLayout.BepInExConfig + "/Tyfon.AutoDeposit.cfg", "f12");
        WriteInstall(
            fx,
            "BepInEx/plugins/Tyfon.AutoDeposit/extra-layouts.json",
            "{ }");

        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Success, harvest.Status);
        Assert.Contains(harvest.AssignedToMods, file => file.CanonicalPath == "BepInEx/config/Tyfon.AutoDeposit.cfg");
        Assert.Contains(
            harvest.AssignedToMods,
            file => file.CanonicalPath == "BepInEx/plugins/Tyfon.AutoDeposit/extra-layouts.json");
        Assert.DoesNotContain(
            harvest.Files,
            file => file.CanonicalPath.Contains("Tyfon.AutoDeposit", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(ProfileRuntimeStore.TryRead(fx.ManagerData, ProfilePaths.DefaultProfileId, "Auto Deposit"));
        Assert.DoesNotContain(
            HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            path => path.Contains("Tyfon.AutoDeposit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PromoteOverwriteConfigs_MovesExistingConfigs_LeavesState()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Project Fika - Server", "2.4.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/fika-server/FikaServer.dll"] = "server"
        });
        fx.PutMod("Talk", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/Talk/mod.dll"] = "talk"
        });

        var overwrite = ProfilePaths.Overwrite(fx.ManagerData, ProfilePaths.DefaultProfileId);
        WriteOverwrite(overwrite, "SPT_Runtime/user/mods/fika-server/assets/configs/fika.jsonc", "{ }");
        WriteOverwrite(overwrite, "SPT_Runtime/user/mods/Talk/config.json", "{ \"x\": 1 }");
        WriteOverwrite(overwrite, "SPT_Runtime/user/mods/Talk/state.json", "{ \"left\": true }");

        var moved = HarvestEngine.PromoteOverwriteConfigs(fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(2, moved.Count);
        Assert.NotNull(ProfileRuntimeStore.TryRead(fx.ManagerData, ProfilePaths.DefaultProfileId, "Project Fika - Server"));
        Assert.NotNull(ProfileRuntimeStore.TryRead(fx.ManagerData, ProfilePaths.DefaultProfileId, "Talk"));
        Assert.Contains(
            HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            path => path.EndsWith("/state.json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            path => path.Contains("config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PromoteOverwriteConfigs_AssignsBepInExCfg_ByPluginFolder()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Scope Rangefinder", "1.0", new Dictionary<string, string>
        {
            ["BepInEx/plugins/maschine-ScopeRangefinder/ScopeRangefinder.dll"] = "dll"
        });

        var overwrite = ProfilePaths.Overwrite(fx.ManagerData, ProfilePaths.DefaultProfileId);
        WriteOverwrite(overwrite, "BepInEx/config/com.maschine.ScopeRangefinder.cfg", "f12");
        WriteOverwrite(overwrite, "BepInEx/plugins/maschine-ScopeRangefinder/ScopeRangefinder.layouts.json", "{ }");
        WriteOverwrite(overwrite, "BepInEx/config/com.lacyway.ch.cfg", "short");

        var moved = HarvestEngine.PromoteOverwriteConfigs(fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(2, moved.Count);
        Assert.NotNull(ProfileRuntimeStore.TryRead(fx.ManagerData, ProfilePaths.DefaultProfileId, "Scope Rangefinder"));
        Assert.Contains(
            HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            path => path.Equals("BepInEx/config/com.lacyway.ch.cfg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Harvest_FikaGeneratedFiles_GoToModRuntime()
    {
        using var fx = new DeployFixture();
        fx.PutMod("fika-server", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/fika-server/FikaServer.dll"] = "server"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("fika-server", "1.0", 0)))).Status);

        WriteInstall(fx, SptLayout.UserMods + "/fika-server/assets/configs/fika.jsonc", "{ }");
        WriteInstall(fx, SptLayout.UserMods + "/fika-server/database/friendRequests.json", "[]");
        WriteInstall(fx, SptLayout.UserMods + "/fika-server/database/playerRelations.json", "{}");

        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.Success, harvest.Status);
        Assert.Equal(3, harvest.AssignedToMods.Count);
        Assert.Empty(harvest.Files);
        Assert.Null(ModStore.TryRead(fx.ManagerData, "fika-server", HarvestRules.RuntimeVersion));
        Assert.NotNull(ProfileRuntimeStore.TryRead(fx.ManagerData, ProfilePaths.DefaultProfileId, "fika-server"));
        Assert.Equal(
            "{ }",
            File.ReadAllText(Path.Combine(
                ProfileRuntimeStore.FilesDirectory(fx.ManagerData, ProfilePaths.DefaultProfileId, "fika-server"),
                "SPT_Runtime", "user", "mods", "fika-server", "assets", "configs", "fika.jsonc")));
        Assert.DoesNotContain(
            HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            path => path.Contains("fika-server", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request()).Status);
        Assert.Equal("{ }", File.ReadAllText(fx.Install(SptLayout.UserMods + "/fika-server/assets/configs/fika.jsonc")));
        Assert.Equal("[]", File.ReadAllText(fx.Install(SptLayout.UserMods + "/fika-server/database/friendRequests.json")));
        Assert.Equal("{}", File.ReadAllText(fx.Install(SptLayout.UserMods + "/fika-server/database/playerRelations.json")));
        Assert.Equal("server", File.ReadAllText(fx.Install(SptLayout.UserMods + "/fika-server/FikaServer.dll")));

        var listed = InstallInventory.Scan(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Single(listed.Items, item => item.Kind == InstallInventory.StoreKind && item.Key == "fika-server");
        Assert.Equal(3, listed.Items.Single(item => item.Key == "fika-server").RuntimeFileCount);
    }

    [Fact]
    public void AssignOverwrite_CreatesNewStoreVersion_LeavesOriginal()
    {
        using var fx = new DeployFixture();
        fx.PutMod("Talk", "1.0", new Dictionary<string, string>
        {
            ["SPT_Runtime/user/mods/Talk/mod.dll"] = "talk"
        });
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Talk", "1.0", 0)))).Status);
        File.WriteAllText(fx.Install(SptLayout.UserMods + "/Talk/generated.json"), "session");
        Assert.Equal(DeployStatus.Success, new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId).Status);

        var assigned = HarvestEngine.AssignToMod(
            fx.ManagerData,
            ProfilePaths.DefaultProfileId,
            "Talk",
            "1.0",
            ["SPT_Runtime/user/mods/Talk/generated.json"]);
        Assert.NotEqual("1.0", assigned.Document.Version);
        Assert.Equal("1.0", assigned.PreviousVersion);
        Assert.Contains(assigned.Document.Files, file => file.CanonicalPath == "SPT_Runtime/user/mods/Talk/generated.json");
        Assert.Equal("talk", File.ReadAllText(Path.Combine(ModStore.FilesDirectory(fx.ManagerData, "Talk", "1.0"), "SPT_Runtime", "user", "mods", "Talk", "mod.dll")));
        Assert.DoesNotContain(
            HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId),
            path => path.Equals("SPT_Runtime/user/mods/Talk/generated.json", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request(fx.Enable(("Talk", assigned.Document.Version, 0)))).Status);
        Assert.Equal("session", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Talk/generated.json")));
        Assert.Equal("talk", File.ReadAllText(fx.Install(SptLayout.UserMods + "/Talk/mod.dll")));
    }

    [Fact]
    public void DiscardSelectedOverwrite_RemovesOnlyThatFile()
    {
        using var fx = new DeployFixture();
        WriteInstall(fx, SptLayout.BepInExConfig + "/BepInEx.cfg", "stock");
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([])).Status);
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/keep.cfg"), "keep");
        File.WriteAllText(fx.Install(SptLayout.BepInExConfig + "/drop.cfg"), "drop");
        Assert.Equal(DeployStatus.Success, new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId).Status);

        HarvestEngine.DiscardPaths(fx.ManagerData, ProfilePaths.DefaultProfileId, ["BepInEx/config/drop.cfg"]);
        var listed = HarvestEngine.ListOverwrite(fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Contains(listed, path => path.Equals("BepInEx/config/keep.cfg", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listed, path => path.Equals("BepInEx/config/drop.cfg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Harvest_BlockedWhileSptProcessReported()
    {
        using var fx = new DeployFixture();
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([])).Status);
        fx.Lock.Running.Add("EscapeFromTarkov");
        var harvest = new HarvestEngine(fx.Lock).Harvest(fx.GameRoot, fx.ManagerData, ProfilePaths.DefaultProfileId);
        Assert.Equal(DeployStatus.BlockedProcesses, harvest.Status);
    }

    [Fact]
    public void CopyProfile_WarnsWhenSavesEmpty()
    {
        using var fx = new DeployFixture();
        Assert.Equal(DeployStatus.Success, fx.Engine.Deploy(fx.Request([], "alpha")).Status);
        var copied = ProfileCopier.Copy(fx.ManagerData, "alpha", "empty-saves");
        Assert.True(copied.Success);
        Assert.True(copied.SavesLookEmpty);
        Assert.Contains("Fika join", copied.Message);
    }

    private static void WriteInstall(DeployFixture fx, string relative, string contents)
    {
        var path = fx.Install(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static void WriteOverwrite(string overwriteRoot, string relative, string contents)
    {
        var path = GamePath.Combine(overwriteRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
