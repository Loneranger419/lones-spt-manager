using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;

namespace Lones.SptManager.Core.Mapping;

public static class PrefixMapper
{
    private static readonly string[] GameRootMarkers =
    [
        SptLayout.BepInEx,
        SptLayout.SptRuntime,
        SptLayout.LegacySptFolder,
        SptLayout.EscapeFromTarkovData,
        "user"
    ];

    private static readonly HashSet<string> RootInjectorDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "dxgi.dll", "d3d9.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll",
        "ReShade32.dll", "ReShade64.dll"
    };

    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "readme.md", "readme.txt", "license", "license.md", "license.txt",
        "licence", "changelog", "changelog.md", "credits.txt"
    };

    public static PackageMap Map(IEnumerable<string> archivePaths, MapperOptions? options = null)
    {
        options ??= new MapperOptions();
        var files = archivePaths
            .Where(path => !ArchivePathRules.IsDirectoryEntry(path))
            .Select(path => ArchivePathRules.NormalizeEntry(path))
            .Where(path => path.Length > 0)
            .ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!seen.Add(file))
            {
                throw new ZipSlipException($"Duplicate archive path after normalize: {file}");
            }
        }

        var wrapper = DetectWrapper(files);
        var stripped = wrapper is null
            ? files
            : files.Select(file => file[(wrapper.Length + 1)..]).ToList();
        var reshade = LooksLikeReshade(stripped);
        var loosePlugin = !reshade && DetectLoosePluginFolder(stripped);

        var entries = new List<MappedEntry>(stripped.Count);
        var warnings = new List<string>();
        if (wrapper is not null)
        {
            warnings.Add($"Stripped wrapper folder '{wrapper}'.");
        }

        if (reshade)
        {
            warnings.Add("Treated archive as a ReShade / game-root overlay.");
        }
        else if (loosePlugin)
        {
            warnings.Add("Treated a single top-level folder as BepInEx/plugins/<folder>.");
        }

        for (var i = 0; i < stripped.Count; i++)
        {
            var relative = loosePlugin ? "BepInEx/plugins/" + stripped[i] : stripped[i];
            entries.Add(MapOne(files[i], relative, options, warnings, reshade));
        }

        var mapped = entries.Where(entry => entry.Disposition == MapDisposition.Mapped).ToList();
        var tools = entries.Where(entry => entry.Disposition == MapDisposition.ToolNotMerged).ToList();
        var needsConfirm = entries.Any(entry => entry.Disposition == MapDisposition.NeedsConfirm);
        var forbidden = entries.Where(entry => entry.Disposition == MapDisposition.Forbidden).ToList();
        if (forbidden.Count > 0)
        {
            throw new ZipSlipException("Archive targets SPT-owned paths: " + string.Join(", ", forbidden.Select(entry => entry.CanonicalPath)));
        }

        var kind = Classify(mapped, tools);
        var deployable = kind is not PackageKind.Tool && mapped.Count > 0 && (!needsConfirm || options.AllowLowConfidence);
        if (needsConfirm)
        {
            warnings.Add("Low-confidence layout (root DLL). Confirm before import.");
        }

        return new PackageMap(kind, deployable, needsConfirm, wrapper, entries, warnings);
    }

    private static MappedEntry MapOne(
        string original,
        string relative,
        MapperOptions options,
        List<string> warnings,
        bool reshade)
    {
        if (SptDenylist.IsForbidden(relative))
        {
            return new MappedEntry(original, GamePath.Normalize(relative), MapDisposition.Forbidden, "SPT-owned denylist");
        }

        if (!reshade && !relative.Contains('/') && JunkNames.Contains(relative))
        {
            return new MappedEntry(original, null, MapDisposition.SkippedJunk, "Root documentation");
        }

        if (GamePath.IsUnderOrEqual(relative, SptLayout.BepInEx)
            || GamePath.IsUnderOrEqual(relative, SptLayout.SptRuntime)
            || GamePath.IsUnderOrEqual(relative, SptLayout.EscapeFromTarkovData))
        {
            return Finish(original, relative, options);
        }

        if (relative.StartsWith(SptLayout.LegacySptFolder + "/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = relative[(SptLayout.LegacySptFolder.Length + 1)..];
            if (rest.StartsWith("SPT_Data/", StringComparison.OrdinalIgnoreCase) || rest.Equals("SPT_Data", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.AllowSptData)
                {
                    warnings.Add($"Skipped wiki SPT/SPT_Data path: {relative}");
                    return new MappedEntry(original, SptLayout.SptData + rest["SPT_Data".Length..], MapDisposition.SkippedSptData, "SPT_Data requires advanced confirm");
                }

                return Finish(original, SptLayout.SptRuntime + "/" + rest, options);
            }

            return Finish(original, SptLayout.SptRuntime + "/" + rest, options);
        }

        if (relative.StartsWith("user/", StringComparison.OrdinalIgnoreCase) || relative.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            return Finish(original, SptLayout.SptRuntime + "/" + relative, options);
        }

        if (!relative.Contains('/'))
        {
            if (relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (options.ImportTools)
                {
                    return new MappedEntry(original, relative, MapDisposition.Mapped, "Tool imported by request");
                }

                return new MappedEntry(original, relative, MapDisposition.ToolNotMerged, "Root exe is a tool; not merged into the game tree");
            }

            if (RootInjectorDlls.Contains(relative) || reshade)
            {
                return Finish(original, relative, options);
            }

            if (relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (options.AllowLowConfidence)
                {
                    var name = Path.GetFileNameWithoutExtension(relative);
                    return Finish(original, $"BepInEx/plugins/{name}/{relative}", options);
                }

                return new MappedEntry(original, $"BepInEx/plugins/{relative}", MapDisposition.NeedsConfirm, "Root DLL needs confirm");
            }
        }

        if (reshade)
        {
            return Finish(original, relative, options);
        }

        if (options.AllowLowConfidence)
        {
            warnings.Add("Mapped leftover file to the game root: " + relative);
            return Finish(original, relative, options);
        }

        return new MappedEntry(original, null, MapDisposition.NeedsConfirm, "Unrecognized layout");
    }

    private static MappedEntry Finish(string original, string canonical, MapperOptions options)
    {
        var normalized = OverlayPlanner.WrapPluginCanonical(GamePath.Normalize(canonical));
        if (SptDenylist.IsForbidden(normalized))
        {
            if (options.AllowSptData && GamePath.IsUnderOrEqual(normalized, SptLayout.SptData))
            {
                return new MappedEntry(original, normalized, MapDisposition.Mapped, "Advanced SPT_Data overlay");
            }

            return new MappedEntry(original, normalized, MapDisposition.Forbidden, "SPT-owned denylist");
        }

        return new MappedEntry(original, normalized, MapDisposition.Mapped, null);
    }

    private static string? DetectWrapper(List<string> files)
    {
        var tops = files
            .Select(file => file.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tops.Count != 1)
        {
            return null;
        }

        var top = tops[0];
        if (GameRootMarkers.Any(marker => marker.Equals(top, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var inner = files
            .Where(file => file.StartsWith(top + "/", StringComparison.OrdinalIgnoreCase))
            .Select(file => file[(top.Length + 1)..].Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (inner.Any(name => GameRootMarkers.Any(marker => marker.Equals(name, StringComparison.OrdinalIgnoreCase))))
        {
            return top;
        }

        var innerFiles = files
            .Where(file => file.StartsWith(top + "/", StringComparison.OrdinalIgnoreCase))
            .Select(file => file[(top.Length + 1)..])
            .ToList();
        if (LooksLikeReshade(innerFiles))
        {
            return top;
        }

        return null;
    }

    private static bool LooksLikeReshade(List<string> files)
    {
        var hasInjector = files.Any(file =>
            !file.Contains('/', StringComparison.Ordinal)
            && RootInjectorDlls.Contains(file));
        if (!hasInjector)
        {
            return false;
        }

        return files.Any(file =>
            file.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase)
            || file.Equals("reshade-shaders", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("reshade-shaders/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool DetectLoosePluginFolder(List<string> files)
    {
        if (files.Count == 0)
        {
            return false;
        }

        if (files.Any(file => GameRootMarkers.Any(marker =>
                file.Equals(marker, StringComparison.OrdinalIgnoreCase)
                || file.StartsWith(marker + "/", StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        if (!files.Any(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var tops = files
            .Select(file => file.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return tops.Count == 1 && files.Any(file => file.Contains('/', StringComparison.Ordinal));
    }

    private static PackageKind Classify(List<MappedEntry> mapped, List<MappedEntry> tools)
    {
        var hasClient = mapped.Any(entry => entry.CanonicalPath is not null
                                            && GamePath.IsUnderOrEqual(entry.CanonicalPath, SptLayout.BepInEx));
        var hasServer = mapped.Any(entry => entry.CanonicalPath is not null
                                            && (GamePath.IsUnderOrEqual(entry.CanonicalPath, SptLayout.UserMods)
                                                || GamePath.IsUnderOrEqual(entry.CanonicalPath, SptLayout.UserPatchers)));
        if (hasClient && hasServer)
        {
            return PackageKind.Hybrid;
        }

        if (hasClient)
        {
            return PackageKind.Client;
        }

        if (hasServer)
        {
            return PackageKind.Server;
        }

        if (tools.Count > 0)
        {
            return PackageKind.Tool;
        }

        return PackageKind.Unknown;
    }
}
