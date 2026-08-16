using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Profiles;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;

namespace Lones.SptManager.Core.Deploy;

public sealed class DeployEngine
{
    private static readonly JsonSerializerOptions JsonOptions = ProfileStore.JsonOptions;
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IProcessLock _processLock;
    private readonly ProfileStore _profiles = new();

    public DeployEngine()
        : this(new SptProcessLock())
    {
    }

    public DeployEngine(IProcessLock processLock)
    {
        _processLock = processLock;
    }

    public DeployResult Deploy(DeployRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManagerData);

        var gameRoot = Path.GetFullPath(request.GameRoot);
        var managerData = Path.GetFullPath(request.ManagerData);
        var profileId = ProfilePaths.Sanitize(request.ProfileId);
        var running = _processLock.RunningSptProcesses();
        if (running.Count > 0)
        {
            return new DeployResult
            {
                Status = DeployStatus.BlockedProcesses,
                Message = "Deploy blocked while SPT processes are running: " + string.Join(", ", running),
                RunningProcesses = running
            };
        }

        if (!File.Exists(GamePath.Combine(gameRoot, SptLayout.EscapeFromTarkovExe))
            || !Directory.Exists(GamePath.Combine(gameRoot, SptLayout.SptRuntime)))
        {
            return Fail("Game root is not an SPT 4.1 layout (need EscapeFromTarkov.exe and SPT_Runtime).");
        }

        var journalPath = ProfilePaths.Journal(managerData, profileId);
        if (File.Exists(journalPath))
        {
            var recovered = Reconcile(managerData, profileId);
            if (recovered.Status == DeployStatus.Failed)
            {
                return recovered;
            }
        }

        IReadOnlyList<EnabledMod> enabled;
        try
        {
            ProfileRuntimeStore.ImportLegacyStoreRuntime(managerData, profileId);
            enabled = ResolveEnabled(managerData, profileId, request.Enabled);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }

        var baseline = request.Baseline ?? new SptOwnedBaselineBuilder().Build(gameRoot);
        var fingerprint = ComputeFingerprint(gameRoot, profileId, enabled, managerData);
        var last = TryReadManifest(ProfilePaths.Manifest(managerData, profileId));
        if (last is not null
            && last.Fingerprint == fingerprint
            && JunctionsMatch(gameRoot, last)
            && BaselineUntouched(gameRoot, baseline))
        {
            return new DeployResult
            {
                Status = DeployStatus.Idempotent,
                Message = "Deploy unchanged; no mutations.",
                Junctions = last.Junctions,
                Conflicts = last.Conflicts
            };
        }

        Directory.CreateDirectory(ProfilePaths.ProfileRoot(managerData, profileId));
        var stagingRoot = ProfilePaths.Staging(managerData, profileId);
        var planned = new DeployManifest
        {
            ProfileId = profileId,
            GameRoot = gameRoot,
            Fingerprint = fingerprint,
            WrittenAtUtc = DateTimeOffset.UtcNow,
            Enabled = enabled
        };
        WriteJson(journalPath, planned);
        AppendLog(managerData, profileId, new { ts = DateTimeOffset.UtcNow, op = "journal", fingerprint, enabled });

        try
        {
            PurgeOwned(managerData, profileId, gameRoot, stagingRoot, planned, last);
            var merge = RebuildStaging(managerData, profileId, stagingRoot, enabled);
            var plan = BuildPlan(managerData, profileId, stagingRoot);
            EnsureTargetsUnderProfile(plan.Junctions, managerData, profileId);

            var committed = new DeployManifest
            {
                ProfileId = profileId,
                GameRoot = gameRoot,
                Fingerprint = fingerprint,
                WrittenAtUtc = DateTimeOffset.UtcNow,
                Enabled = enabled,
                Junctions = plan.Junctions,
                CopiedFiles = plan.CopiedFiles,
                StagedFiles = merge.Files,
                Conflicts = merge.Conflicts
            };
            WriteJson(journalPath, committed);

            ApplyOverlays(managerData, profileId, gameRoot, stagingRoot, committed);
            EnsureStockUserDirs(gameRoot, committed);
            Verify(gameRoot, managerData, profileId, stagingRoot, committed, baseline);
            HarvestEngine.WriteBaseline(managerData, profileId);

            WriteJson(ProfilePaths.Manifest(managerData, profileId), committed);
            File.Delete(journalPath);
            _profiles.Save(managerData, profileId, enabled);
            AppendLog(managerData, profileId, new { ts = DateTimeOffset.UtcNow, op = "commit", fingerprint, junctions = committed.Junctions.Count });
            var summary = Summarize(committed, merge.Warnings);
            AppendHumanLog(managerData, profileId, summary);

            return new DeployResult
            {
                Status = DeployStatus.Success,
                Message = summary,
                Junctions = committed.Junctions,
                Conflicts = committed.Conflicts,
                Warnings = merge.Warnings
            };
        }
        catch (Exception ex)
        {
            var explained = OverlayFailure.Explain(ex);
            AppendLog(managerData, profileId, new { ts = DateTimeOffset.UtcNow, op = "failed", error = explained });
            AppendHumanLog(managerData, profileId, "FAILED " + explained);
            return Fail(explained);
        }
    }

    public DeployResult Reconcile(string managerData, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        profileId = ProfilePaths.Sanitize(profileId);
        managerData = Path.GetFullPath(managerData);
        var journalPath = ProfilePaths.Journal(managerData, profileId);
        if (!File.Exists(journalPath))
        {
            return new DeployResult { Status = DeployStatus.Clean, Message = "No in-flight deploy." };
        }

        var journal = TryReadManifest(journalPath);
        var last = TryReadManifest(ProfilePaths.Manifest(managerData, profileId));
        var gameRoot = journal?.GameRoot ?? last?.GameRoot;
        var stagingRoot = ProfilePaths.Staging(managerData, profileId);

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            File.Delete(journalPath);
            return new DeployResult
            {
                Status = DeployStatus.Recovered,
                Message = "Removed stale deploy journal; game root is missing."
            };
        }

        gameRoot = Path.GetFullPath(gameRoot);
        try
        {
            PurgeOwned(managerData, profileId, gameRoot, stagingRoot, journal, last);
            if (last is not null)
            {
                var merge = RebuildStaging(managerData, profileId, stagingRoot, last.Enabled);
                var plan = BuildPlan(managerData, profileId, stagingRoot);
                var restored = new DeployManifest
                {
                    ProfileId = last.ProfileId,
                    GameRoot = gameRoot,
                    Fingerprint = last.Fingerprint,
                    WrittenAtUtc = DateTimeOffset.UtcNow,
                    Enabled = last.Enabled,
                    Junctions = plan.Junctions,
                    CopiedFiles = plan.CopiedFiles,
                    StagedFiles = merge.Files,
                    Conflicts = merge.Conflicts
                };
                ApplyOverlays(managerData, profileId, gameRoot, stagingRoot, restored);
                EnsureStockUserDirs(gameRoot, restored);
                var baseline = new SptOwnedBaselineBuilder().Build(gameRoot);
                Verify(gameRoot, managerData, profileId, stagingRoot, restored, baseline);
                HarvestEngine.WriteBaseline(managerData, profileId);
                WriteJson(ProfilePaths.Manifest(managerData, profileId), restored);
            }
            else
            {
                if (Directory.Exists(stagingRoot) && !NtfsLinks.IsJunction(stagingRoot))
                {
                    SafeFileSystem.DeleteDirectoryNoFollow(stagingRoot);
                }

                EnsureStockUserDirs(gameRoot, junctions: []);
            }

            File.Delete(journalPath);
            AppendLog(managerData, profileId, new { ts = DateTimeOffset.UtcNow, op = "reconcile", restored = last is not null });
            return new DeployResult
            {
                Status = DeployStatus.Recovered,
                Message = last is null
                    ? "Rolled back in-flight deploy; no prior manifest."
                    : "Rolled back in-flight deploy to the last committed manifest.",
                Junctions = last?.Junctions ?? []
            };
        }
        catch (Exception ex)
        {
            return Fail("Repair failed: " + ex.Message);
        }
    }

    public DeployResult DetachAll(string gameRoot, string managerData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);
        gameRoot = Path.GetFullPath(gameRoot);
        managerData = Path.GetFullPath(managerData);

        var running = _processLock.RunningSptProcesses();
        if (running.Count > 0)
        {
            return new DeployResult
            {
                Status = DeployStatus.BlockedProcesses,
                Message = "Detach blocked while SPT processes are running: " + string.Join(", ", running),
                RunningProcesses = running
            };
        }

        if (!Directory.Exists(gameRoot))
        {
            return new DeployResult { Status = DeployStatus.Clean, Message = "Game root is missing; nothing to detach." };
        }

        var ids = ProfilePaths.ListProfileIds(managerData);
        if (ids.Count == 0)
        {
            ids = [ProfilePaths.DefaultProfileId];
        }

        foreach (var profileId in ids)
        {
            var stagingRoot = ProfilePaths.Staging(managerData, profileId);
            var journal = TryReadManifest(ProfilePaths.Journal(managerData, profileId));
            var last = TryReadManifest(ProfilePaths.Manifest(managerData, profileId));
            PurgeOwned(managerData, profileId, gameRoot, stagingRoot, journal, last);
        }

        EnsureStockUserDirs(gameRoot, junctions: []);
        var config = GamePath.Combine(gameRoot, SptLayout.BepInExConfig);
        if (!Directory.Exists(config) && !NtfsLinks.IsJunction(config))
        {
            Directory.CreateDirectory(config);
        }

        return new DeployResult
        {
            Status = DeployStatus.Success,
            Message = "Removed manager junctions from the SPT install."
        };
    }

    public DeployResult ReconcileAll(string managerData)
    {
        var messages = new List<string>();
        var anyFailed = false;
        var anyRecovered = false;
        foreach (var id in ProfilePaths.ProfileIdsWithJournal(managerData))
        {
            var result = Reconcile(managerData, id);
            messages.Add($"{id}: {result.Message}");
            anyFailed |= result.Status == DeployStatus.Failed;
            anyRecovered |= result.Status == DeployStatus.Recovered;
        }

        if (messages.Count == 0)
        {
            return new DeployResult { Status = DeployStatus.Clean, Message = "No in-flight deploy." };
        }

        return new DeployResult
        {
            Status = anyFailed ? DeployStatus.Failed : anyRecovered ? DeployStatus.Recovered : DeployStatus.Clean,
            Message = string.Join(Environment.NewLine, messages)
        };
    }

    private static IReadOnlyList<EnabledMod> ResolveEnabled(string managerData, string profileId, IReadOnlyList<EnabledMod>? requested)
    {
        if (requested is not null)
        {
            foreach (var mod in requested)
            {
                if (ModStore.TryRead(managerData, mod.ModKey, mod.Version) is null)
                {
                    throw new InvalidOperationException($"Store package missing: {mod.ModKey} {mod.Version}");
                }
            }

            return RuntimeAttachment.DeployableOnly(requested);
        }

        var profile = new ProfileStore().TryRead(managerData, profileId);
        if (profile is not null)
        {
            return RuntimeAttachment.DeployableOnly(profile.Enabled);
        }

        return RuntimeAttachment.WithoutStoreRuntime(RuntimeAttachment.AllDeployable(managerData));
    }

    private static StagingMergeResult RebuildStaging(
        string managerData,
        string profileId,
        string stagingRoot,
        IReadOnlyList<EnabledMod> enabled)
    {
        var merge = StagingMerger.Rebuild(managerData, stagingRoot, enabled);
        merge = StagingMerger.ApplyProfileRuntime(managerData, profileId, stagingRoot, enabled, merge);
        return StagingMerger.ApplyOverwrite(stagingRoot, ProfilePaths.Overwrite(managerData, profileId), merge);
    }

    private static OverlayPlan BuildPlan(string managerData, string profileId, string stagingRoot)
        => IsolatedOverlay.Combine(OverlayPlanner.FromStaging(stagingRoot), IsolatedOverlay.Plan(managerData, profileId));

    private static string ComputeFingerprint(string gameRoot, string profileId, IReadOnlyList<EnabledMod> enabled, string managerData)
    {
        using var sha = SHA256.Create();
        void Add(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        Add(gameRoot.ToUpperInvariant());
        Add("\n");
        Add(profileId);
        Add("\n");
        foreach (var mod in enabled.OrderBy(item => item.Priority).ThenBy(item => item.ModKey, StringComparer.OrdinalIgnoreCase))
        {
            Add($"{mod.ModKey}\0{mod.Version}\0{mod.Priority}\n");
            var document = ModStore.TryRead(managerData, mod.ModKey, mod.Version);
            if (document is null)
            {
                continue;
            }

            foreach (var file in document.Files.OrderBy(item => item.CanonicalPath, StringComparer.OrdinalIgnoreCase))
            {
                Add($"{file.CanonicalPath}\0{file.Sha256}\n");
            }
        }

        foreach (var runtime in ProfileRuntimeStore.List(managerData, profileId)
                     .OrderBy(item => item.ModKey, StringComparer.OrdinalIgnoreCase))
        {
            Add($"runtime\0{runtime.ModKey}\n");
            foreach (var file in runtime.Files.OrderBy(item => item.CanonicalPath, StringComparer.OrdinalIgnoreCase))
            {
                Add($"{file.CanonicalPath}\0{file.Sha256}\n");
            }
        }

        var overwriteRoot = ProfilePaths.Overwrite(managerData, profileId);
        if (Directory.Exists(overwriteRoot))
        {
            foreach (var file in Directory.EnumerateFiles(overwriteRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Add(GamePath.Normalize(Path.GetRelativePath(overwriteRoot, file)));
                Add("\0");
                Add(HashFile(file));
                Add("\n");
            }
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static void PurgeOwned(
        string managerData,
        string profileId,
        string gameRoot,
        string stagingRoot,
        DeployManifest? journal,
        DeployManifest? last)
    {
        var installPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in (journal?.Junctions ?? []).Concat(last?.Junctions ?? []))
        {
            installPaths.Add(GamePath.Combine(gameRoot, record.InstallRelative));
        }

        foreach (var path in DiscoverOwnedJunctions(gameRoot, managerData, stagingRoot))
        {
            installPaths.Add(path);
        }

        foreach (var path in installPaths.OrderByDescending(item => item.Length))
        {
            if (IsProtectedInstallPath(gameRoot, path))
            {
                continue;
            }

            if (NtfsLinks.IsJunction(path))
            {
                NtfsLinks.RemoveJunction(path);
                AppendLog(managerData, profileId, new { ts = DateTimeOffset.UtcNow, op = "remove-junction", install = path });
            }
        }

        foreach (var copy in (journal?.CopiedFiles ?? []).Concat(last?.CopiedFiles ?? []))
        {
            var path = GamePath.Combine(gameRoot, copy.InstallRelative);
            if (File.Exists(path) && !SptDenylist.IsForbidden(copy.InstallRelative))
            {
                File.Delete(path);
            }
        }
    }

    private static IReadOnlyList<string> DiscoverOwnedJunctions(string gameRoot, string managerData, string stagingRoot)
    {
        var found = new List<string>();
        var profilesRoot = ProfilePaths.ProfilesRoot(managerData);
        void Consider(string path)
        {
            if (!NtfsLinks.IsJunction(path) || IsProtectedInstallPath(gameRoot, path))
            {
                return;
            }

            var target = NtfsLinks.TryGetJunctionTarget(path);
            if (target is null)
            {
                return;
            }

            if (SafeFileSystem.IsUnderDirectory(target, stagingRoot)
                || SafeFileSystem.SamePath(target, stagingRoot)
                || SafeFileSystem.IsUnderDirectory(target, profilesRoot)
                || SafeFileSystem.SamePath(target, profilesRoot))
            {
                found.Add(path);
            }
        }

        Consider(GamePath.Combine(gameRoot, SptLayout.UserMods));
        Consider(GamePath.Combine(gameRoot, SptLayout.UserPatchers));
        Consider(GamePath.Combine(gameRoot, SptLayout.UserProfiles));
        Consider(GamePath.Combine(gameRoot, SptLayout.BepInExConfig));
        var plugins = GamePath.Combine(gameRoot, OverlayPlanner.BepInExPlugins);
        if (Directory.Exists(plugins))
        {
            foreach (var child in Directory.EnumerateDirectories(plugins))
            {
                Consider(child);
            }
        }

        return found;
    }

    private static bool IsProtectedInstallPath(string gameRoot, string fullPath)
    {
        var relative = GamePath.Normalize(Path.GetRelativePath(gameRoot, fullPath));
        return SptDenylist.IsForbidden(relative)
               || GamePath.EqualsNormalized(relative, SptLayout.BepInExPluginsSpt)
               || GamePath.EqualsNormalized(relative, OverlayPlanner.BepInExPlugins)
               || GamePath.EqualsNormalized(relative, SptLayout.BepInEx);
    }

    private static void ApplyOverlays(
        string managerData,
        string profileId,
        string gameRoot,
        string stagingRoot,
        DeployManifest manifest)
    {
        var profilesRoot = ProfilePaths.ProfilesRoot(managerData);
        foreach (var junction in manifest.Junctions)
        {
            var installPath = GamePath.Combine(gameRoot, junction.InstallRelative);
            if (SafeFileSystem.IsUnderDirectory(junction.TargetFull, stagingRoot)
                || SafeFileSystem.SamePath(junction.TargetFull, stagingRoot))
            {
                if (OverlayPlanner.IsPluginOverlayDirectory(junction.InstallRelative)
                    && Directory.Exists(installPath)
                    && !NtfsLinks.IsJunction(installPath))
                {
                    IsolatedOverlay.ClaimInstallDirectory(installPath, junction.TargetFull, profilesRoot);
                }
                else
                {
                    PrepareJunctionSource(installPath);
                }
            }
            else
            {
                IsolatedOverlay.ClaimInstallDirectory(installPath, junction.TargetFull, profilesRoot);
            }

            Directory.CreateDirectory(junction.TargetFull);
            NtfsLinks.CreateJunction(installPath, junction.TargetFull);
            AppendLog(managerData, profileId, new
            {
                ts = DateTimeOffset.UtcNow,
                op = "create-junction",
                install = junction.InstallRelative,
                target = junction.TargetFull
            });
        }

        foreach (var copy in manifest.CopiedFiles)
        {
            if (SptDenylist.IsForbidden(copy.InstallRelative))
            {
                continue;
            }

            var dest = GamePath.Combine(gameRoot, copy.InstallRelative);
            var source = GamePath.Combine(stagingRoot, copy.InstallRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (File.Exists(dest))
            {
                throw new IOException($"Refusing to overwrite existing file that is not a managed copy: {copy.InstallRelative}");
            }

            File.Copy(source, dest);
        }
    }

    private static void PrepareJunctionSource(string installPath)
    {
        if (NtfsLinks.IsJunction(installPath))
        {
            NtfsLinks.RemoveJunction(installPath);
            return;
        }

        if (!Directory.Exists(installPath) && !File.Exists(installPath))
        {
            return;
        }

        if (File.Exists(installPath))
        {
            throw new IOException($"Overlay path is a file, not an empty directory: {installPath}");
        }

        if (Directory.EnumerateFileSystemEntries(installPath).Any())
        {
            throw new IOException(
                "Overlay directory must be empty or already a manager junction before deploy: " + installPath);
        }

        Directory.Delete(installPath);
    }

    private static void EnsureStockUserDirs(string gameRoot, DeployManifest? manifest)
        => EnsureStockUserDirs(gameRoot, manifest?.Junctions ?? []);

    private static void EnsureStockUserDirs(string gameRoot, IReadOnlyList<JunctionRecord> junctions)
    {
        var junctioned = junctions
            .Select(item => GamePath.Normalize(item.InstallRelative))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in new[] { SptLayout.UserMods, SptLayout.UserPatchers, SptLayout.UserProfiles })
        {
            var path = GamePath.Combine(gameRoot, relative);
            if (junctioned.Contains(GamePath.Normalize(relative)))
            {
                continue;
            }

            if (!Directory.Exists(path) && !NtfsLinks.IsJunction(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }

    private static void EnsureTargetsUnderProfile(IReadOnlyList<JunctionRecord> junctions, string managerData, string profileId)
    {
        var profileRoot = ProfilePaths.ProfileRoot(managerData, profileId);
        foreach (var junction in junctions)
        {
            if (!SafeFileSystem.IsUnderDirectory(junction.TargetFull, profileRoot)
                && !SafeFileSystem.SamePath(junction.TargetFull, profileRoot))
            {
                throw new InvalidOperationException("Refusing junction target outside the profile folder: " + junction.TargetFull);
            }
        }
    }

    private static void Verify(
        string gameRoot,
        string managerData,
        string profileId,
        string stagingRoot,
        DeployManifest manifest,
        SptOwnedBaseline baseline)
    {
        var profileRoot = ProfilePaths.ProfileRoot(managerData, profileId);
        foreach (var junction in manifest.Junctions)
        {
            var installPath = GamePath.Combine(gameRoot, junction.InstallRelative);
            if (!NtfsLinks.IsJunction(installPath))
            {
                throw new InvalidOperationException("Expected junction missing: " + junction.InstallRelative);
            }

            var target = NtfsLinks.TryGetJunctionTarget(installPath);
            if (target is null || !SafeFileSystem.SamePath(target, junction.TargetFull))
            {
                throw new InvalidOperationException(
                    $"Junction target mismatch for {junction.InstallRelative}: {target} != {junction.TargetFull}");
            }

            if (!SafeFileSystem.IsUnderDirectory(target, profileRoot) && !SafeFileSystem.SamePath(target, profileRoot))
            {
                throw new InvalidOperationException("Junction target escaped the profile folder: " + target);
            }

            var samples = Directory.Exists(junction.TargetFull)
                ? Directory.EnumerateFiles(junction.TargetFull, "*", SearchOption.AllDirectories).Take(3)
                : [];
            foreach (var stagingFile in samples)
            {
                var relative = Path.GetRelativePath(junction.TargetFull, stagingFile);
                var throughInstall = Path.Combine(installPath, relative);
                if (!File.Exists(throughInstall))
                {
                    throw new InvalidOperationException("Staged file not visible through junction: " + relative);
                }

                if (!string.Equals(HashFile(stagingFile), HashFile(throughInstall), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Sample hash mismatch through junction: " + relative);
                }
            }
        }

        if (!BaselineUntouched(gameRoot, baseline))
        {
            throw new InvalidOperationException("SPT-owned baseline changed during deploy.");
        }
    }

    private static bool JunctionsMatch(string gameRoot, DeployManifest manifest)
    {
        foreach (var junction in manifest.Junctions)
        {
            var installPath = GamePath.Combine(gameRoot, junction.InstallRelative);
            if (!NtfsLinks.IsJunction(installPath))
            {
                return false;
            }

            var target = NtfsLinks.TryGetJunctionTarget(installPath);
            if (target is null || !SafeFileSystem.SamePath(target, junction.TargetFull))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BaselineUntouched(string gameRoot, SptOwnedBaseline baseline)
    {
        foreach (var file in baseline.Files)
        {
            var path = GamePath.Combine(gameRoot, file.RelativePath);
            if (!File.Exists(path) || !string.Equals(HashFile(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static DeployManifest? TryReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<DeployManifest>(File.ReadAllText(path), JsonOptions);
    }

    private static void WriteJson(string path, DeployManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void AppendLog(string managerData, string profileId, object row)
    {
        try
        {
            var path = ProfilePaths.DeployLog(managerData, profileId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(row, LogJsonOptions) + Environment.NewLine);
        }
        catch (IOException)
        {
            // Logging must not fail deploy.
        }
    }

    private static void AppendHumanLog(string managerData, string profileId, string text)
    {
        try
        {
            var path = ProfilePaths.DeployHumanLog(managerData, profileId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, DateTimeOffset.Now.ToString("u") + "  " + text + Environment.NewLine);
        }
        catch (IOException)
        {
            // Logging must not fail deploy.
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Summarize(DeployManifest manifest, IReadOnlyList<string> warnings)
    {
        var text = $"Deployed {manifest.Enabled.Count} mod(s), {manifest.Junctions.Count} junction(s), {manifest.CopiedFiles.Count} file copy(ies), {manifest.Conflicts.Count} overlay conflict(s).";
        if (warnings.Count > 0)
        {
            text += Environment.NewLine + string.Join(Environment.NewLine, warnings);
        }

        return text;
    }

    private static DeployResult Fail(string message)
        => new() { Status = DeployStatus.Failed, Message = message };
}
