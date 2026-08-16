using System.IO.Compression;
using System.Text;

namespace Lones.SptManager.Tests;

internal static class ZipFixture
{
    public static string WriteZip(string path, IEnumerable<(string Name, string Text)> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(text);
        }

        return path;
    }
}
