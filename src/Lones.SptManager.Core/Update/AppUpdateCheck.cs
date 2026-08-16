using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lones.SptManager.Core.Update;

public enum AppUpdateCheckStatus
{
    UpdateAvailable,
    Current,
    Unavailable
}

public sealed class AppUpdateInfo
{
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required string ReleaseUrl { get; init; }

    public string Summary
        => LatestVersion + " is available (you have " + CurrentVersion + ").";
}

public sealed class AppUpdateCheckResult
{
    public required AppUpdateCheckStatus Status { get; init; }
    public AppUpdateInfo? Update { get; init; }

    public static AppUpdateCheckResult Unavailable { get; } = new() { Status = AppUpdateCheckStatus.Unavailable };

    public static AppUpdateCheckResult UpToDate { get; } = new() { Status = AppUpdateCheckStatus.Current };

    public static AppUpdateCheckResult Found(AppUpdateInfo update)
        => new() { Status = AppUpdateCheckStatus.UpdateAvailable, Update = update };
}

public static class AppUpdateCheck
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<AppUpdateCheckResult> CheckLatestAsync(
        HttpClient http,
        string? currentVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        using var request = new HttpRequestMessage(HttpMethod.Get, ProductInfo.LatestReleaseApiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Lones-SPT-Manager", ProductInfo.Version));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return AppUpdateCheckResult.Unavailable;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return TryParseRelease(json, currentVersion ?? ProductInfo.Version);
    }

    public static AppUpdateCheckResult TryParseRelease(string json, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(currentVersion))
        {
            return AppUpdateCheckResult.Unavailable;
        }

        GitHubReleaseDto? release;
        try
        {
            release = JsonSerializer.Deserialize<GitHubReleaseDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return AppUpdateCheckResult.Unavailable;
        }

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            return AppUpdateCheckResult.Unavailable;
        }

        if (release.Draft || release.Prerelease)
        {
            return AppUpdateCheckResult.UpToDate;
        }

        if (CompareVersions(release.TagName, currentVersion) is not > 0)
        {
            return AppUpdateCheckResult.UpToDate;
        }

        var url = string.IsNullOrWhiteSpace(release.HtmlUrl)
            ? ProductInfo.ReleasesUrl
            : release.HtmlUrl.Trim();
        return AppUpdateCheckResult.Found(new AppUpdateInfo
        {
            CurrentVersion = NormalizeTag(currentVersion),
            LatestVersion = NormalizeTag(release.TagName),
            ReleaseUrl = url
        });
    }

    public static int? CompareVersions(string? left, string? right)
    {
        var a = NormalizeTag(left);
        var b = NormalizeTag(right);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return null;
        }

        var leftParts = a.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightParts = b.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < count; i++)
        {
            var leftPart = i < leftParts.Length ? leftParts[i] : "0";
            var rightPart = i < rightParts.Length ? rightParts[i] : "0";
            if (!int.TryParse(leftPart, out var leftNumber) || !int.TryParse(rightPart, out var rightNumber))
            {
                var text = string.Compare(leftPart, rightPart, StringComparison.OrdinalIgnoreCase);
                if (text != 0)
                {
                    return text;
                }

                continue;
            }

            var number = leftNumber.CompareTo(rightNumber);
            if (number != 0)
            {
                return number;
            }
        }

        return 0;
    }

    public static string NormalizeTag(string? tag)
    {
        var value = (tag ?? "").Trim();
        if (value.Length >= 2 && (value[0] is 'v' or 'V') && char.IsDigit(value[1]))
        {
            return value[1..];
        }

        return value;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
    }
}
