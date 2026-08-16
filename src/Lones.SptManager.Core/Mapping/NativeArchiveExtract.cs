using System.Diagnostics;

namespace Lones.SptManager.Core.Mapping;

internal static class NativeArchiveExtract
{
    public static string? TryUnpack(string archivePath, string destDir, ArchiveKind kind, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destDir);
        var sevenZip = Find7Zip();
        if (sevenZip is not null && Run(
                sevenZip,
                ["x", "-y", "-bd", "-sccUTF-8", "-o" + destDir, "--", archivePath],
                destDir,
                cancellationToken))
        {
            return "7-Zip";
        }

        ResetDir(destDir);
        if (kind == ArchiveKind.Zip)
        {
            var tar = FindTar();
            if (tar is not null && Run(tar, ["-xf", archivePath, "-C", destDir], destDir, cancellationToken))
            {
                return "tar";
            }
        }

        return null;
    }

    internal static string? Find7Zip()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath("7z.exe") ?? FindOnPath("7z");
    }

    internal static string? FindTar()
    {
        var system = Path.Combine(Environment.SystemDirectory, "tar.exe");
        return File.Exists(system) ? system : FindOnPath("tar.exe") ?? FindOnPath("tar");
    }

    private static void ResetDir(string destDir)
    {
        if (Directory.Exists(destDir))
        {
            try
            {
                Directory.Delete(destDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        Directory.CreateDirectory(destDir);
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var kill = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });

            process.WaitForExit();
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
