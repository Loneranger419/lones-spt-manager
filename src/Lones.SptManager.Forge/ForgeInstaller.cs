using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Mapping;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Forge;

public sealed class ForgeInstallResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ModDocument> Documents { get; init; } = [];
}

public sealed class ForgeInstaller
{
    private readonly ForgeClient _client;
    private readonly InstallMapper _mapper = new();

    public ForgeInstaller(ForgeClient client)
    {
        _client = client;
    }

    public async Task<ForgeInstallResult> InstallAsync(
        int modId,
        string managerData,
        string? profileId = null,
        string sptVersion = "4.1.2",
        string? requestedVersion = null,
        bool includeAddons = true,
        bool fetchThumbnails = true,
        string? displayName = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (ForgeRestrictedMods.IsRestricted(modId, name: displayName))
        {
            return Fail(ForgeRestrictedMods.Reason(displayName));
        }

        var versions = await _client.GetVersionsAsync(modId, cancellationToken).ConfigureAwait(false);
        var chosen = requestedVersion is null
            ? ForgeClient.PickVersion(versions)
            : versions.FirstOrDefault(item => string.Equals(item.Version, requestedVersion, StringComparison.OrdinalIgnoreCase));
        if (chosen?.Version is null || chosen.Link is null)
        {
            return Fail("No downloadable Forge version for mod " + modId + ".");
        }

        var warnings = new List<string>();
        if (string.Equals(chosen.FikaCompatibility, "incompatible", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Forge marks this version fika_compatibility=incompatible.");
        }

        var key = modId + ":" + chosen.Version;
        var trees = await _client.GetDependenciesAsync([key], sptVersion, cancellationToken).ConfigureAwait(false);
        var nodes = trees.TryGetValue(key, out var list) ? ForgeClient.Flatten(list) : [];
        var conflict = nodes.FirstOrDefault(node => node.Conflict);
        if (conflict is not null)
        {
            return Fail(
                $"Forge dependency conflict: {conflict.Name ?? conflict.Guid ?? conflict.Id?.ToString()} (AC-C2).",
                warnings);
        }

        var downloads = new List<(int? Id, string? Guid, string? Name, ForgeVersion Version)>
        {
            (modId, null, null, chosen)
        };
        foreach (var node in nodes)
        {
            if (ForgeRestrictedMods.IsRestricted(node))
            {
                warnings.Add(ForgeRestrictedMods.Reason(node.Name));
                continue;
            }

            if (node.LatestCompatibleVersion?.Link is null)
            {
                warnings.Add("Dependency has no compatible version: " + (node.Name ?? node.Guid ?? node.Id?.ToString()));
                continue;
            }

            downloads.Add((node.Id, node.Guid, node.Name, node.LatestCompatibleVersion));
        }

        if (includeAddons)
        {
            var addons = await _client.ListAddonsAsync(modId, cancellationToken).ConfigureAwait(false);
            foreach (var addon in addons)
            {
                if (ForgeRestrictedMods.IsRestricted(addon.Id, slug: addon.Slug, name: addon.Name))
                {
                    warnings.Add(ForgeRestrictedMods.Reason(addon.Name));
                    continue;
                }

                var addonVersion = ForgeClient.PickVersion(addon.Versions);
                if (addonVersion?.Version is null || addonVersion.Link is null)
                {
                    continue;
                }

                var addonKey = addon.Id + ":" + addonVersion.Version;
                var addonTrees = await _client.GetAddonDependenciesAsync([addonKey], sptVersion, cancellationToken)
                    .ConfigureAwait(false);
                var addonNodes = addonTrees.TryGetValue(addonKey, out var addonList) ? ForgeClient.Flatten(addonList) : [];
                var addonConflict = addonNodes.FirstOrDefault(node => node.Conflict);
                if (addonConflict is not null)
                {
                    return Fail(
                        $"Forge addon dependency conflict: {addonConflict.Name ?? addonConflict.Slug} (AC-C2).",
                        warnings);
                }

                downloads.Add((addon.Id, null, addon.Name, addonVersion));
                foreach (var node in addonNodes.Where(item => item.LatestCompatibleVersion?.Link is not null))
                {
                    if (ForgeRestrictedMods.IsRestricted(node))
                    {
                        warnings.Add(ForgeRestrictedMods.Reason(node.Name));
                        continue;
                    }

                    downloads.Add((node.Id, node.Guid, node.Name, node.LatestCompatibleVersion!));
                }
            }
        }

        var documents = new List<ModDocument>();
        var cache = Path.Combine(managerData, "cache", "forge");
        Directory.CreateDirectory(cache);
        var catalogue = await TryGetModAsync(modId, cancellationToken).ConfigureAwait(false);
        if (catalogue is not null && ForgeRestrictedMods.IsRestricted(catalogue))
        {
            return Fail(ForgeRestrictedMods.Reason(catalogue.Name), warnings);
        }

        var thumbnailUrl = ThumbnailCache.IsAllowedUrl(catalogue?.Thumbnail) ? catalogue!.Thumbnail : null;
        var primaryName = FirstNonEmpty(displayName, catalogue?.Name);
        foreach (var item in downloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ForgeRestrictedMods.IsRestricted(item.Id, item.Guid, name: item.Name))
            {
                warnings.Add(ForgeRestrictedMods.Reason(item.Name ?? primaryName));
                continue;
            }

            var label = item.Name ?? primaryName ?? ("mod " + item.Id);
            var dest = Path.Combine(cache, Sanitize(item.Id + "-" + item.Version.Version) + ".zip");
            var downloadProgress = status is null
                ? null
                : new Progress<DownloadProgress>(update => status.Report("Downloading " + label + " — " + update.Display));
            status?.Report("Downloading " + label + "…");
            await _client.DownloadAsync(
                    item.Version.Link!,
                    dest,
                    item.Version.ContentLength,
                    downloadProgress,
                    cancellationToken)
                .ConfigureAwait(false);
            status?.Report("Extracting " + label + "…");
            var imported = await _mapper.ImportArchiveAsync(
                    dest,
                    managerData,
                    new MapperOptions
                    {
                        AllowLowConfidence = true,
                        ForgeModId = item.Id,
                        ForgeGuid = item.Guid,
                        Version = item.Version.Version,
                        ModKey = item.Name ?? (item.Id == modId ? primaryName : null),
                        DisplayName = item.Name ?? (item.Id == modId ? primaryName : null),
                        ThumbnailUrl = item.Id == modId ? thumbnailUrl : null
                    },
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
            if (imported.Document is null)
            {
                return Fail(imported.Message ?? "Import failed for " + item.Version.Link, warnings);
            }

            documents.Add(imported.Document);
            if (imported.Map.NeedsConfirm)
            {
                warnings.Add(label + ": imported a low-confidence archive layout.");
            }
            if (item.Id == modId && thumbnailUrl is not null && fetchThumbnails)
            {
                await TryCacheThumbnailAsync(managerData, thumbnailUrl, cancellationToken).ConfigureAwait(false);
            }

            if (profileId is not null)
            {
                EnableOnProfile(managerData, profileId, imported.Document);
            }
        }

        return new ForgeInstallResult
        {
            Success = true,
            Message = $"Installed {documents.Count} package(s) from Forge into the store.",
            Warnings = warnings,
            Documents = documents
        };
    }

    public async Task<string> CheckUpdatesAsync(string managerData, string sptVersion, CancellationToken cancellationToken = default)
    {
        var pairs = ModStore.List(managerData)
            .Where(document => document.ForgeModId is not null || !string.IsNullOrWhiteSpace(document.ForgeGuid))
            .Select(document => (document.ForgeModId?.ToString() ?? document.ForgeGuid!) + ":" + document.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pairs.Length == 0)
        {
            return "No Forge-tagged store packages to check.";
        }

        var updates = await _client.GetUpdatesAsync(pairs, sptVersion, cancellationToken).ConfigureAwait(false);
        var lines = new List<string>();
        foreach (var blocked in updates.BlockedUpdates)
        {
            var reason = blocked.Reason ?? blocked.BlockedReason ?? "blocked";
            lines.Add($"Blocked: {blocked.CurrentVersion?.Name ?? blocked.CurrentVersion?.Guid} — {reason}");
        }

        foreach (var offer in updates.Updates)
        {
            lines.Add(
                $"Update: {offer.CurrentVersion?.Name ?? offer.CurrentVersion?.Guid} {offer.CurrentVersion?.Version} → {offer.RecommendedVersion?.Version} ({offer.UpdateReason})");
        }

        if (lines.Count == 0)
        {
            return updates.UpToDate.Count > 0
                ? $"All {updates.UpToDate.Count} Forge package(s) are up to date for SPT {sptVersion}."
                : "No Forge updates returned.";
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task<ForgeMod?> TryGetModAsync(int modId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetModAsync(modId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private async Task TryCacheThumbnailAsync(string managerData, string url, CancellationToken cancellationToken)
    {
        try
        {
            var dest = ThumbnailCache.LocalPathFor(managerData, url);
            if (File.Exists(dest))
            {
                return;
            }

            await _client.DownloadAsync(url, dest, expectedContentLength: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Thumbnails must not fail an install.
        }
    }

    private static void EnableOnProfile(string managerData, string profileId, ModDocument document)
    {
        if (!document.Deployable)
        {
            return;
        }

        InstallInventory.AddToLoadOrder(managerData, profileId, document.ModKey, document.Version);
    }

    private static string Sanitize(string value)
        => string.Concat(value.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));

    private static ForgeInstallResult Fail(string message, IReadOnlyList<string>? warnings = null)
        => new() { Success = false, Message = message, Warnings = warnings ?? [] };
}
