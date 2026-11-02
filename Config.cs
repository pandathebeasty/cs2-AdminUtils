using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace AdminUtils;

/// <summary>
/// Single, shared config for AdminUtils (configs/plugins/AdminUtils/AdminUtils.json).
/// Combines three features, each independently toggleable:
///   - AllSpec : admins with a flag can spectate everyone (incl. the enemy team) while spectating.
///   - Esp     : admins toggle a persistent glow/ESP on all players (css_esp) while dead/spectating.
///   - Spy     : admins with a flag see enemy team-chat and can hear enemy voice (css_voicespy).
/// </summary>
public sealed class Config : BasePluginConfig
{
    /// <summary>Chat prefix used by both features' messages. Supports {COLOR} tags (see Chat.cs).</summary>
    [JsonPropertyName("ChatPrefix")]
    public string ChatPrefix { get; set; } = "{GREEN}[AdminUtils]";

    /// <summary>When true, feature debug lines are printed to the server console.</summary>
    [JsonPropertyName("DebugMode")]
    public bool DebugMode { get; set; } = false;

    [JsonPropertyName("AllSpec")]
    public AllSpecConfig AllSpec { get; set; } = new();

    [JsonPropertyName("Esp")]
    public EspConfig Esp { get; set; } = new();

    [JsonPropertyName("Spy")]
    public SpyConfig Spy { get; set; } = new();
}

/// <summary>
/// AllSpec: hooks CPlayer_ObserverServices::IsValidObserverTarget so an admin holding <see cref="Flag"/>
/// is allowed to spectate any connected, alive player on either team while in spectator mode.
/// </summary>
public sealed class AllSpecConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Admin flag/permission required to spectate everyone. Empty = nobody.</summary>
    [JsonPropertyName("Flag")]
    public string Flag { get; set; } = "@css/generic";
}

/// <summary>
/// Esp: admins toggle a persistent player glow (wall-visible ESP) with css_esp. It stays on and
/// re-applies after death as long as the admin is dead or on the spectator team. Glow is implemented
/// with follower prop_dynamic entities and per-viewer entity transmit filtering.
/// </summary>
public sealed class EspConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Admin flag/permission required to use css_esp. Empty = nobody.</summary>
    [JsonPropertyName("AdminFlag")]
    public string AdminFlag { get; set; } = "@css/ban";

    /// <summary>
    /// Glow colour for Terrorist players, as a hex string (e.g. "#FFA500") or a named .NET color
    /// (e.g. "Orange"). Falls back to orange if it can't be parsed.
    /// </summary>
    [JsonPropertyName("GlowColorT")]
    public string GlowColorT { get; set; } = "#FFA500";

    /// <summary>
    /// Glow colour for Counter-Terrorist players, as a hex string (e.g. "#87CEEB") or a named .NET
    /// color (e.g. "SkyBlue"). Falls back to sky blue if it can't be parsed.
    /// </summary>
    [JsonPropertyName("GlowColorCT")]
    public string GlowColorCT { get; set; } = "#87CEEB";
}

/// <summary>
/// Spy (ported from itsAudioo/AdminSpy): admins holding one of <see cref="AdminFlags"/> see the enemy
/// team's team-chat, and - when VoiceSpy is on - can hear the enemy team's voice. css_voicespy toggles
/// a given admin's voice spy on/off. Flags support "@permission", "#group" and plain command overrides.
/// </summary>
public sealed class SpyConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Admin flags that grant spy access. Each entry is one of:
    ///   "@css/..."  = permission, "#css/..." = group, anything else = command override.
    /// </summary>
    [JsonPropertyName("AdminFlags")]
    public List<string> AdminFlags { get; set; } = ["@css/chat", "#css/admin"];

    /// <summary>See the enemy team's team-chat messages.</summary>
    [JsonPropertyName("ChatSpy")]
    public bool ChatSpy { get; set; } = true;

    /// <summary>Hear the enemy team's voice (admins get ListenAll voice flags on connect).</summary>
    [JsonPropertyName("VoiceSpy")]
    public bool VoiceSpy { get; set; } = true;
}
