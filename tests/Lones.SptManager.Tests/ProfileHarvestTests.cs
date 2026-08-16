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
}
