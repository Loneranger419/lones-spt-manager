using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Tests;

public sealed class HarvestRulesTests
{
    [Theory]
    [InlineData("SPT_Runtime/user/mods/Talk/config.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/config.jsonc")]
    [InlineData("SPT_Runtime/user/mods/Talk/blacklists.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/params.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/loader.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/nameData/userDefinedNames.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/config/config.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/assets/configs/fika.jsonc")]
    [InlineData("BepInEx/config/com.example.mod.cfg")]
    public void ShouldPinToMod_Configs(string path)
        => Assert.True(HarvestRules.ShouldPinToMod(path));

    [Theory]
    [InlineData("SPT_Runtime/user/mods/Talk/state.json", true)]
    [InlineData("SPT_Runtime/user/mods/Talk/config.json", false)]
    [InlineData("SPT_Runtime/user/mods/Talk/generated.json", true)]
    public void ShouldStayInOverwrite_MatchesGeneratedOrState(string path, bool stay)
        => Assert.Equal(stay, HarvestRules.ShouldStayInOverwrite(path));

    [Theory]
    [InlineData("SPT_Runtime/user/mods/Talk/state.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/nameData/allNames.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/data/traits.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/data/shop_global_restock.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/GeneratedVanillaMappings-DO_NOT_TOUCH/ArmorVest.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/logs/debug.txt")]
    [InlineData("SPT_Runtime/user/mods/Talk/wwwroot/files/RELEASE_NOTES.txt")]
    [InlineData("SPT_Runtime/user/mods/Talk/database/friendRequests.json")]
    [InlineData("SPT_Runtime/user/mods/Talk/generated.json")]
    public void ShouldNotPin_GeneratedOrState(string path)
    {
        Assert.True(HarvestRules.IsGeneratedOrState(path) || !HarvestRules.ShouldPinToMod(path));
        Assert.False(HarvestRules.ShouldPinToMod(path));
    }

    [Fact]
    public void TryOwnedModKey_ResolvesDisplayNamePackage()
    {
        var store = new[]
        {
            new ModDocument
            {
                ModKey = "APBS - Acid's Progressive Bot System",
                Version = "2.3.0",
                Kind = "Server",
                Deployable = true,
                Files =
                [
                    new ModFileRecord
                    {
                        CanonicalPath = "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/package.json",
                        Sha256 = "a"
                    }
                ]
            }
        };

        Assert.Equal(
            "APBS - Acid's Progressive Bot System",
            HarvestRules.TryOwnedModKey(
                "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/config.json",
                store));
        Assert.Null(HarvestRules.TryOwnedModKey(
            "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/state.json",
            store));
        Assert.Null(HarvestRules.TryOwnedModKey(
            "SPT_Runtime/user/mods/acidphantasm-progressivebotsystem/GeneratedVanillaMappings-DO_NOT_TOUCH/ArmorVest.json",
            store));
    }

    [Fact]
    public void TryOwnedModKey_FikaDatabase_UsesStoreKeyWhenPresent()
    {
        var store = new[]
        {
            new ModDocument
            {
                ModKey = "Project Fika - Server",
                Version = "2.4.0",
                Kind = "Server",
                Deployable = true,
                Files =
                [
                    new ModFileRecord
                    {
                        CanonicalPath = "SPT_Runtime/user/mods/fika-server/FikaServer.dll",
                        Sha256 = "a"
                    }
                ]
            }
        };

        Assert.Equal(
            "Project Fika - Server",
            HarvestRules.TryOwnedModKey(
                "SPT_Runtime/user/mods/fika-server/database/friendRequests.json",
                store));
        Assert.Equal(
            "fika-server",
            HarvestRules.TryOwnedModKey("SPT_Runtime/user/mods/fika-server/database/friendRequests.json"));
    }

    [Fact]
    public void TryOwnedModKey_BepInExCfg_MatchesForgeGuid()
    {
        var store = new[]
        {
            Client("Waypoints", "1.0", "BepInEx/plugins/DrakiaXYZ-Waypoints/Waypoints.dll", forgeGuid: "xyz.drakia.waypoints")
        };

        Assert.Equal(
            "Waypoints",
            HarvestRules.TryOwnedModKey("BepInEx/config/xyz.drakia.waypoints.cfg", store));
    }

    [Fact]
    public void TryOwnedModKey_BepInExCfg_MatchesPluginFolderOrDll_WhenGuidMissing()
    {
        var store = new[]
        {
            Client("Auto Deposit", "1.0", "BepInEx/plugins/Tyfon.AutoDeposit/Tyfon.AutoDeposit.dll"),
            Client("Scope Rangefinder", "1.0", "BepInEx/plugins/maschine-ScopeRangefinder/ScopeRangefinder.dll"),
            Client("Project Fika", "1.0", "BepInEx/plugins/Fika/Fika.Core.dll"),
            Client("Waypoints", "1.0", "BepInEx/plugins/DrakiaXYZ-Waypoints/Waypoints.dll"),
            Client("WTT CommonLib", "3.0.4", "BepInEx/plugins/WTT-CommonLib/FixPluginTypesSerialization.dll"),
            Client("Auto Shoulder Swap", "1.0", "BepInEx/plugins/hazelify.StanceSync/hazelify.StanceSync.dll")
        };

        Assert.Equal("Auto Deposit", HarvestRules.TryOwnedModKey("BepInEx/config/Tyfon.AutoDeposit.cfg", store));
        Assert.Equal(
            "Scope Rangefinder",
            HarvestRules.TryOwnedModKey("BepInEx/config/com.maschine.ScopeRangefinder.cfg", store));
        Assert.Equal("Project Fika", HarvestRules.TryOwnedModKey("BepInEx/config/com.fika.core.cfg", store));
        Assert.Equal("Waypoints", HarvestRules.TryOwnedModKey("BepInEx/config/xyz.drakia.waypoints.cfg", store));
        Assert.Equal(
            "WTT CommonLib",
            HarvestRules.TryOwnedModKey("BepInEx/config/FixPluginTypesSerialization.cfg", store));
        Assert.Equal(
            "Auto Shoulder Swap",
            HarvestRules.TryOwnedModKey("BepInEx/config/hazelify.StanceSync.cfg", store));
    }

    [Fact]
    public void TryOwnedModKey_BepInExCfg_LeavesShortOrAmbiguousNames()
    {
        var store = new[]
        {
            Client("Continuous Healing", "1.0", "BepInEx/plugins/ContinuousHealing/ContinuousHealing.dll"),
            Client("Hands Are Not Busy", "1.0", "BepInEx/plugins/HandsAreNotBusy/HandsAreNotBusy.dll"),
            Client("Nerf Bot Grenades", "1.0", "BepInEx/plugins/NerfBotGrenades/NerfBotGrenades.dll"),
            Client("Waypoints", "1.0", "BepInEx/plugins/DrakiaXYZ-Waypoints/Waypoints.dll"),
            Client("Other Waypoints", "1.0", "BepInEx/plugins/Other-Waypoints/Other.dll")
        };

        Assert.Null(HarvestRules.TryOwnedModKey("BepInEx/config/com.lacyway.ch.cfg", store));
        Assert.Null(HarvestRules.TryOwnedModKey("BepInEx/config/com.lacyway.hanb.cfg", store));
        Assert.Null(HarvestRules.TryOwnedModKey("BepInEx/config/com.lacyway.nbg.cfg", store));
        Assert.Null(HarvestRules.TryOwnedModKey("BepInEx/config/xyz.drakia.waypoints.cfg", store));
        Assert.Null(HarvestRules.TryOwnedModKey("BepInEx/config/unknown.mod.cfg", store));
    }

    [Fact]
    public void TryOwnedModKey_PluginFolderExtra_AndExactPackagedPath()
    {
        var store = new[]
        {
            Client("Scope Rangefinder", "1.0", "BepInEx/plugins/maschine-ScopeRangefinder/ScopeRangefinder.dll"),
            Client("Sharper Tushonka", "1.0", "ReShade.ini")
        };

        Assert.Equal(
            "Scope Rangefinder",
            HarvestRules.TryOwnedModKey(
                "BepInEx/plugins/maschine-ScopeRangefinder/ScopeRangefinder.layouts.json",
                store));
        Assert.Equal("Sharper Tushonka", HarvestRules.TryOwnedModKey("ReShade.ini", store));
        Assert.Null(HarvestRules.TryOwnedModKey("BepInEx/plugins/maschine-ScopeRangefinder/state.json", store));
    }

    [Fact]
    public void TryOwnedModKey_SameModKeyTwoVersions_IsStillUnique()
    {
        var store = new[]
        {
            Client("WTT CommonLib", "3.0.3", "BepInEx/plugins/WTT-CommonLib/FixPluginTypesSerialization.dll"),
            Client("WTT CommonLib", "3.0.4", "BepInEx/plugins/WTT-CommonLib/FixPluginTypesSerialization.dll")
        };

        Assert.Equal(
            "WTT CommonLib",
            HarvestRules.TryOwnedModKey("BepInEx/config/FixPluginTypesSerialization.cfg", store));
    }

    private static ModDocument Client(string key, string version, string file, string? forgeGuid = null)
        => new()
        {
            ModKey = key,
            Version = version,
            Kind = "Client",
            Deployable = true,
            ForgeGuid = forgeGuid,
            Files =
            [
                new ModFileRecord
                {
                    CanonicalPath = file,
                    Sha256 = "a"
                }
            ]
        };
}
