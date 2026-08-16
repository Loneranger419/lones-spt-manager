using Lones.SptManager.Core.Mapping;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Tests;

public sealed class PrefixMapperTests
{
    [Fact]
    public void Prefixes_NormalizeToSameCanonicalTree()
    {
        var a = PrefixMapper.Map(
        [
            "BepInEx/plugins/Fika/Fika.Core.dll",
            "SPT_Runtime/user/mods/fika-server/FikaServer.dll"
        ]);
        var b = PrefixMapper.Map(
        [
            "SPT/user/mods/fika-server/FikaServer.dll",
            "BepInEx/plugins/Fika/Fika.Core.dll"
        ]);
        var c = PrefixMapper.Map(
        [
            @"BepInEx\plugins\Fika\Fika.Core.dll",
            @"user\mods\fika-server\FikaServer.dll"
        ]);

        Assert.Equal(PackageKind.Hybrid, a.Kind);
        Assert.Equal(PackageKind.Hybrid, b.Kind);
        Assert.Equal(PackageKind.Hybrid, c.Kind);
        Assert.Equal(CanonicalSet(a), CanonicalSet(b));
        Assert.Equal(CanonicalSet(a), CanonicalSet(c));
        Assert.Contains("BepInEx/plugins/Fika/Fika.Core.dll", CanonicalSet(a));
        Assert.Contains("SPT_Runtime/user/mods/fika-server/FikaServer.dll", CanonicalSet(a));
    }

    [Fact]
    public void WrapperFolder_IsStripped()
    {
        var map = PrefixMapper.Map(
        [
            "FieldKit-1.4.0/BepInEx/plugins/Hysocs-FieldKit/FieldKit.dll",
            "FieldKit-1.4.0/SPT_Runtime/user/mods/HysocsFieldKit/FieldKit.Server.dll"
        ]);
        Assert.Equal("FieldKit-1.4.0", map.WrapperFolder);
        Assert.Equal(PackageKind.Hybrid, map.Kind);
        Assert.Contains("BepInEx/plugins/Hysocs-FieldKit/FieldKit.dll", CanonicalSet(map));
        Assert.Contains("SPT_Runtime/user/mods/HysocsFieldKit/FieldKit.Server.dll", CanonicalSet(map));
    }

    [Fact]
    public void HasPrepatcherLayout_MapsPatchers()
    {
        var map = PrefixMapper.Map(["SPT_Runtime/user/patchers/com.example.mod/pre.dll"]);
        Assert.Equal(PackageKind.Server, map.Kind);
        Assert.Contains("SPT_Runtime/user/patchers/com.example.mod/pre.dll", CanonicalSet(map));
    }

    [Fact]
    public void RootExe_IsToolNotMerged()
    {
        var map = PrefixMapper.Map(["SPTModChecker_v3.3.1.exe"]);
        Assert.Equal(PackageKind.Tool, map.Kind);
        Assert.False(map.Deployable);
        Assert.Contains(map.Entries, entry => entry.Disposition == MapDisposition.ToolNotMerged);
    }

    [Fact]
    public void SptData_IsSkippedUnlessAdvanced()
    {
        var blocked = PrefixMapper.Map(["SPT/SPT_Data/configs/core.json"]);
        Assert.Contains(blocked.Entries, entry => entry.Disposition == MapDisposition.SkippedSptData);
        var allowed = PrefixMapper.Map(["SPT/SPT_Data/configs/core.json"], new MapperOptions { AllowSptData = true });
        Assert.Contains("SPT_Runtime/SPT_Data/configs/core.json", CanonicalSet(allowed));
    }

    [Fact]
    public void LoosePluginDll_WrapsIntoPerModFolder()
    {
        var map = PrefixMapper.Map(["BepInEx/plugins/CoolMod.dll"]);
        Assert.Contains("BepInEx/plugins/CoolMod/CoolMod.dll", CanonicalSet(map));
        Assert.Equal(PackageKind.Client, map.Kind);
        Assert.True(map.Deployable);
    }

    [Fact]
    public void Denylist_Throws()
    {
        Assert.Throws<ZipSlipException>(() => PrefixMapper.Map(["BepInEx/plugins/spt/spt-core.dll"]));
        Assert.Throws<ZipSlipException>(() => PrefixMapper.Map(["winhttp.dll"]));
    }

    [Fact]
    public void DuplicateAfterNormalize_Throws()
    {
        Assert.Throws<ZipSlipException>(() => PrefixMapper.Map(
        [
            @"SPT_Runtime\user\mods\X\a.dll",
            "SPT_Runtime/user/mods/X/a.dll"
        ]));
    }

    private static HashSet<string> CanonicalSet(PackageMap map)
        => map.DeployFiles.Select(entry => GamePath.Normalize(entry.CanonicalPath!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed class ArchivePathRulesTests
{
    [Theory]
    [InlineData("../evil.dll")]
    [InlineData(@"..\..\Windows\evil.dll")]
    [InlineData("BepInEx/plugins/../../evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("/etc/passwd")]
    [InlineData("BepInEx/plugins/foo:stream")]
    [InlineData("CON.dll")]
    [InlineData("BepInEx/plugins/NUL/x.dll")]
    public void ZipSlip_Rejected(string entry)
    {
        Assert.Throws<ZipSlipException>(() => ArchivePathRules.EnsureSafe(entry, Path.GetTempPath()));
    }

    [Fact]
    public void BackslashLimitlessPath_IsSafeAndMaps()
    {
        var raw = @"SPT_Runtime\user\mods\Limitless\Limitless.dll";
        ArchivePathRules.EnsureSafe(raw, Path.GetTempPath());
        var map = PrefixMapper.Map([raw]);
        Assert.Equal(PackageKind.Server, map.Kind);
        Assert.Equal("SPT_Runtime/user/mods/Limitless/Limitless.dll", map.DeployFiles[0].CanonicalPath);
    }
}

public sealed class InstallMapperTests
{
    [Fact]
    public void Import_WritesStoreAndModJson()
    {
        var work = Path.Combine(Path.GetTempPath(), "lones-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = ZipFixture.WriteZip(
                Path.Combine(work, "TrashTalk.zip"),
                [
                    ("BepInEx/plugins/TrashTalk/TrashTalk.dll", "client"),
                    ("SPT_Runtime/user/mods/TrashTalk/TrashTalkServer.dll", "server"),
                    ("README.md", "docs")
                ]);
            var manager = Path.Combine(work, "mgr");
            var result = new InstallMapper().ImportArchive(zip, manager);
            Assert.NotNull(result.Document);
            Assert.Equal(PackageKind.Hybrid, result.Map.Kind);
            Assert.Equal(2, result.Document!.Files.Count);
            Assert.True(File.Exists(Path.Combine(ModStore.PackageDirectory(manager, result.Document.ModKey, result.Document.Version), "mod.json")));
            Assert.True(File.Exists(Path.Combine(ModStore.PackageDirectory(manager, result.Document.ModKey, result.Document.Version), "files", "BepInEx", "plugins", "TrashTalk", "TrashTalk.dll")));
            Assert.Contains(result.Document.Files, file => file.Sha256.Length == 64);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void Import_ReportsExtractProgress()
    {
        var work = Path.Combine(Path.GetTempPath(), "lones-prog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = ZipFixture.WriteZip(
                Path.Combine(work, "Talk.zip"),
                [("BepInEx/plugins/Talk/Talk.dll", "client")]);
            var seen = new List<string>();
            var result = new InstallMapper().ImportArchive(
                zip,
                Path.Combine(work, "mgr"),
                progress: new SyncProgress<string>(seen.Add));
            Assert.NotNull(result.Document);
            Assert.Contains(seen, line => line.StartsWith("Reading archive listing", StringComparison.Ordinal));
            Assert.Contains(seen, line => line.Contains("Unpacking", StringComparison.Ordinal) || line.Contains("Hashing", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void Import_ManySmallFiles_HashesMatchAndUsesParallelPath()
    {
        var work = Path.Combine(Path.GetTempPath(), "lones-many-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var entries = Enumerable.Range(0, 48)
                .Select(i => ($"BepInEx/plugins/Many/f{i:D2}.dll", "payload-" + i))
                .ToArray();
            var zip = ZipFixture.WriteZip(Path.Combine(work, "Many.zip"), entries);
            var manager = Path.Combine(work, "mgr");
            var result = new InstallMapper().ImportArchive(zip, manager);
            Assert.NotNull(result.Document);
            Assert.Equal(48, result.Document!.Files.Count);
            var filesDir = Path.Combine(
                ModStore.PackageDirectory(manager, result.Document.ModKey, result.Document.Version),
                "files");
            foreach (var record in result.Document.Files)
            {
                var path = Path.Combine(filesDir, record.CanonicalPath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path));
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), record.Sha256);
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void Import_ZipSlip_DoesNotWriteStore()
    {
        var work = Path.Combine(Path.GetTempPath(), "lones-slip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = ZipFixture.WriteZip(Path.Combine(work, "evil.zip"), [("BepInEx/plugins/../../evil.dll", "nope")]);
            var manager = Path.Combine(work, "mgr");
            Assert.Throws<ZipSlipException>(() => new InstallMapper().ImportArchive(zip, manager));
            Assert.False(Directory.Exists(Path.Combine(manager, "store")));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void Import_ContentLengthMismatch_Throws()
    {
        var work = Path.Combine(Path.GetTempPath(), "lones-len-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = ZipFixture.WriteZip(Path.Combine(work, "a.zip"), [("BepInEx/plugins/X/X.dll", "x")]);
            Assert.Throws<InvalidOperationException>(() =>
                new InstallMapper().ImportArchive(zip, work, new MapperOptions { ExpectedContentLength = 1 }));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void Import_LowConfidenceRootDll_BlockedUntilConfirmed()
    {
        var work = Path.Combine(Path.GetTempPath(), "lones-low-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = ZipFixture.WriteZip(Path.Combine(work, "loose.zip"), [("CoolMod.dll", "dll")]);
            var blocked = new InstallMapper().ImportArchive(zip, work);
            Assert.Null(blocked.Document);
            Assert.True(blocked.Map.NeedsConfirm);

            var imported = new InstallMapper().ImportArchive(zip, work, new MapperOptions { AllowLowConfidence = true });
            Assert.NotNull(imported.Document);
            Assert.Contains(imported.Document!.Files, file => file.CanonicalPath.StartsWith("BepInEx/plugins/", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }
}
