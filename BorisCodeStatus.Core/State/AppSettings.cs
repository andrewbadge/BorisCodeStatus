using System.Text.Json.Serialization;

namespace BorisCodeStatus.Core.State;

/// <summary>A top-left screen position, as the status card was last left.</summary>
public sealed record CardPosition(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y);

/// <summary>
/// Every tray preference, as the contents of <c>settings.json</c>. A property's initial value is its
/// default, so a missing or unreadable file — and a member missing from one — yields the default
/// and nothing is written until the user changes something.
///
/// Not <c>state.json</c>, and not part of <c>/status</c>: that is the wire payload the hook process
/// rewrites constantly. The defaults are deliberate: the HTTP service and the usage API are off
/// because <c>/status</c> is unauthenticated and the usage API is the only thing that reads the
/// OAuth token or touches the network; sounds and the idle card are off because they are the most
/// intrusive things the app does.
///
/// Access goes through <see cref="SettingsStore"/>, usually via the <c>*Preference</c> classes.
/// </summary>
public sealed record AppSettings
{
    [JsonPropertyName("http_enabled")]
    public bool HttpEnabled { get; init; }

    [JsonPropertyName("usage_api_enabled")]
    public bool UsageApiEnabled { get; init; }

    /// <summary>The card shown when Claude starts waiting. On by default, unlike the idle card.</summary>
    [JsonPropertyName("notifications_enabled")]
    public bool NotificationsEnabled { get; init; } = true;

    [JsonPropertyName("idle_card_enabled")]
    public bool IdleCardEnabled { get; init; }

    [JsonPropertyName("waiting_sound_enabled")]
    public bool WaitingSoundEnabled { get; init; }

    [JsonPropertyName("idle_sound_enabled")]
    public bool IdleSoundEnabled { get; init; }

    [JsonPropertyName("pet")]
    public Pet Pet { get; init; } = Pet.Dog;

    /// <summary>
    /// The pets whose second sound (woof, meow, …) is chosen for each moment. Kept per pet so
    /// switching pets does not lose a choice.
    /// </summary>
    [JsonPropertyName("waiting_alternate_sound_pets")]
    public IReadOnlyList<Pet> WaitingAlternateSoundPets { get; init; } = [];

    [JsonPropertyName("idle_alternate_sound_pets")]
    public IReadOnlyList<Pet> IdleAlternateSoundPets { get; init; } = [];

    /// <summary>The process name a click on a card brings forward; null means a click only dismisses.</summary>
    [JsonPropertyName("click_target_app")]
    public string? ClickTargetApp { get; init; }

    [JsonPropertyName("status_card_pinned")]
    public bool StatusCardPinned { get; init; }

    [JsonPropertyName("status_card_mini")]
    public bool StatusCardMini { get; init; }

    /// <summary>
    /// Stored as it was left and only kept on screen when it is used, because the monitors
    /// present at that point are the ones that matter.
    /// </summary>
    [JsonPropertyName("status_card_position")]
    public CardPosition? StatusCardPosition { get; init; }

    /// <summary>Whether the tray may ask GitHub for the latest release. See <see cref="UpdateCheckPreference"/>.</summary>
    [JsonPropertyName("update_check_enabled")]
    public bool UpdateCheckEnabled { get; init; }

    /// <summary>The newest release already announced, so a restart does not announce it again.</summary>
    [JsonPropertyName("update_notified_version")]
    public string? UpdateNotifiedVersion { get; init; }

    /// <summary>The 5-hour usage, in percent, past which the warning card shows. See <see cref="SessionWarningPreference"/>.</summary>
    [JsonPropertyName("session_warning_percent")]
    public int SessionWarningPercent { get; init; } = SessionWarningPreference.Default;
}
