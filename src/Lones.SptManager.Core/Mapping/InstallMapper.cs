using System.IO.Compression;
using System.Security.Cryptography;
using Lones.SptManager.Core.Deploy;
using Lones.SptManager.Core.Instance;
using Lones.SptManager.Core.Paths;
using Lones.SptManager.Core.Store;
using Lones.SptManager.Native;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;

namespace Lones.SptManager.Core.Mapping;

public sealed record ImportResult(PackageMap Map, ModDocument? Document, string? Message);

public sealed class InstallMapper
{
    private readonly ModStore _store = new();

    public PackageMap MapArchive(string archivePath, MapperOptions? options = null)
    {
        options ??= new MapperOptions();
        VerifyLength(archivePath, options);
        var listed = ArchiveReader.ListEntries(archivePath);
        foreach (var entry in listed)
        {
            ArchivePathRules.EnsureSafe(entry.Key, Path.GetTempPath());
        }

        return PrefixMapper.Map(listed.Select(entry => entry.Key), options);
    }

    public ImportResult ImportArchive(
        string archivePath,
        string managerData,
        MapperOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MapperOptions();
        VerifyLength(archivePath, options);
        progress?.Report("Reading archive listing…");
        var kind = ArchiveReader.DetectKind(archivePath);
        var listed = ArchiveReader.ListEntries(archivePath);
        foreach (var entry in listed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchivePathRules.EnsureSafe(entry.Key, Path.GetTempPath());
        }

        var map = PrefixMapper.Map(listed.Select(entry => entry.Key), options);
        if (map.NeedsConfirm && !options.AllowLowConfidence)
        {
            return new ImportResult(map, null, "Import blocked: confirm the archive layout first.");
        }

        if (!map.Deployable && map.Kind == PackageKind.Tool && !options.ImportTools)
        {
            return new ImportResult(map, null, "Tool package not merged into the game tree.");
        }

        if (map.DeployFiles.Count == 0)
        {
            return new ImportResult(map, null, "Nothing deployable in this archive.");
        }

        var key = ModStore.ResolveKey(map, options, archivePath);
        string? archiveHash = null;
        if (string.IsNullOrWhiteSpace(options.Version))
        {
            var size = new FileInfo(archivePath).Length;
            if (size < 80L * 1024 * 1024)
            {
                progress?.Report("Hashing archive…");
                archiveHash = HashArchive(archivePath);
            }
            else
            {
                archiveHash = "sz" + size.ToString("x") + "-" + new FileInfo(archivePath).LastWriteTimeUtc.Ticks.ToString("x");
            }
        }

        var version = ModStore.ResolveVersion(options, archiveHash);
        var packageDir = ModStore.PackageDirectory(managerData, key, version);
        var filesDir = ModStore.FilesDirectory(managerData, key, version);
        if (Directory.Exists(packageDir))
        {
            Directory.Delete(packageDir, recursive: true);
        }

        Directory.CreateDirectory(filesDir);
        try
        {
            var records = ExtractMapped(
                archivePath,
                kind,
                map,
                filesDir,
                progress,
                cancellationToken);
            var document = _store.Commit(managerData, map, records, options, archiveHash, archivePath);
            return new ImportResult(map, document, $"Imported {document.ModKey} {document.Version} ({document.Files.Count} files, {document.Kind}).");
        }
        catch
        {
            try
            {
                if (Directory.Exists(packageDir))
                {
                    Directory.Delete(packageDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Partial extract cleanup is best-effort.
            }

            throw;
        }
    }

    public Task<ImportResult> ImportArchiveAsync(
        string archivePath,
        string managerData,
        MapperOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => ImportArchive(archivePath, managerData, options, progress, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Copy a leftover install folder or loose file into the store, then remove it from the game tree
    /// so the next Deploy can junction it.
    /// </summary>
    public ImportResult ImportInstallTree(string gameRoot, string installRelative, string managerData, MapperOptions? options = null)
    {
        options ??= new MapperOptions { AllowLowConfidence = true };
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(installRelative);
        ArgumentException.ThrowIfNullOrWhiteSpace(managerData);

        var relative = GamePath.Normalize(installRelative);
        if (SptDenylist.IsForbidden(relative)
            || GamePath.EqualsNormalized(relative, SptLayout.BepInExPluginsSpt)
            || GamePath.IsUnderOrEqual(relative, SptLayout.BepInExPluginsSpt))
        {
            return new ImportResult(
                PrefixMapper.Map(Array.Empty<string>(), options),
                null,
                "Refusing to import an SPT-owned path.");
        }

        var source = GamePath.Combine(gameRoot, relative);
        if (NtfsLinks.IsJunction(source))
        {
            return new ImportResult(
                PrefixMapper.Map(Array.Empty<string>(), options),
                null,
                "Refusing to import a junction. That path is already manager-owned.");
        }

        if (!File.Exists(source) && !Directory.Exists(source))
        {
            return new ImportResult(
                PrefixMapper.Map(Array.Empty<string>(), options),
                null,
                "Leftover path is missing: " + relative);
        }

        var archivePaths = new List<string>();
        if (File.Exists(source))
        {
            ArchivePathRules.EnsureSafe(relative, gameRoot);
            archivePaths.Add(relative);
        }
        else
        {
            foreach (var file in EnumerateFilesNoFollow(source))
            {
                var entry = GamePath.Normalize(Path.GetRelativePath(gameRoot, file));
                ArchivePathRules.EnsureSafe(entry, gameRoot);
                archivePaths.Add(entry);
            }
        }

        var map = PrefixMapper.Map(archivePaths, options);
        if (map.NeedsConfirm && !options.AllowLowConfidence)
        {
            return new ImportResult(map, null, "Import blocked: confirm the leftover layout first.");
        }

        if (map.DeployFiles.Count == 0)
        {
            return new ImportResult(map, null, "Nothing deployable in this leftover path.");
        }

        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in map.DeployFiles)
        {
            var from = GamePath.Combine(gameRoot, ArchivePathRules.NormalizeEntry(entry.ArchivePath));
            extracted[GamePath.Normalize(entry.CanonicalPath!)] = from;
        }

        var version = options.Version ?? ("disk-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        var writeOptions = new MapperOptions
        {
            AllowSptData = options.AllowSptData,
            AllowLowConfidence = options.AllowLowConfidence,
            ImportTools = options.ImportTools,
            ModKey = options.ModKey,
            Version = version,
            ForgeModId = options.ForgeModId,
            ForgeGuid = options.ForgeGuid
        };
        var document = _store.Write(managerData, map, extracted, writeOptions, archiveHash: null, sourceArchive: relative);
        RemoveLeftover(source);
        return new ImportResult(map, document, $"Imported leftover {document.ModKey} {document.Version} ({document.Files.Count} files). Deploy to replace the install copy with a junction.");
    }

    private static IEnumerable<string> EnumerateFilesNoFollow(string directory)
    {
        if (NtfsLinks.IsJunction(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (NtfsLinks.IsJunction(child))
            {
                continue;
            }

            foreach (var file in EnumerateFilesNoFollow(child))
            {
                yield return file;
            }
        }
    }

    private static void RemoveLeftover(string source)
    {
        if (NtfsLinks.IsJunction(source))
        {
            return;
        }

        if (File.Exists(source))
        {
            File.Delete(source);
            return;
        }

        if (Directory.Exists(source))
        {
            SafeFileSystem.DeleteDirectoryNoFollow(source);
        }
    }

    private static List<ModFileRecord> ExtractMapped(
        string archivePath,
        ArchiveKind kind,
        PackageMap map,
        string filesDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (kind == ArchiveKind.Zip)
        {
            try
            {
                return ExtractZip(archivePath, map, filesDir, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                progress?.Report("Retrying extract with the 7-zip reader…");
            }
        }

        return ExtractSharpCompress(archivePath, map, filesDir, consumeUnmapped: kind == ArchiveKind.SevenZip, progress, cancellationToken);
    }

    private static List<ModFileRecord> ExtractZip(
        string archivePath,
        PackageMap map,
        string filesDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var byNormalized = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            byNormalized[ArchivePathRules.NormalizeEntry(entry.FullName)] = entry;
        }

        var records = new List<ModFileRecord>(map.DeployFiles.Count);
        var total = map.DeployFiles.Count;
        progress?.Report($"Extracting 0 / {total} files…");
        var done = 0;
        foreach (var mapped in map.DeployFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mapped.CanonicalPath is null)
            {
                continue;
            }

            var key = ArchivePathRules.NormalizeEntry(mapped.ArchivePath);
            if (!byNormalized.TryGetValue(key, out var entry))
            {
                throw new InvalidOperationException("Mapped file was not extracted: " + mapped.ArchivePath);
            }

            ArchivePathRules.EnsureSafe(entry.FullName, filesDir);
            var dest = Path.Combine(filesDir, mapped.CanonicalPath.Replace('/', Path.DirectorySeparatorChar));
            using var input = entry.Open();
            var hash = CopyHashed(input, dest, entry.Length, done + 1, total, progress, cancellationToken);
            records.Add(new ModFileRecord
            {
                CanonicalPath = GamePath.Normalize(mapped.CanonicalPath),
                Sha256 = hash
            });
            done++;
        }

        return records;
    }

    private static List<ModFileRecord> ExtractSharpCompress(
        string archivePath,
        PackageMap map,
        string filesDir,
        bool consumeUnmapped,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ArchiveReader.Open(archivePath);
        var byNormalized = map.DeployFiles.ToDictionary(
            entry => ArchivePathRules.NormalizeEntry(entry.ArchivePath),
            entry => entry,
            StringComparer.OrdinalIgnoreCase);
        var records = new List<ModFileRecord>(map.DeployFiles.Count);
        var total = map.DeployFiles.Count;
        var done = 0;
        var solid = consumeUnmapped || archive is SevenZipArchive;
        progress?.Report($"Extracting 0 / {total} files…");

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory || entry.Key is null)
            {
                continue;
            }

            var normalized = ArchivePathRules.NormalizeEntry(entry.Key);
            if (!byNormalized.TryGetValue(normalized, out var mapped) || mapped.CanonicalPath is null)
            {
                if (solid)
                {
                    using var skip = entry.OpenEntryStream();
                    skip.CopyTo(Stream.Null);
                }

                continue;
            }

            ArchivePathRules.EnsureSafe(entry.Key, filesDir);
            var dest = Path.Combine(filesDir, mapped.CanonicalPath.Replace('/', Path.DirectorySeparatorChar));
            using (var input = entry.OpenEntryStream())
            {
                var hash = CopyHashed(input, dest, entry.Size, done + 1, total, progress, cancellationToken);
                records.Add(new ModFileRecord
                {
                    CanonicalPath = GamePath.Normalize(mapped.CanonicalPath),
                    Sha256 = hash
                });
            }

            done++;
        }

        if (records.Count != total)
        {
            throw new InvalidOperationException($"Mapped file was not extracted: expected {total}, got {records.Count}.");
        }

        return records;
    }

    private static string CopyHashed(
        Stream input,
        string dest,
        long expectedLength,
        int fileNumber,
        int total,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var sha = SHA256.Create();
        using var output = File.Create(dest);
        using var hashed = new CryptoStream(output, sha, CryptoStreamMode.Write);
        var buffer = new byte[512 * 1024];
        long written = 0;
        var lastReport = DateTime.UtcNow;
        progress?.Report(FormatExtract(fileNumber, total, 0, expectedLength));
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hashed.Write(buffer, 0, read);
            written += read;
            if (progress is not null && DateTime.UtcNow - lastReport >= TimeSpan.FromMilliseconds(250))
            {
                progress.Report(FormatExtract(fileNumber, total, written, expectedLength));
                lastReport = DateTime.UtcNow;
            }
        }

        hashed.FlushFinalBlock();
        progress?.Report(FormatExtract(fileNumber, total, written, expectedLength));
        return Convert.ToHexString(sha.Hash!);
    }

    private static string FormatExtract(int fileNumber, int total, long written, long expectedLength)
    {
        if (expectedLength > 0)
        {
            return $"Extracting {fileNumber} / {total} files — {FormatBytes(written)} / {FormatBytes(expectedLength)}";
        }

        return written > 0
            ? $"Extracting {fileNumber} / {total} files — {FormatBytes(written)}"
            : $"Extracting {fileNumber} / {total} files…";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
        }

        if (bytes >= 1024)
        {
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        return bytes + " B";
    }

    private static void VerifyLength(string archivePath, MapperOptions options)
    {
        if (options.ExpectedContentLength is { } expected)
        {
            var actual = new FileInfo(archivePath).Length;
            if (actual != expected)
            {
                throw new InvalidOperationException($"content_length mismatch: expected {expected}, got {actual}.");
            }
        }
    }

    private static string HashArchive(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
