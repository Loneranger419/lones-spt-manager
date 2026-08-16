using Lones.SptManager.Core.Paths;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Instance;

public sealed class SptInstanceBinder
{
    private readonly IFileVersionReader _versions;
    private readonly IVolumeIdReader _volumes;

    public SptInstanceBinder()
        : this(new FileVersionReader(), new VolumeIdReader())
    {
    }

    public SptInstanceBinder(IFileVersionReader versions, IVolumeIdReader volumes)
    {
        _versions = versions;
        _volumes = volumes;
    }

    public BindResult Bind(string gameRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        if (!Directory.Exists(gameRoot))
        {
            return BindResult.Fail(BindStatus.GameRootNotFound, gameRoot, null, "Game root does not exist.");
        }

        var root = Path.GetFullPath(gameRoot);
        var legacySpt = Path.Combine(root, SptLayout.LegacySptFolder);
        var runtime = Path.Combine(root, SptLayout.SptRuntime);
        var hasLegacy = Directory.Exists(legacySpt);
        var hasRuntime = Directory.Exists(runtime);

        if (hasLegacy && !hasRuntime)
        {
            return BindResult.Fail(
                BindStatus.UnsupportedSpt40Layout,
                root,
                [SptLayout.SptRuntime],
                "This looks like an SPT 4.0 layout (folder named SPT). Lone's SPT Manager targets 4.1.x SPT_Runtime.");
        }

        var missing = new List<string>();
        foreach (var file in SptLayout.RequiredGameRootFiles)
        {
            if (!File.Exists(Combine(root, file)))
            {
                missing.Add(file);
            }
        }

        foreach (var dir in SptLayout.RequiredGameRootDirectories)
        {
            if (!Directory.Exists(Combine(root, dir)))
            {
                missing.Add(dir);
            }
        }

        foreach (var file in SptLayout.RequiredRuntimeFiles)
        {
            if (!File.Exists(Combine(root, file)))
            {
                missing.Add(file);
            }
        }

        if (missing.Count > 0)
        {
            return BindResult.Fail(
                BindStatus.MissingRequiredFiles,
                root,
                missing,
                "Required SPT 4.1 files are missing. Bind an overlayed EFT + SPT 4.1.x game root.");
        }

        var warnings = new List<BindWarning>();
        if (hasLegacy)
        {
            warnings.Add(BindWarning.ExtraLegacySptFolder);
        }

        var sptVersion = _versions.GetFileVersion(Combine(root, SptLayout.SptServerExe));
        var eftVersion = _versions.GetFileVersion(Combine(root, SptLayout.EscapeFromTarkovExe));

        if (sptVersion is null || !sptVersion.StartsWith(SptLayout.ExpectedSptVersionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(BindWarning.SptVersionNot41);
        }

        if (!string.Equals(eftVersion, SptLayout.ExpectedEftFileVersion, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(BindWarning.EftVersionMismatch);
        }

        return new BindResult(
            BindStatus.Success,
            root,
            [],
            warnings,
            sptVersion,
            eftVersion,
            Directory.Exists(Combine(root, SptLayout.UserMods)),
            Directory.Exists(Combine(root, SptLayout.UserProfiles)),
            Directory.Exists(Combine(root, SptLayout.UserPatchers)),
            File.Exists(Combine(root, SptLayout.UserLauncherConfig)),
            TryVolume(root),
            BuildSuccessMessage(sptVersion, eftVersion, warnings));
    }

    private string? TryVolume(string root)
    {
        try
        {
            return _volumes.GetVolumeId(root);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Combine(string root, string relative)
        => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string BuildSuccessMessage(string? spt, string? eft, List<BindWarning> warnings)
    {
        var text = $"Bound SPT {spt ?? "unknown"} / EFT {eft ?? "unknown"}.";
        if (warnings.Count > 0)
        {
            text += " Warnings: " + string.Join(", ", warnings);
        }

        return text;
    }
}
