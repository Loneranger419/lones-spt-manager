using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Tests;

public sealed class SptInstanceBinderTests
{
    [Fact]
    public void Bind_MissingRoot_ReturnsNotFound()
    {
        var binder = new SptInstanceBinder(new StubFileVersionReader(), new StubVolumeIdReader());
        var result = binder.Bind(Path.Combine(Path.GetTempPath(), "lones-spt-missing-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(BindStatus.GameRootNotFound, result.Status);
    }

    [Fact]
    public void Bind_LegacySptFolderWithoutRuntime_IsUnsupported()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-spt-40-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, SptLayout.LegacySptFolder));
        try
        {
            var result = new SptInstanceBinder(new StubFileVersionReader(), new StubVolumeIdReader()).Bind(root);
            Assert.Equal(BindStatus.UnsupportedSpt40Layout, result.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bind_41Layout_SucceedsWithoutUserMods()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-spt-41-" + Guid.NewGuid().ToString("N"));
        GameRootFixture.Create41Layout(root, includeUserMods: false);
        try
        {
            var versions = new StubFileVersionReader();
            versions.Set(GameRootFixture.Combine(root, SptLayout.SptServerExe), "4.1.2");
            versions.Set(GameRootFixture.Combine(root, SptLayout.EscapeFromTarkovExe), SptLayout.ExpectedEftFileVersion);
            var result = new SptInstanceBinder(versions, new StubVolumeIdReader()).Bind(root);
            Assert.Equal(BindStatus.Success, result.Status);
            Assert.False(result.HasUserModsDirectory);
            Assert.True(result.HasUserProfilesDirectory);
            Assert.False(result.HasUserLauncherConfig);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bind_WrongVersions_WarnsButSucceeds()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-spt-ver-" + Guid.NewGuid().ToString("N"));
        GameRootFixture.Create41Layout(root, includeLegacySpt: true);
        try
        {
            var versions = new StubFileVersionReader();
            versions.Set(GameRootFixture.Combine(root, SptLayout.SptServerExe), "4.0.11");
            versions.Set(GameRootFixture.Combine(root, SptLayout.EscapeFromTarkovExe), "0.16.9.0.40087");
            var result = new SptInstanceBinder(versions, new StubVolumeIdReader()).Bind(root);
            Assert.Equal(BindStatus.Success, result.Status);
            Assert.Contains(BindWarning.SptVersionNot41, result.Warnings);
            Assert.Contains(BindWarning.EftVersionMismatch, result.Warnings);
            Assert.Contains(BindWarning.ExtraLegacySptFolder, result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bind_MissingDoorstop_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "lones-spt-miss-" + Guid.NewGuid().ToString("N"));
        GameRootFixture.Create41Layout(root);
        File.Delete(Path.Combine(root, SptLayout.WinHttpDll));
        try
        {
            var result = new SptInstanceBinder(new StubFileVersionReader(), new StubVolumeIdReader()).Bind(root);
            Assert.Equal(BindStatus.MissingRequiredFiles, result.Status);
            Assert.Contains(SptLayout.WinHttpDll, result.MissingPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
