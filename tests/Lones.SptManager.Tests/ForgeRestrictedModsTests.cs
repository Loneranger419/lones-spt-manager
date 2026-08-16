using Lones.SptManager.Forge;

namespace Lones.SptManager.Tests;

public sealed class ForgeRestrictedModsTests
{
    [Fact]
    public void MatchesKnownSptModManagerIdentity()
    {
        Assert.True(ForgeRestrictedMods.IsRestricted(ForgeRestrictedMods.SptModManagerId));
        Assert.True(ForgeRestrictedMods.IsRestricted(slug: ForgeRestrictedMods.SptModManagerSlug));
        Assert.True(ForgeRestrictedMods.IsRestricted(guid: ForgeRestrictedMods.SptModManagerGuid));
        Assert.True(ForgeRestrictedMods.IsRestricted(name: "spt mod manager"));
        Assert.False(ForgeRestrictedMods.IsRestricted(1343, name: "Skipper", slug: "skipper"));
    }
}
