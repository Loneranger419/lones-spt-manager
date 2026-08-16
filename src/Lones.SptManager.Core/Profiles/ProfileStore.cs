using System.Text.Json;
using System.Text.Json.Serialization;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Profiles;

public sealed class ProfileDocument
{
    public int ManifestVersion { get; init; } = ProductInfo.ManifestVersion;
    public required string ProfileId { get; init; }
    public IReadOnlyList<EnabledMod> Enabled { get; init; } = [];
    public string LaunchMode { get; init; } = "solo";
    public string? JoinUrl { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public static class ProfilePaths
{
    public const string DefaultProfileId = "default";
    public const string StagingMarkerName = ".lones-owned";

    public static string ProfilesRoot(string managerData) => Path.Combine(managerData, "profiles");

    public static string ProfileRoot(string managerData, string profileId)
        => Path.Combine(ProfilesRoot(managerData), Sanitize(profileId));

    public static string Staging(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "staging");

    public static string Manifest(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "deploy-manifest.json");

    public static string Journal(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "deploy-staging.json");

    public static string ProfileJson(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "profile.json");

    public static string DeployLog(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "deploy.jsonl");

    public static string DeployHumanLog(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "deploy.log");

    public static string Saves(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "saves");

    public static string BepInExConfig(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "bepinex-config");

    public static string Overwrite(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "overwrite");

    public static string HarvestBaseline(string managerData, string profileId)
        => Path.Combine(ProfileRoot(managerData, profileId), "harvest-baseline.json");

    public static IReadOnlyList<string> ListProfileIds(string managerData)
    {
        var root = ProfilesRoot(managerData);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> ProfileIdsWithJournal(string managerData)
    {
        var root = ProfilesRoot(managerData);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Where(dir => File.Exists(Path.Combine(dir, "deploy-staging.json")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToArray();
    }

    public static string Sanitize(string profileId)
    {
        var cleaned = string.Concat(profileId.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
        return cleaned.Length == 0 ? DefaultProfileId : cleaned;
    }
}

public sealed class ProfileStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProfileDocument? TryRead(string managerData, string profileId)
    {
        var path = ProfilePaths.ProfileJson(managerData, ProfilePaths.Sanitize(profileId));
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(path), JsonOptions);
    }

    public ProfileDocument Save(
        string managerData,
        string profileId,
        IReadOnlyList<EnabledMod> enabled,
        string? launchMode = null,
        string? joinUrl = null)
    {
        var id = ProfilePaths.Sanitize(profileId);
        var existing = TryRead(managerData, id);
        Directory.CreateDirectory(ProfilePaths.ProfileRoot(managerData, id));
        var document = new ProfileDocument
        {
            ProfileId = id,
            Enabled = enabled,
            LaunchMode = launchMode ?? existing?.LaunchMode ?? "solo",
            JoinUrl = joinUrl ?? existing?.JoinUrl,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(ProfilePaths.ProfileJson(managerData, id), JsonSerializer.Serialize(document, JsonOptions));
        return document;
    }

    public ProfileDocument LoadOrCreate(string managerData, string profileId)
    {
        var id = ProfilePaths.Sanitize(profileId);
        var path = ProfilePaths.ProfileJson(managerData, id);
        if (File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(path), JsonOptions);
            if (existing is not null)
            {
                return existing;
            }
        }

        return Save(managerData, id, []);
    }

    public static ProfileCopyResult Rename(string managerData, string sourceId, string destinationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        sourceId = ProfilePaths.Sanitize(sourceId);
        destinationId = ProfilePaths.Sanitize(destinationId);
        if (sourceId.Equals(destinationId, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileCopyResult { Success = false, Message = "New profile name is the same as the current one." };
        }

        var sourceRoot = ProfilePaths.ProfileRoot(managerData, sourceId);
        if (!Directory.Exists(sourceRoot))
        {
            return new ProfileCopyResult { Success = false, Message = "Profile does not exist: " + sourceId };
        }

        var destRoot = ProfilePaths.ProfileRoot(managerData, destinationId);
        if (Directory.Exists(destRoot))
        {
            return new ProfileCopyResult { Success = false, Message = "A profile named '" + destinationId + "' already exists." };
        }

        Directory.Move(sourceRoot, destRoot);
        var store = new ProfileStore();
        var existing = store.TryRead(managerData, destinationId);
        store.Save(
            managerData,
            destinationId,
            existing?.Enabled ?? [],
            existing?.LaunchMode,
            existing?.JoinUrl);

        return new ProfileCopyResult
        {
            Success = true,
            Message = $"Renamed profile {sourceId} → {destinationId}. Deploy to retarget junctions.",
            DestinationId = destinationId
        };
    }

    public static ProfileCopyResult Delete(string managerData, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        profileId = ProfilePaths.Sanitize(profileId);
        var ids = ProfilePaths.ListProfileIds(managerData);
        if (ids.Count <= 1)
        {
            return new ProfileCopyResult { Success = false, Message = "Can't delete the last profile." };
        }

        var root = ProfilePaths.ProfileRoot(managerData, profileId);
        if (!Directory.Exists(root))
        {
            return new ProfileCopyResult { Success = false, Message = "Profile does not exist: " + profileId };
        }

        SafeFileSystem.DeleteDirectoryNoFollow(root);
        return new ProfileCopyResult
        {
            Success = true,
            Message = "Deleted profile " + profileId + "."
        };
    }
}
