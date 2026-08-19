using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Forge;

namespace Lones.SptManager.Tests;

public sealed class ModPackUpdateCheckTests
{
    [Fact]
    public void Compare_FlagsNewerVersionAndNewMod_IgnoresAheadAndRestricted()
    {
        var pack = ModPackManifest.Parse(
            """
            {
              "mods": [
                { "id": 10, "name": "Already", "installedVersion": "1.2.0" },
                { "id": 20, "name": "Fresh", "installedVersion": "2.0.0" },
                { "id": 30, "name": "Ahead", "installedVersion": "1.0.0" },
                { "id": 2851, "name": "SPT Mod Manager", "installedVersion": "9.9.9" }
              ]
            }
            """);
        var store = new[]
        {
            Doc("Already", "1.1.0", 10),
            Doc("Ahead", "1.5.0", 30)
        };
        var enabled = new[]
        {
            On("Already", "1.1.0"),
            On("Ahead", "1.5.0")
        };

        var report = ModPackUpdateCheck.Compare(pack, enabled, store);
        Assert.True(report.HasUpdates);
        Assert.Contains(report.Changes, item => item.DisplayName == "Already" && item.Kind == ModPackUpdateReport.NewerVersion);
        Assert.Contains(report.Changes, item => item.DisplayName == "Fresh" && item.Kind == ModPackUpdateReport.NewMod);
        Assert.DoesNotContain(report.Changes, item => item.DisplayName == "Ahead");
        Assert.DoesNotContain(report.Changes, item => item.DisplayName.Contains("Mod Manager", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("newer version", report.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new mod", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_SameVersion_IsQuiet()
    {
        var pack = ModPackManifest.Parse(
            """
            { "mods": [ { "id": 10, "name": "Already", "installedVersion": "v1.1.0" } ] }
            """);
        var report = ModPackUpdateCheck.Compare(
            pack,
            [On("Already", "1.1.0")],
            [Doc("Already", "1.1.0", 10)]);
        Assert.False(report.HasUpdates);
        Assert.Equal(string.Empty, report.Summary);
    }

    [Fact]
    public void Compare_FlagsMissingAndNewerAddon_IgnoresModWithSameId()
    {
        var pack = ModPackManifest.Parse(
            """
            {
              "mods": [
                { "id": 1657, "name": "MergeConsumables", "installedVersion": "1.6.1" },
                { "kind": "addon", "id": 4, "name": "Merge Consumables - Fika sync", "installedVersion": "1.2.0" }
              ]
            }
            """);
        var store = new[]
        {
            Doc("MergeConsumables", "1.6.1", 1657),
            Doc("Some other mod 4", "9.9.9", 4),
            AddonDoc("Merge Consumables - Fika sync", "1.1.0", 4)
        };
        var enabled = new[]
        {
            On("MergeConsumables", "1.6.1"),
            On("Some other mod 4", "9.9.9"),
            On("Merge Consumables - Fika sync", "1.1.0")
        };

        var report = ModPackUpdateCheck.Compare(pack, enabled, store);
        Assert.True(report.HasUpdates);
        Assert.Contains(
            report.Changes,
            item => item.DisplayName == "Merge Consumables - Fika sync"
                    && item.Kind == ModPackUpdateReport.NewerVersion
                    && item.CurrentVersion == "1.1.0"
                    && item.PackVersion == "1.2.0");
        Assert.DoesNotContain(report.Changes, item => item.DisplayName == "MergeConsumables");
        Assert.DoesNotContain(report.Changes, item => item.DisplayName == "Some other mod 4");
    }

    [Fact]
    public void Compare_MissingAddon_IsNewEvenIfModIdMatches()
    {
        var pack = ModPackManifest.Parse(
            """
            { "mods": [ { "kind": "addon", "id": 4, "name": "Merge Consumables - Fika sync", "installedVersion": "1.1.0" } ] }
            """);
        var report = ModPackUpdateCheck.Compare(
            pack,
            [On("Some other mod 4", "9.9.9")],
            [Doc("Some other mod 4", "9.9.9", 4)]);
        Assert.True(report.HasUpdates);
        Assert.Contains(
            report.Changes,
            item => item.DisplayName == "Merge Consumables - Fika sync" && item.Kind == ModPackUpdateReport.NewMod);
    }

    [Theory]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("1.1.0", "1.2.0", -1)]
    [InlineData("v1.1.0", "1.1.0", 0)]
    [InlineData("1.1", "1.1.0", 0)]
    public void CompareVersions_OrdersNumericParts(string left, string right, int expectedSign)
    {
        var result = ModPackUpdateCheck.CompareVersions(left, right);
        Assert.NotNull(result);
        Assert.Equal(expectedSign, Math.Sign(result.Value));
    }

    private static EnabledMod On(string key, string version)
        => new() { ModKey = key, Version = version, Priority = 0, Enabled = true };

    private static ModDocument Doc(string key, string version, int forgeId)
        => new()
        {
            ModKey = key,
            Version = version,
            Kind = "Client",
            Deployable = true,
            ForgeModId = forgeId,
            Files = [],
            ImportedAtUtc = DateTimeOffset.UtcNow
        };

    private static ModDocument AddonDoc(string key, string version, int addonId)
        => new()
        {
            ModKey = key,
            Version = version,
            Kind = "Client",
            Deployable = true,
            ForgeAddonId = addonId,
            Files = [],
            ImportedAtUtc = DateTimeOffset.UtcNow
        };
}
