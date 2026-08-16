using System.IO.Compression;
using SharpCompress.Archives;

namespace Lones.SptManager.Core.Mapping;

public enum ArchiveKind
{
    Zip = 0,
    SevenZip = 1
}

public sealed record ArchiveEntryInfo(string Key, long Size, bool Encrypted);

public static class ArchiveReader
{
    public static ArchiveKind DetectKind(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        Span<byte> header = stackalloc byte[6];
        var read = stream.Read(header);
        if (read >= 4 && header[0] == 0x50 && header[1] == 0x4B)
        {
            return ArchiveKind.Zip;
        }

        if (read >= 6
            && header[0] == 0x37
            && header[1] == 0x7A
            && header[2] == 0xBC
            && header[3] == 0xAF
            && header[4] == 0x27
            && header[5] == 0x1C)
        {
            return ArchiveKind.SevenZip;
        }

        var ext = Path.GetExtension(archivePath);
        if (ext.Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveKind.SevenZip;
        }

        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveKind.Zip;
        }

        throw new InvalidOperationException("Only .zip and .7z archives are accepted.");
    }

    public static IReadOnlyList<ArchiveEntryInfo> ListEntries(string archivePath)
    {
        if (DetectKind(archivePath) == ArchiveKind.Zip)
        {
            return ListZipEntries(archivePath);
        }

        using var archive = Open(archivePath);
        return ListEntries(archive);
    }

    public static IReadOnlyList<ArchiveEntryInfo> ListZipEntries(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var list = new List<ArchiveEntryInfo>();
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            list.Add(new ArchiveEntryInfo(entry.FullName, entry.Length, false));
        }

        return list;
    }

    public static IReadOnlyList<ArchiveEntryInfo> ListEntries(IArchive archive)
    {
        var list = new List<ArchiveEntryInfo>();
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            if (entry.IsEncrypted)
            {
                throw new ZipSlipException("Password-protected archives are not allowed.");
            }

            list.Add(new ArchiveEntryInfo(entry.Key ?? string.Empty, entry.Size, entry.IsEncrypted));
        }

        return list;
    }

    public static IArchive Open(string archivePath)
    {
        var ext = Path.GetExtension(archivePath);
        if (!ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".7z", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .zip and .7z archives are accepted.");
        }

        return ArchiveFactory.OpenArchive(archivePath);
    }
}
