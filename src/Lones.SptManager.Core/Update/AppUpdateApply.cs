using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace Lones.SptManager.Core.Update;

public sealed class AppUpdateProgress
{
    public required string Message { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
}

public sealed class AppUpdateApplyPlan
{
    public required string ScriptPath { get; init; }
    public required string StagingDirectory { get; init; }
    public required string TargetDirectory { get; init; }
}

public static class AppUpdateApply
{
    private static readonly HashSet<string> UnpackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ProductInfo.ExeFileName,
        "mods.json.example"
    };

    private static readonly string[] TrustedHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com"
    ];

    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!TrustedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var expected = "/" + ProductInfo.GitHubOwner + "/" + ProductInfo.GitHubRepo + "/";
            return uri.AbsolutePath.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public static void EnsureInstallFolderWritable(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        Directory.CreateDirectory(targetDirectory);
        var probe = Path.Combine(targetDirectory, ".lones-update-write-test");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    public static async Task<string> DownloadAsync(
        HttpClient http,
        AppUpdateInfo update,
        string destinationPath,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(update);
        if (!update.CanInstall || !IsTrustedDownloadUrl(update.DownloadUrl))
        {
            throw new InvalidOperationException("This release has no trusted download for the Windows zip or exe.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Lones-SPT-Manager", ProductInfo.Version));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && !IsTrustedDownloadUrl(finalUri.ToString()))
        {
            throw new InvalidOperationException("Download redirected off GitHub.");
        }

        var total = response.Content.Headers.ContentLength ?? (update.AssetSize > 0 ? update.AssetSize : null);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destinationPath);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new AppUpdateProgress
            {
                Message = FormatDownload(received, total),
                Current = ToProgressUnits(received),
                Total = total is > 0 ? ToProgressUnits(total.Value) : 0
            });
        }

        if (update.AssetSize > 0 && received != update.AssetSize)
        {
            throw new InvalidOperationException("Download size did not match the GitHub asset.");
        }

        return destinationPath;
    }

    public static string UnpackRelease(string downloadedPath, string stagingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        Directory.CreateDirectory(stagingDirectory);
        var name = Path.GetFileName(downloadedPath);
        if (name.Equals(ProductInfo.ExeFileName, StringComparison.OrdinalIgnoreCase))
        {
            var dest = Path.Combine(stagingDirectory, ProductInfo.ExeFileName);
            File.Copy(downloadedPath, dest, overwrite: true);
            return dest;
        }

        using var zip = ZipFile.OpenRead(downloadedPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var fileName = Path.GetFileName(entry.FullName);
            if (!UnpackNames.Contains(fileName))
            {
                continue;
            }

            var dest = Path.GetFullPath(Path.Combine(stagingDirectory, fileName));
            var root = Path.GetFullPath(stagingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.ExtractToFile(dest, overwrite: true);
        }

        var exe = Path.Combine(stagingDirectory, ProductInfo.ExeFileName);
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException("The update zip did not contain " + ProductInfo.ExeFileName + ".");
        }

        return exe;
    }

    public static AppUpdateApplyPlan WriteApplyScript(int processId, string stagingDirectory, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        var src = CmdLiteral(Path.GetFullPath(stagingDirectory));
        var dest = CmdLiteral(Path.GetFullPath(targetDirectory));
        var scriptPath = Path.Combine(Path.GetTempPath(), "lones-spt-manager-apply-" + Guid.NewGuid().ToString("N") + ".cmd");
        var text = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("setlocal")
            .AppendLine("set \"PID=" + processId + "\"")
            .AppendLine("set \"SRC=" + src + "\"")
            .AppendLine("set \"DEST=" + dest + "\"")
            .AppendLine("set TRIES=0")
            .AppendLine(":wait")
            .AppendLine("timeout /t 1 /nobreak >nul")
            .AppendLine("tasklist /FI \"PID eq %PID%\" | find \"%PID%\" >nul")
            .AppendLine("if not errorlevel 1 goto wait")
            .AppendLine(":copy")
            .AppendLine("copy /Y \"%SRC%\\" + ProductInfo.ExeFileName + "\" \"%DEST%\\" + ProductInfo.ExeFileName + "\" >nul")
            .AppendLine("if errorlevel 1 (")
            .AppendLine("  set /a TRIES+=1")
            .AppendLine("  if %TRIES% GEQ 20 goto fail")
            .AppendLine("  timeout /t 1 /nobreak >nul")
            .AppendLine("  goto copy")
            .AppendLine(")")
            .AppendLine("if exist \"%SRC%\\mods.json.example\" copy /Y \"%SRC%\\mods.json.example\" \"%DEST%\\mods.json.example\" >nul")
            .AppendLine("start \"\" \"%DEST%\\" + ProductInfo.ExeFileName + "\"")
            .AppendLine("rmdir /S /Q \"%SRC%\"")
            .AppendLine("del /Q \"%~f0\"")
            .AppendLine("exit /b 0")
            .AppendLine(":fail")
            .AppendLine("start \"\" \"%DEST%\\" + ProductInfo.ExeFileName + "\"")
            .AppendLine("exit /b 1")
            .ToString();
        File.WriteAllText(scriptPath, text, Encoding.ASCII);
        return new AppUpdateApplyPlan
        {
            ScriptPath = scriptPath,
            StagingDirectory = src,
            TargetDirectory = dest
        };
    }

    public static string? TryGetInstallDirectory(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        if (!Path.GetFileName(processPath).Equals(ProductInfo.ExeFileName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetDirectoryName(Path.GetFullPath(processPath));
    }

    private static string CmdLiteral(string value)
    {
        if (value.IndexOfAny(['"', '\r', '\n', '&', '|', '>', '<']) >= 0)
        {
            throw new InvalidOperationException("Install path is not safe for the updater script.");
        }

        return value.Replace("%", "%%", StringComparison.Ordinal);
    }

    private static int ToProgressUnits(long bytes)
        => (int)Math.Clamp(bytes / 1024, 0, int.MaxValue);

    private static string FormatDownload(long received, long? total)
    {
        if (total is > 0)
        {
            return "Downloading " + FormatMb(received) + " / " + FormatMb(total.Value) + "…";
        }

        return "Downloading " + FormatMb(received) + "…";
    }

    private static string FormatMb(long bytes)
        => (bytes / (1024d * 1024d)).ToString("0.0") + " MB";
}
