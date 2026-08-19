using System.Buffers;
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
            ForgeAddonId = options.ForgeAddonId,
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

    private const int SmallFileBytes = 1024 * 1024;

    private static List<ModFileRecord> ExtractMapped(
        string archivePath,
        ArchiveKind kind,
        PackageMap map,
        string filesDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var work = new List<(MappedEntry Mapped, string Key, string Dest)>(map.DeployFiles.Count);
        foreach (var mapped in map.DeployFiles)
        {
            if (mapped.CanonicalPath is null)
            {
                continue;
            }

            var dest = Path.Combine(filesDir, mapped.CanonicalPath.Replace('/', Path.DirectorySeparatorChar));
            ArchivePathRules.EnsureSafe(mapped.ArchivePath, filesDir);
            work.Add((mapped, ArchivePathRules.NormalizeEntry(mapped.ArchivePath), dest));
        }

        PrecreateDirectories(work.Select(item => item.Dest));
        var clock = new ExtractClock(progress, work.Count, "Unpacking");
        if (!TryNativeThenMove(archivePath, kind, filesDir, work, clock, cancellationToken))
        {
            if (kind == ArchiveKind.Zip)
            {
                try
                {
                    ExtractZipWriteOnly(archivePath, work, clock, cancellationToken);
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
                {
                    progress?.Report("Retrying extract with the 7-zip reader…");
                    ExtractSharpCompressWriteOnly(archivePath, work, consumeUnmapped: false, clock, cancellationToken);
                }
            }
            else
            {
                ExtractSharpCompressWriteOnly(archivePath, work, consumeUnmapped: true, clock, cancellationToken);
            }
        }

        return HashWrittenFiles(work, progress, cancellationToken);
    }

    private static bool TryNativeThenMove(
        string archivePath,
        ArchiveKind kind,
        string filesDir,
        List<(MappedEntry Mapped, string Key, string Dest)> work,
        ExtractClock clock,
        CancellationToken cancellationToken)
    {
        var temp = Path.Combine(Path.GetDirectoryName(filesDir)!, "extracting");
        if (Directory.Exists(temp))
        {
            SafeFileSystem.DeleteDirectoryNoFollow(temp);
        }

        clock.Status("Unpacking with 7-Zip / tar…");
        var tool = NativeArchiveExtract.TryUnpack(archivePath, temp, kind, cancellationToken);
        if (tool is null)
        {
            if (Directory.Exists(temp))
            {
                SafeFileSystem.DeleteDirectoryNoFollow(temp);
            }

            return false;
        }

        clock.Status("Unpacking with " + tool + "…");
        try
        {
            foreach (var item in work)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = ResolveExtractedPath(temp, item.Key, item.Mapped.ArchivePath);
                if (source is null)
                {
                    return false;
                }

                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(item.Dest), StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(item.Dest))
                    {
                        File.Delete(item.Dest);
                    }

                    File.Move(source, item.Dest);
                }

                clock.FileFinished();
            }

            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                {
                    SafeFileSystem.DeleteDirectoryNoFollow(temp);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? ResolveExtractedPath(string temp, string normalizedKey, string archivePath)
    {
        var relative = normalizedKey.Replace('/', Path.DirectorySeparatorChar);
        var direct = Path.Combine(temp, relative);
        if (File.Exists(direct))
        {
            return direct;
        }

        var raw = Path.Combine(temp, archivePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(raw) ? raw : null;
    }

    private static void ExtractZipWriteOnly(
        string archivePath,
        List<(MappedEntry Mapped, string Key, string Dest)> work,
        ExtractClock clock,
        CancellationToken cancellationToken)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var destByKey = work.ToDictionary(item => item.Key, item => item.Dest, StringComparer.OrdinalIgnoreCase);
        foreach (var zipEntry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(zipEntry.Name))
            {
                continue;
            }

            var key = ArchivePathRules.NormalizeEntry(zipEntry.FullName);
            if (!destByKey.TryGetValue(key, out var dest))
            {
                continue;
            }

            using var input = zipEntry.Open();
            WriteOnly(input, dest, zipEntry.Length, clock, cancellationToken);
            clock.FileFinished();
        }

        if (work.Any(item => !File.Exists(item.Dest)))
        {
            throw new InvalidOperationException("Mapped file was not extracted.");
        }
    }

    private static void ExtractSharpCompressWriteOnly(
        string archivePath,
        List<(MappedEntry Mapped, string Key, string Dest)> work,
        bool consumeUnmapped,
        ExtractClock clock,
        CancellationToken cancellationToken)
    {
        using var archive = ArchiveReader.Open(archivePath);
        var destByKey = work.ToDictionary(item => item.Key, item => item.Dest, StringComparer.OrdinalIgnoreCase);
        var solid = consumeUnmapped || archive is SevenZipArchive;
        var written = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory || entry.Key is null)
            {
                continue;
            }

            var normalized = ArchivePathRules.NormalizeEntry(entry.Key);
            if (!destByKey.TryGetValue(normalized, out var dest))
            {
                if (solid)
                {
                    using var skip = entry.OpenEntryStream();
                    skip.CopyTo(Stream.Null);
                }

                continue;
            }

            using (var input = entry.OpenEntryStream())
            {
                WriteOnly(input, dest, entry.Size, clock, cancellationToken);
            }

            written++;
            clock.FileFinished();
        }

        if (written != work.Count)
        {
            throw new InvalidOperationException($"Mapped file was not extracted: expected {work.Count}, got {written}.");
        }
    }

    private static List<ModFileRecord> HashWrittenFiles(
        List<(MappedEntry Mapped, string Key, string Dest)> work,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var clock = new ExtractClock(progress, work.Count, "Hashing");
        var records = new ModFileRecord[work.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
            CancellationToken = cancellationToken
        };
        Parallel.For(0, work.Count, options, i =>
        {
            var item = work[i];
            records[i] = new ModFileRecord
            {
                CanonicalPath = GamePath.Normalize(item.Mapped.CanonicalPath!),
                Sha256 = HashFile(item.Dest)
            };
            clock.FileFinished();
        });

        return [.. records];
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void PrecreateDirectories(IEnumerable<string> destinations)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in destinations)
        {
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir))
            {
                dirs.Add(dir);
            }
        }

        foreach (var dir in dirs.OrderBy(item => item.Length))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void WriteOnly(
        Stream input,
        string dest,
        long expectedLength,
        ExtractClock clock,
        CancellationToken cancellationToken)
    {
        if (expectedLength >= 0 && expectedLength <= SmallFileBytes)
        {
            var data = expectedLength == 0 ? [] : new byte[expectedLength];
            var offset = 0;
            while (offset < data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = input.Read(data, offset, data.Length - offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset != data.Length)
            {
                Array.Resize(ref data, offset);
            }

            File.WriteAllBytes(dest, data);
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(512 * 1024);
        try
        {
            using var output = new FileStream(
                dest,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                512 * 1024,
                FileOptions.SequentialScan);
            long written = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                written += read;
                clock.Bytes(written, expectedLength);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class ExtractClock
    {
        private readonly IProgress<string>? _progress;
        private readonly int _total;
        private readonly string _phase;
        private int _done;
        private long _nextReport;

        public ExtractClock(IProgress<string>? progress, int total, string phase)
        {
            _progress = progress;
            _total = total;
            _phase = phase;
            _progress?.Report($"{phase} 0 / {total} files…");
        }

        public void Status(string message) => _progress?.Report(message);

        public void FileFinished()
        {
            var n = Interlocked.Increment(ref _done);
            Report(n, 0, 0, force: n == 1 || n == _total);
        }

        public void Bytes(long written, long expected)
            => Report(Math.Max(Volatile.Read(ref _done), 1), written, expected, force: false);

        private void Report(int current, long written, long expected, bool force)
        {
            if (_progress is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            if (!force && now < Volatile.Read(ref _nextReport))
            {
                return;
            }

            Volatile.Write(ref _nextReport, now + 250);
            _progress.Report(FormatExtract(_phase, current, _total, written, expected));
        }
    }

    private static string FormatExtract(string phase, int fileNumber, int total, long written, long expectedLength)
    {
        if (expectedLength > 0)
        {
            return $"{phase} {fileNumber} / {total} files — {FormatBytes(written)} / {FormatBytes(expectedLength)}";
        }

        return written > 0
            ? $"{phase} {fileNumber} / {total} files — {FormatBytes(written)}"
            : $"{phase} {fileNumber} / {total} files…";
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
