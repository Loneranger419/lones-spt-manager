using System.Net;
using System.Text.Json;
using System.Threading;
using Lones.SptManager.Core;

namespace Lones.SptManager.Forge;

public sealed class ForgeClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ForgeClient()
        : this(CreateDefaultClient(), ownsClient: true)
    {
    }

    public ForgeClient(HttpClient http, bool ownsClient = false)
    {
        _http = http;
        _ownsClient = ownsClient;
        _http.BaseAddress ??= new Uri(ForgeEndpoints.ApiBase);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
        }

        if (ForgeEndpoints.IsForbiddenHost(_http.BaseAddress))
        {
            throw new InvalidOperationException("Do not use forge.sp-tarkov.com or forge.sp-mod.com (both 525). Use sp-mod.com.");
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, "ping"), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
    }

    internal async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = requestFactory();
            if (request.RequestUri is not null && ForgeEndpoints.IsForbiddenHost(request.RequestUri.IsAbsoluteUri ? request.RequestUri : new Uri(_http.BaseAddress!, request.RequestUri)))
            {
                throw new InvalidOperationException("Refusing to call a dead Forge host.");
            }

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
            if (!retryable || attempt == maxAttempts)
            {
                return response;
            }

            var delay = ParseRetryAfter(response) ?? TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
            if (delay > TimeSpan.FromMinutes(2))
            {
                delay = TimeSpan.FromMinutes(2);
            }

            response.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Unreachable retry loop.");
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry is null)
        {
            return null;
        }

        if (retry.Delta is { } delta)
        {
            return delta;
        }

        if (retry.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    public async Task<IReadOnlyList<ForgeMod>> ListModsAsync(
        string? query = null,
        string? sptVersion = "4.1.2",
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        var qs = new List<string> { "include=versions", "per_page=" + Math.Clamp(perPage, 1, 50), "sort=-updated_at" };
        if (!string.IsNullOrWhiteSpace(query))
        {
            qs.Add("query=" + Uri.EscapeDataString(query));
        }

        if (!string.IsNullOrWhiteSpace(sptVersion))
        {
            qs.Add("filter[spt_version]=" + Uri.EscapeDataString("^" + sptVersion.Trim().TrimStart('^')));
        }

        var envelope = await GetJsonAsync<ForgeEnvelope<List<ForgeMod>>>("mods?" + string.Join("&", qs), cancellationToken)
            .ConfigureAwait(false);
        return envelope.Data ?? [];
    }

    public async Task<ForgeMod?> GetModAsync(int modId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetJsonAsync<ForgeEnvelope<ForgeMod>>("mod/" + modId, cancellationToken)
            .ConfigureAwait(false);
        return envelope.Data;
    }

    public async Task<IReadOnlyList<ForgeVersion>> GetVersionsAsync(int modId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetJsonAsync<ForgeEnvelope<List<ForgeVersion>>>(
                $"mod/{modId}/versions?sort=-version",
                cancellationToken)
            .ConfigureAwait(false);
        return envelope.Data ?? [];
    }

    public async Task<IReadOnlyDictionary<string, List<ForgeDependencyNode>>> GetDependenciesAsync(
        IEnumerable<string> identifierVersions,
        string sptVersion,
        CancellationToken cancellationToken = default)
    {
        var mods = string.Join(",", identifierVersions);
        var path = "mods/dependencies?mods=" + Uri.EscapeDataString(mods) + "&spt_version=" + Uri.EscapeDataString(sptVersion);
        var envelope = await GetJsonAsync<ForgeEnvelope<Dictionary<string, List<ForgeDependencyNode>>>>(path, cancellationToken)
            .ConfigureAwait(false);
        return envelope.Data ?? new Dictionary<string, List<ForgeDependencyNode>>();
    }

    public async Task<ForgeUpdates> GetUpdatesAsync(
        IEnumerable<string> identifierVersions,
        string sptVersion,
        CancellationToken cancellationToken = default)
    {
        var mods = string.Join(",", identifierVersions);
        var path = "mods/updates?mods=" + Uri.EscapeDataString(mods) + "&spt_version=" + Uri.EscapeDataString(sptVersion);
        var envelope = await GetJsonAsync<ForgeEnvelope<ForgeUpdates>>(path, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? new ForgeUpdates { SptVersion = sptVersion };
    }

    public async Task<IReadOnlyList<ForgeAddon>> ListAddonsAsync(int modId, CancellationToken cancellationToken = default)
    {
        var path = "addons?filter[mod_id]=" + modId + "&include=versions&per_page=50";
        var envelope = await GetJsonAsync<ForgeEnvelope<List<ForgeAddon>>>(path, cancellationToken).ConfigureAwait(false);
        return envelope.Data ?? [];
    }

    public async Task<IReadOnlyDictionary<string, List<ForgeDependencyNode>>> GetAddonDependenciesAsync(
        IEnumerable<string> identifierVersions,
        string sptVersion,
        CancellationToken cancellationToken = default)
    {
        var addons = string.Join(",", identifierVersions);
        var path = "addons/dependencies?addons=" + Uri.EscapeDataString(addons) + "&spt_version=" + Uri.EscapeDataString(sptVersion);
        var envelope = await GetJsonAsync<ForgeEnvelope<Dictionary<string, List<ForgeDependencyNode>>>>(path, cancellationToken)
            .ConfigureAwait(false);
        return envelope.Data ?? new Dictionary<string, List<ForgeDependencyNode>>();
    }

    public async Task<string> DownloadAsync(
        string downloadUrl,
        string destinationPath,
        long? expectedContentLength,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Download link is not an absolute URL.");
        }

        if (ForgeEndpoints.IsForbiddenHost(uri))
        {
            throw new InvalidOperationException("Refusing to download from a dead Forge host.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var response = await SendWithRetryAsync(
                        () => new HttpRequestMessage(HttpMethod.Get, uri),
                        cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? expectedContentLength;
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = File.Create(destinationPath))
                {
                    await CopyWithProgressAsync(input, output, total, progress, cancellationToken).ConfigureAwait(false);
                }

                var length = new FileInfo(destinationPath).Length;
                if (expectedContentLength is > 0 && length != expectedContentLength)
                {
                    if (LooksLikeArchive(destinationPath))
                    {
                        return destinationPath;
                    }

                    File.Delete(destinationPath);
                    last = new InvalidOperationException(
                        $"Download size {length} did not match content_length {expectedContentLength}.");
                    continue;
                }

                return destinationPath;
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                throw;
            }
            catch (Exception ex) when (attempt < 3 && IsTransientDownload(ex))
            {
                last = ex;
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, attempt * 4)), cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new InvalidOperationException("Download failed.");
    }

    public static bool LooksLikeArchive(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[6];
            var read = stream.Read(header);
            if (read >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] is 0x03 or 0x05 or 0x07)
            {
                return true;
            }

            return read >= 6
                   && header[0] == 0x37
                   && header[1] == 0x7A
                   && header[2] == 0xBC
                   && header[3] == 0xAF
                   && header[4] == 0x27
                   && header[5] == 0x1C;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task CopyWithProgressAsync(
        Stream input,
        Stream output,
        long? total,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long copied = 0;
        var lastReport = DateTime.UtcNow;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            stall.CancelAfter(TimeSpan.FromSeconds(90));
            int read;
            try
            {
                read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Download stalled (no data for 90 seconds).");
            }

            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;
            stall.TryReset();
            if (progress is not null && DateTime.UtcNow - lastReport >= TimeSpan.FromMilliseconds(250))
            {
                progress.Report(new DownloadProgress { Bytes = copied, Total = total });
                lastReport = DateTime.UtcNow;
            }
        }

        progress?.Report(new DownloadProgress { Bytes = copied, Total = total ?? copied });
    }

    internal static bool IsTransientDownload(Exception ex)
    {
        var text = ex.Message;
        return text.Contains("429", StringComparison.Ordinal)
               || text.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
               || text.Contains("maximum buffer size", StringComparison.OrdinalIgnoreCase)
               || text.Contains("503", StringComparison.Ordinal);
    }

    public static IReadOnlyList<ForgeSearchHit> ToSearchHits(IEnumerable<ForgeMod> mods, string sptMajorMinor = "4.1")
    {
        var hits = new List<ForgeSearchHit>();
        foreach (var mod in mods)
        {
            var version = PickVersion(mod.Versions, sptMajorMinor);
            hits.Add(new ForgeSearchHit
            {
                ModId = mod.Id,
                Guid = mod.Guid,
                Name = mod.Name ?? "mod " + mod.Id,
                Slug = mod.Slug,
                Teaser = mod.Teaser,
                Thumbnail = string.IsNullOrWhiteSpace(mod.Thumbnail) ? null : mod.Thumbnail,
                Version = version?.Version,
                DownloadLink = version?.Link,
                ContentLength = version?.ContentLength,
                SptVersionConstraint = version?.SptVersionConstraint,
                VersionFikaCompatibility = version?.FikaCompatibility,
                ModFikaCompatibility = mod.FikaCompatibility
            });
        }

        return hits;
    }

    public static ForgeVersion? PickVersion(IEnumerable<ForgeVersion> versions, string sptMajorMinor = "4.1")
    {
        var list = versions.ToList();
        return list.FirstOrDefault(version =>
                   (version.SptVersionConstraint ?? "").Contains(sptMajorMinor, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault();
    }

    public static IReadOnlyList<ForgeDependencyNode> Flatten(IEnumerable<ForgeDependencyNode> roots)
    {
        var list = new List<ForgeDependencyNode>();
        void Walk(ForgeDependencyNode node)
        {
            list.Add(node);
            foreach (var child in node.Dependencies)
            {
                Walk(child);
            }
        }

        foreach (var root in roots)
        {
            Walk(root);
        }

        return list;
    }

    private async Task<T> GetJsonAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, relativePath), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var parsed = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (parsed is null)
        {
            throw new InvalidOperationException("Forge returned empty JSON for " + relativePath);
        }

        return parsed;
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(ForgeEndpoints.ApiBase),
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.UserAgent);
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
