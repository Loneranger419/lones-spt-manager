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
    [InlineData("SPT_Runtime/user/mods/Talk/generated.json", false)]
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
}
