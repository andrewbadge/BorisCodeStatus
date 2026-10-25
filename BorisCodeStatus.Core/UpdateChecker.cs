using System.Net.Http.Headers;
using System.Text.Json;

namespace BorisCodeStatus.Core;

/// <summary>
/// Asks GitHub for the newest published release. Opt-in (see <c>UpdateCheckPreference</c>) and
/// anonymous: no token, no account, and nothing about this install in the request — only a fixed
/// User-Agent, which GitHub's API requires. <c>/releases/latest</c> already skips drafts and
/// pre-releases.
///
/// Nothing here throws past the caller: any failure is simply "no answer", and the next check
/// tries again.
/// </summary>
public static class UpdateChecker
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/andrewbadge/BorisCodeStatus/releases/latest";

    /// <summary>Where the card's link goes.</summary>
    public const string ReleasesPageUrl = "https://github.com/andrewbadge/BorisCodeStatus/releases";

    /// <summary>The newest release's version, or null if it could not be fetched or understood.</summary>
    public static async Task<Version?> GetLatestAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.UserAgent.ParseAdd("BorisCodeStatus");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

            return document.RootElement.TryGetProperty("tag_name", out var tag) ? ParseTag(tag.GetString()) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>"v1.7.0" or "1.7.0" to a version; anything else, including a pre-release suffix, is null.</summary>
    public static Version? ParseTag(string? tag)
    {
        var text = tag?.Trim();
        if (text is { Length: > 0 } && text[0] is 'v' or 'V')
        {
            text = text[1..];
        }

        // Two or three numeric parts only: Version would also accept four, which no release uses.
        return Version.TryParse(text, out var version) && version.Revision < 0 ? version : null;
    }

    /// <summary>Whether <paramref name="latest"/> is a newer release than the running <paramref name="current"/>.</summary>
    public static bool IsNewer(Version latest, string? current) =>
        ParseTag(current) is { } running && Normalise(latest) > Normalise(running);

    /// <summary>"1.7" and "1.7.0" are the same release.</summary>
    private static Version Normalise(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
