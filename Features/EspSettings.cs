using CounterStrikeSharp.API.Core;

namespace AdminUtils;

/// <summary>
/// Persistent ESP preference via the shared PlayerSettings core (see PlayerSettingsCore.cs). The
/// css_esp choice is stored per player so it survives reconnects and server restarts - just like
/// CS2-Tags uses PlayerSettings. If the core plugin is absent, everything falls back to the in-memory
/// wantESP[] (session-only), no crash.
///
/// Stored value: "1" = ESP wanted, "0"/absent = not. Key: EspSettingsKey.
/// </summary>
public sealed partial class AdminUtils
{
    private const string EspSettingsKey = "adminutils_esp";

    /// <summary>Wire ESP into the shared PlayerSettings core: resolve it and register our callbacks.</summary>
    private void Esp_InitPlayerSettings()
    {
        Settings_Init();
        Settings_RegisterToggle("Admin ESP", "Admin ESP");
        Settings_AddLoadedCallback(Esp_OnSettingsLoaded);
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
        return Settings_Get(player, EspSettingsKey, "0") == "1";
    }

    /// <summary>Persists the ESP preference for a player (no-op if PlayerSettings is absent).</summary>
    private void Esp_SavePreference(CCSPlayerController player, bool wanted)
    {
        Settings_Set(player, EspSettingsKey, wanted ? "1" : "0");
    }
}
