using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Tests;

public sealed class SptDenylistTests
{
    [Theory]
    [InlineData("BepInEx/plugins/spt/spt-core.dll")]
    [InlineData(@"BepInEx\plugins\spt\ConfigurationManager\ConfigurationManager.dll")]
    [InlineData("BepInEx/core/BepInEx.Preloader.dll")]
    [InlineData("BepInEx/patchers/spt-prepatch.dll")]
    [InlineData("winhttp.dll")]
    [InlineData("doorstop_config.ini")]
    [InlineData(".doorstop_version")]
    [InlineData("SPT_Runtime/SPT.Server.exe")]
    [InlineData("SPT_Runtime/SPTarkov.Server.Core.dll")]
    [InlineData("SPT_Runtime/SPT_Data/configs/core.json")]
    [InlineData("SPT_Runtime/SPT_Data/Launcher/ignored.bin")]
    public void Forbidden_SptOwnedPaths(string relative)
    {
        Assert.True(SptDenylist.IsForbidden(relative));
    }

    [Theory]
    [InlineData("BepInEx/plugins/Fika/Fika.Core.dll")]
    [InlineData("SPT_Runtime/user/mods/fika-server/FikaServer.dll")]
    [InlineData("SPT_Runtime/user/patchers/com.example/pre.dll")]
    [InlineData("BepInEx/config/com.fika.core.cfg")]
    public void Allowed_UserModPaths(string relative)
    {
        Assert.False(SptDenylist.IsForbidden(relative));
    }
}

public sealed class SptOwnedBaselineTests
{
    [Fact]
    public void Baseline_IncludesSptOwned_ExcludesUserModsAndSecrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-spt-base-" + Guid.NewGuid().ToString("N"));
        GameRootFixture.Create41Layout(root, includeUserMods: true);
        try
        {
            var baseline = new SptOwnedBaselineBuilder().Build(root);
            Assert.Contains(baseline.Files, file => GamePath.EqualsNormalized(file.RelativePath, SptLayout.WinHttpDll));
            Assert.Contains(baseline.Files, file => GamePath.IsUnderOrEqual(file.RelativePath, SptLayout.BepInExPluginsSpt));
            Assert.Contains(baseline.Files, file => GamePath.IsUnderOrEqual(file.RelativePath, SptLayout.SptDataConfigs));
            Assert.DoesNotContain(baseline.Files, file => GamePath.IsUnderOrEqual(file.RelativePath, SptLayout.UserMods));
            Assert.DoesNotContain(baseline.Files, file => GamePath.IsUnderOrEqual(file.RelativePath, SptLayout.UserCredentials));
            Assert.All(baseline.Files, file => Assert.False(string.IsNullOrWhiteSpace(file.Sha256)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class InstanceStoreTests
{
    [Fact]
    public void Save_WritesManifestAndRefusesFailedBind()
    {
        var manager = Path.Combine(Path.GetTempPath(), "lones-mgr-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(Path.GetTempPath(), "lones-spt-save-" + Guid.NewGuid().ToString("N"));
        GameRootFixture.Create41Layout(root);
        try
        {
            var versions = new StubFileVersionReader();
            versions.Set(GameRootFixture.Combine(root, SptLayout.SptServerExe), "4.1.2");
            versions.Set(GameRootFixture.Combine(root, SptLayout.EscapeFromTarkovExe), SptLayout.ExpectedEftFileVersion);
            var bind = new SptInstanceBinder(versions, new StubVolumeIdReader()).Bind(root);
            var baseline = new SptOwnedBaselineBuilder().Build(root);
            var document = new InstanceStore(new StubVolumeIdReader()).Save(manager, bind, baseline);
            Assert.Equal(1, document.ManifestVersion);
            Assert.True(File.Exists(Path.Combine(manager, "instances", document.InstanceId, "instance.json")));

            var failed = BindResult.Fail(BindStatus.MissingRequiredFiles, root, ["winhttp.dll"], "nope");
            Assert.Throws<InvalidOperationException>(() => new InstanceStore(new StubVolumeIdReader()).Save(manager, failed, baseline));

            var again = new InstanceStore(new StubVolumeIdReader()).Save(manager, bind, baseline);
            Assert.Equal(document.InstanceId, again.InstanceId);
            Assert.Equal(document.InstanceId, InstanceStore.TryLatest(manager)!.InstanceId);
            Assert.Equal(document.InstanceId, InstanceStore.TryFindByGameRoot(manager, root)!.InstanceId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            if (Directory.Exists(manager))
            {
                Directory.Delete(manager, recursive: true);
            }
        }
    }
}
