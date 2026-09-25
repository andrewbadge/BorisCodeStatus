using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BorisClaudeNotifications.Core.Models;
using BorisClaudeNotifications.Core.State;

namespace BorisClaudeNotifications.Core;

/// <summary>
/// Best-effort client for Anthropic's <b>undocumented</b> <c>/api/oauth/usage</c> endpoint.
///
/// This is a fallback only, for data the statusLine hook does not carry (currently just the
/// Sonnet-only weekly split). The endpoint rate-limits aggressively and stays limited for an
/// extended period once tripped, so calls are throttled to <see cref="MinimumInterval"/> via a
/// stamp file shared across processes, and a 429 triggers an additional long back-off.
///
/// Nothing here throws past the caller: on any failure the previous state simply stands.
/// </summary>
public sealed class UsageApiClient : IDisposable
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string OAuthBetaHeader = "oauth-2025-04-20";

    /// <summary>Floor between calls. The endpoint punishes frequent polling; do not lower this.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(5);

    /// <summary>Applied on top of the floor after a 429, because the limit persists well past one window.</summary>
    public static readonly TimeSpan RateLimitedBackoff = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Candidate property names for each window. The response shape is not contractual, so try
    // the plausible spellings and accept whichever the server actually sends.
    private static readonly string[] SonnetWeekKeys =
        ["seven_day_sonnet", "sonnet_seven_day", "seven_day_sonnet_4"];

    private readonly HttpClient _http;
    private readonly string _stampPath;
    private readonly bool _ownsHttpClient;
    private DateTimeOffset _nextAllowedCall = DateTimeOffset.MinValue;

    public UsageApiClient(HttpClient? http = null, string? stampPath = null)
    {
        _ownsHttpClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _stampPath = stampPath ?? VitalsPaths.UsageApiStampFile;
    }

    /// <summary>
    /// Fetches the Sonnet-only weekly window if the throttle allows and a token is available.
    /// Returns null when throttled, unauthenticated, or on any error — all of which are normal.
    /// </summary>
    public async Task<RateLimitWindow?> TryGetSonnetWeekAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCallAllowed())
        {
            return null;
        }

        var token = TryReadAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            // Stamp before the call, not after: a request that hangs or fails must still count
            // against the throttle, or a failing endpoint would be hammered.
            StampCall(MinimumInterval);

            using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", OAuthBetaHeader);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                StampCall(RateLimitedBackoff);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ExtractWindow(json, SonnetWeekKeys);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Reads <c>claudeAiOauth.accessToken</c> from ~/.claude/.credentials.json, or null.</summary>
    public static string? TryReadAccessToken(string? credentialsPath = null)
    {
        var path = credentialsPath ?? VitalsPaths.CredentialsFile;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.TryGetProperty("accessToken", out var accessToken) &&
                accessToken.ValueKind == JsonValueKind.String)
            {
                return accessToken.GetString();
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pulls the first matching window out of a usage response. Exposed for testing because the
    /// response shape is undocumented and worth pinning against captured samples.
    /// </summary>
    public static RateLimitWindow? ExtractWindow(string json, params string[] candidateKeys)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var key in candidateKeys)
            {
                if (root.TryGetProperty(key, out var window) && window.ValueKind == JsonValueKind.Object)
                {
                    return ReadWindow(window);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RateLimitWindow? ReadWindow(JsonElement window)
    {
        double? used = null;
        foreach (var name in new[] { "utilization", "used_percentage", "usedPercentage", "percent_used" })
        {
            if (window.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                used = value.GetDouble();
                break;
            }
        }

        DateTimeOffset? resetsAt = null;
        foreach (var name in new[] { "resets_at", "resetsAt" })
        {
            if (!window.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                resetsAt = parsed;
                break;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch))
            {
                resetsAt = epoch > 10_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                    : DateTimeOffset.FromUnixTimeSeconds(epoch);
                break;
            }
        }

        return used is null && resetsAt is null
            ? null
            : new RateLimitWindow { UsedPercentage = used, ResetsAt = resetsAt };
    }

    private bool IsCallAllowed()
    {
        if (DateTimeOffset.UtcNow < _nextAllowedCall)
        {
            return false;
        }

        // The stamp file also holds off a second process (or a restarted tray) from re-polling.
        try
        {
            if (File.Exists(_stampPath))
            {
                var text = File.ReadAllText(_stampPath).Trim();
                if (DateTimeOffset.TryParse(text, out var nextAllowed) && DateTimeOffset.UtcNow < nextAllowed)
                {
                    _nextAllowedCall = nextAllowed;
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable stamp: fall through and rely on the in-memory gate.
        }

        return true;
    }

    private void StampCall(TimeSpan interval)
    {
        _nextAllowedCall = DateTimeOffset.UtcNow + interval;
        try
        {
            VitalsPaths.EnsureDataDirectory();
            File.WriteAllText(_stampPath, _nextAllowedCall.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In-memory gate still applies for this process.
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
