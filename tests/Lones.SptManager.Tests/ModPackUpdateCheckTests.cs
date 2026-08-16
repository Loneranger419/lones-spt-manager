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
}
