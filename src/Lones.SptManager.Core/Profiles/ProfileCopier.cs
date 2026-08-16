using System.Text.Json;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Profiles;

public sealed class ProfileCopyResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? DestinationId { get; init; }
    public bool SavesLookEmpty { get; init; }
}

public sealed class ProfileCopyOptions
{
    public static ProfileCopyOptions All { get; } = new()
    {
        EnabledMods = true,
        Saves = true,
        BepInExConfig = true,
        Overwrite = true,
        RuntimeFiles = true,
        LaunchSettings = true
    };

    public bool EnabledMods { get; init; }
    public bool Saves { get; init; }
    public bool BepInExConfig { get; init; }
    public bool Overwrite { get; init; }
    public bool RuntimeFiles { get; init; }
    public bool LaunchSettings { get; init; }
}

public static class ProfileCopier
{
    public static ProfileCopyResult Copy(string managerData, string sourceId, string destinationId)
        => Copy(managerData, sourceId, destinationId, ProfileCopyOptions.All);

    public static ProfileCopyResult Copy(
        string managerData,
        string sourceId,
        string destinationId,
        ProfileCopyOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        sourceId = ProfilePaths.Sanitize(sourceId);
        destinationId = ProfilePaths.Sanitize(destinationId);
        if (sourceId.Equals(destinationId, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileCopyResult { Success = false, Message = "Source and destination profile are the same." };
        }

        var sourceRoot = ProfilePaths.ProfileRoot(managerData, sourceId);
        if (!Directory.Exists(sourceRoot))
        {
            return new ProfileCopyResult { Success = false, Message = "Source profile does not exist: " + sourceId };
        }

        var destRoot = ProfilePaths.ProfileRoot(managerData, destinationId);
        if (Directory.Exists(destRoot) && Directory.EnumerateFileSystemEntries(destRoot).Any())
        {
            return new ProfileCopyResult { Success = false, Message = "Destination profile already exists: " + destinationId };
        }

        Directory.CreateDirectory(destRoot);
        if (options.Saves)
        {
            CopyScoped(ProfilePaths.Saves(managerData, sourceId), ProfilePaths.Saves(managerData, destinationId));
        }

        if (options.BepInExConfig)
        {
            CopyScoped(ProfilePaths.BepInExConfig(managerData, sourceId), ProfilePaths.BepInExConfig(managerData, destinationId));
        }

        if (options.Overwrite)
        {
            CopyScoped(ProfilePaths.Overwrite(managerData, sourceId), ProfilePaths.Overwrite(managerData, destinationId));
        }

        if (options.RuntimeFiles)
        {
            ProfileRuntimeStore.CopyAll(managerData, sourceId, destinationId);
        }

        var sourceProfile = new ProfileStore().LoadOrCreate(managerData, sourceId);
        var copied = new ProfileDocument
        {
            ProfileId = destinationId,
            Enabled = options.EnabledMods ? sourceProfile.Enabled : [],
            LaunchMode = options.LaunchSettings ? sourceProfile.LaunchMode : "solo",
            JoinUrl = options.LaunchSettings ? sourceProfile.JoinUrl : null,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(
            ProfilePaths.ProfileJson(managerData, destinationId),
            JsonSerializer.Serialize(copied, ProfileStore.JsonOptions));

        var saves = ProfilePaths.Saves(managerData, destinationId);
        var savesLookEmpty = !Directory.Exists(saves)
                             || !Directory.EnumerateFiles(saves, "*.json", SearchOption.TopDirectoryOnly).Any();
        var copiedParts = Describe(options);
        var message = $"Copied profile {sourceId} → {destinationId} ({copiedParts}).";
        if (options.Saves && savesLookEmpty)
        {
            message += " Local saves look empty (normal for Fika join; the host backend holds characters).";
        }

        return new ProfileCopyResult
        {
            Success = true,
            Message = message,
            DestinationId = destinationId,
            SavesLookEmpty = savesLookEmpty
        };
    }

    public static ProfileCopyResult CopyRuntimeMod(
        string managerData,
        string sourceId,
        string destinationId,
        string modKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        ArgumentException.ThrowIfNullOrWhiteSpace(modKey);
        sourceId = ProfilePaths.Sanitize(sourceId);
        destinationId = ProfilePaths.Sanitize(destinationId);
        if (sourceId.Equals(destinationId, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileCopyResult { Success = false, Message = "Source and destination profile are the same." };
        }

        if (new ProfileStore().TryRead(managerData, destinationId) is null
            && !Directory.Exists(ProfilePaths.ProfileRoot(managerData, destinationId)))
        {
            return new ProfileCopyResult { Success = false, Message = "Destination profile does not exist: " + destinationId };
        }

        try
        {
            ProfileRuntimeStore.CopyMod(managerData, sourceId, destinationId, modKey);
        }
        catch (Exception ex)
        {
            return new ProfileCopyResult { Success = false, Message = ex.Message };
        }

        return new ProfileCopyResult
        {
            Success = true,
            Message = $"Copied {modKey} generated files from {sourceId} → {destinationId}. Deploy that profile to apply.",
            DestinationId = destinationId
        };
    }

    private static string Describe(ProfileCopyOptions options)
    {
        var parts = new List<string>();
        if (options.Saves)
        {
            parts.Add("saves");
        }

        if (options.BepInExConfig)
        {
            parts.Add("BepInEx config");
        }

        if (options.Overwrite)
        {
            parts.Add("Overwrite");
        }

        if (options.RuntimeFiles)
        {
            parts.Add("generated files");
        }

        if (options.EnabledMods)
        {
            parts.Add("enabled list");
        }

        return parts.Count == 0 ? "profile record only" : string.Join(", ", parts);
    }

    private static void CopyScoped(string source, string dest)
    {
        if (!Directory.Exists(source) || NtfsLinks.IsJunction(source))
        {
            return;
        }

        IsolatedOverlay.CopyDirectoryNoFollow(source, dest, skipExisting: false);
    }
}
