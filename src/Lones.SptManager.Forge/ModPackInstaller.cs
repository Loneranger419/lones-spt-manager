using Lones.SptManager.Core;
using Lones.SptManager.Core.Inventory;
using Lones.SptManager.Core.Store;

namespace Lones.SptManager.Forge;

public sealed class ModPackProgress
{
    public required string Message { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public string? LogLine { get; init; }

    public bool Indeterminate => Total <= 0;

    public double Percent => Total <= 0 ? 0 : Math.Clamp(100.0 * Current / Total, 0, 100);
}

public sealed class ModPackInstallResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public int Installed { get; init; }
    public int Reused { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class ModPackInstaller : IDisposable
{
    private readonly ForgeInstaller _installer;
    private readonly HttpClient _packHttp;
    private readonly bool _ownsPackHttp;

    public ModPackInstaller(ForgeClient client, HttpClient? packHttp = null)
    {
        _installer = new ForgeInstaller(client);
        if (packHttp is null)
        {
            _packHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _packHttp.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
            _ownsPackHttp = true;
        }
        else
        {
            _packHttp = packHttp;
        }
    }

    public async Task<ModPackInstallResult> InstallAsync(
        string source,
        string managerData,
        string profileId,
        string sptVersion = "4.1.2",
        IProgress<ModPackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = ModPackSource.Normalize(source);
        Report(progress, "Reading pack…", 0, 0);
        var json = await ReadJsonAsync(resolved, cancellationToken).ConfigureAwait(false);
        var listed = ModPackManifest.Parse(json).ListedMods();
        if (listed.Count == 0)
        {
            return Fail("Pack JSON has no mods.");
        }

        var store = ModStore.List(managerData);
        var order = new List<(string ModKey, string Version)>();
        var warnings = new List<string>();
        var installed = 0;
        var reused = 0;
        var failed = 0;

        try
        {
            for (var i = 0; i < listed.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = listed[i];
                var label = $"{entry.DisplayName} {entry.RequestedVersion}".Trim();
                Report(progress, $"Pack {i + 1}/{listed.Count}: {label}", i, listed.Count);
                try
                {
                    var existing = FindInStore(store, entry.Id, entry.RequestedVersion);
                    if (existing is not null)
                    {
                        order.Add((existing.ModKey, existing.Version));
                        reused++;
                        continue;
                    }

                    var result = await InstallWithRetryAsync(
                            entry,
                            managerData,
                            sptVersion,
                            progress,
                            i,
                            listed.Count,
                            cancellationToken)
                        .ConfigureAwait(false);
                    warnings.AddRange(result.Warnings);
                    if (!result.Success)
                    {
                        failed++;
                        var line = $"{entry.DisplayName}: {result.Message ?? "Forge install failed."}";
                        warnings.Add(line);
                        Report(progress, $"Pack {i + 1}/{listed.Count}: {label}", i + 1, listed.Count, line);
                        continue;
                    }

                    var document = result.Documents.FirstOrDefault(item => item.ForgeModId == entry.Id)
                                   ?? result.Documents.FirstOrDefault();
                    if (document is null || !document.Deployable)
                    {
                        failed++;
                        var line = entry.DisplayName + ": installed package is not deployable.";
                        warnings.Add(line);
                        Report(progress, $"Pack {i + 1}/{listed.Count}: {label}", i + 1, listed.Count, line);
                        continue;
                    }

                    order.Add((document.ModKey, document.Version));
                    installed++;
                    store = ModStore.List(managerData);
                    Report(progress, $"Pack {i + 1}/{listed.Count}: {label}", i + 1, listed.Count);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    var line = $"{entry.DisplayName}: {ex.Message}";
                    warnings.Add(line);
                    Report(progress, $"Pack {i + 1}/{listed.Count}: {label}", i + 1, listed.Count, line);
                }

                if (i + 1 < listed.Count)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            warnings.Add("Pack install cancelled.");
        }

        InstallInventory.ReplaceLoadOrder(managerData, profileId, order);
        var ok = order.Count > 0;
        var message = ok
            ? $"Pack installed {installed}, reused {reused}, failed {failed} of {listed.Count}. Load order follows the JSON list."
            : $"Pack installed nothing ({failed} failed of {listed.Count}).";
        return new ModPackInstallResult
        {
            Success = ok,
            Message = message,
            Installed = installed,
            Reused = reused,
            Failed = failed,
            Warnings = warnings
        };
    }

    private async Task<ForgeInstallResult> InstallWithRetryAsync(
        ModPackEntry entry,
        string managerData,
        string sptVersion,
        IProgress<ModPackProgress>? progress,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        var status = new Progress<string>(text =>
        {
            var log = text.StartsWith("Reading archive", StringComparison.Ordinal)
                      || text.StartsWith("Extracting 0 /", StringComparison.Ordinal)
                      || text.StartsWith("Retrying extract", StringComparison.Ordinal)
                      || text.StartsWith("Hashing", StringComparison.Ordinal)
                ? text
                : null;
            Report(progress, text, index, total, log);
        });
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await _installer.InstallAsync(
                        entry.Id,
                        managerData,
                        profileId: null,
                        sptVersion,
                        requestedVersion: string.IsNullOrWhiteSpace(entry.RequestedVersion)
                            ? null
                            : entry.RequestedVersion,
                        includeAddons: false,
                        fetchThumbnails: false,
                        status,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 3 && ForgeClient.IsTransientDownload(ex))
            {
                last = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 5)), cancellationToken).ConfigureAwait(false);
            }
        }

        return new ForgeInstallResult
        {
            Success = false,
            Message = last?.Message ?? "Forge install failed."
        };
    }

    private static void Report(
        IProgress<ModPackProgress>? progress,
        string message,
        int current,
        int total,
        string? logLine = null)
        => progress?.Report(new ModPackProgress
        {
            Message = message,
            Current = current,
            Total = total,
            LogLine = logLine
        });

    private async Task<string> ReadJsonAsync(string source, CancellationToken cancellationToken)
    {
        if (!ModPackSource.IsHttpsUrl(source))
        {
            var info = new FileInfo(source);
            if (info.Length > ModPackSource.MaxJsonBytes)
            {
                throw new InvalidOperationException("Pack JSON is larger than 2 MB.");
            }

            return await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
        }

        using var response = await _packHttp.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > ModPackSource.MaxJsonBytes)
        {
            throw new InvalidOperationException("Pack JSON is larger than 2 MB.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > ModPackSource.MaxJsonBytes)
            {
                throw new InvalidOperationException("Pack JSON is larger than 2 MB.");
            }

            limited.Write(buffer, 0, read);
        }

        limited.Position = 0;
        using var reader = new StreamReader(limited);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ModDocument? FindInStore(IReadOnlyList<ModDocument> store, int forgeModId, string? version)
    {
        var matches = store.Where(document => document.ForgeModId == forgeModId && document.Deployable);
        if (string.IsNullOrWhiteSpace(version))
        {
            return matches.FirstOrDefault();
        }

        return matches.FirstOrDefault(document => VersionEquals(document.Version, version));
    }

    public static bool VersionEquals(string? left, string? right)
        => string.Equals(NormalizeVersion(left), NormalizeVersion(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVersion(string? version)
    {
        var value = (version ?? "").Trim();
        if (value.Length >= 2 && (value[0] is 'v' or 'V') && char.IsDigit(value[1]))
        {
            return value[1..];
        }

        return value;
    }

    private static ModPackInstallResult Fail(string message)
        => new() { Success = false, Message = message };

    public void Dispose()
    {
        if (_ownsPackHttp)
        {
            _packHttp.Dispose();
        }
    }
}
