using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using PlayerSettings;

namespace AdminUtils;

/// <summary>
/// Persistent ESP preference via the PlayerSettings core plugin (NickFox007, capability
/// "settings:nfcore"). The css_esp choice is stored per player so it survives reconnects and server
/// restarts - just like CS2-Tags uses PlayerSettings. Soft dependency: if the core plugin is not
/// installed, everything falls back to the in-memory wantESP[] (session-only), no crash.
///
/// Stored value: "1" = ESP wanted, "0"/absent = not. Key: PlayerSettingsKey.
/// </summary>
public sealed partial class AdminUtils
{
    private const string PlayerSettingsKey = "adminutils_esp";

    private readonly PluginCapability<ISettingsApi?> _settingsCapability = new("settings:nfcore");
    private ISettingsApi? _settings;
    private Action<CCSPlayerController>? _settingsHook;

    /// <summary>Resolve the PlayerSettings API and register the toggle + load hook. Best-effort.</summary>
    private void Esp_InitPlayerSettings()
    {
        try
        {
            _settings = _settingsCapability.Get();
        }
        catch
        {
            _settings = null;
        }

        if (_settings is null)
        {
            Logger.LogInformation("PlayerSettings core not found - ESP preference is session-only (not persisted).");
            return;
        }

        // Expose the ESP toggle in the PlayerSettings menu.
        try { _settings.RegisterTogglableSetting("Admin ESP", "Admin ESP"); }
        catch { /* older cores may not support this - ignore */ }

        // When a player's settings finish loading (on connect), restore their saved preference.
        _settingsHook = Esp_OnSettingsLoaded;
        _settings.AddHook(_settingsHook);

        Logger.LogInformation("PlayerSettings core found - ESP preference will persist per player.");
    }

    private void Esp_ShutdownPlayerSettings()
    {
        if (_settings is not null && _settingsHook is not null)
        {
            try { _settings.RemHook(_settingsHook); }
            catch { /* ignore */ }
        }
        _settingsHook = null;
        _settings = null;
    }

    /// <summary>Called by PlayerSettings once a player's stored settings are available.</summary>
    private void Esp_OnSettingsLoaded(CCSPlayerController player)
    {
        if (player is null || player.IsValid is not true || player.IsBot) return;
        if (player.Slot < 0 || player.Slot >= 64) return;

        wantESP[player.Slot] = Esp_LoadPreference(player);
        // Note: ESP only actually activates while dead/spectating; the stored "want" just means it
        // re-applies automatically when the player is eligible (handled in Esp_OnPlayerDeath, etc.).
    }

    /// <summary>Reads the saved ESP preference for a player (false if PlayerSettings is absent).</summary>
    private bool Esp_LoadPreference(CCSPlayerController player)
    {
        if (_settings is null) return false;
        try
        {
            return _settings.GetPlayerSettingsValue(player, PlayerSettingsKey, "0") == "1";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Persists the ESP preference for a player (no-op if PlayerSettings is absent).</summary>
    private void Esp_SavePreference(CCSPlayerController player, bool wanted)
    {
        if (_settings is null) return;
        try
        {
            _settings.SetPlayerSettingsValue(player, PlayerSettingsKey, wanted ? "1" : "0");
        }
        catch
        {
            /* ignore - preference stays in-memory for the session */
        }
    }
}
