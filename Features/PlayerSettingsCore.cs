using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using PlayerSettings;

namespace AdminUtils;

/// <summary>
/// Shared access to the PlayerSettings core plugin (NickFox007, capability "settings:nfcore"), used
/// by any feature that persists a per-player preference (ESP, Spy, ...). Resolving the capability and
/// adding the "settings loaded" hook is done ONCE here, no matter how many features use it, because
/// the core exposes a single API instance and a single hook list. Soft dependency: if the core is not
/// installed, every read/write is a no-op and features fall back to their in-memory defaults.
/// </summary>
public sealed partial class AdminUtils
{
    private readonly PluginCapability<ISettingsApi?> _settingsCapability = new("settings:nfcore");
    private ISettingsApi? _settings;
    private bool _settingsInitTried;

    // Per-feature callbacks invoked when a player's stored settings finish loading (on connect). Each
    // feature adds its own; we forward the single core hook to all of them.
    private readonly List<Action<CCSPlayerController>> _settingsLoadedCallbacks = new();
    private Action<CCSPlayerController>? _settingsCoreHook;

    /// <summary>
    /// Resolves the PlayerSettings API once (idempotent) and wires the core "settings loaded" hook.
    /// Safe to call from multiple features' Load - only the first call does the work.
    /// </summary>
    private void Settings_Init()
    {
        if (_settingsInitTried) return;
        _settingsInitTried = true;

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
            Logger.LogInformation("PlayerSettings core not found - per-player preferences are session-only (not persisted).");
            return;
        }

        _settingsCoreHook = Settings_OnLoaded;
        _settings.AddHook(_settingsCoreHook);

        Logger.LogInformation("PlayerSettings core found - per-player preferences will persist.");
    }

    /// <summary>Removes the core hook. Called once from Unload after all features have shut down.</summary>
    private void Settings_Shutdown()
    {
        if (_settings is not null && _settingsCoreHook is not null)
        {
            try { _settings.RemHook(_settingsCoreHook); }
            catch { /* ignore */ }
        }
        _settingsCoreHook = null;
        _settings = null;
        _settingsInitTried = false;
        _settingsLoadedCallbacks.Clear();
    }

    /// <summary>Registers a callback for when a player's stored settings are available (on connect).</summary>
    private void Settings_AddLoadedCallback(Action<CCSPlayerController> callback)
    {
        if (callback is not null && !_settingsLoadedCallbacks.Contains(callback))
            _settingsLoadedCallbacks.Add(callback);
    }

    private void Settings_OnLoaded(CCSPlayerController player)
    {
        if (player is null || player.IsValid is not true || player.IsBot) return;

        foreach (var cb in _settingsLoadedCallbacks.ToArray())
        {
            try { cb(player); } catch { /* a feature callback must not break others */ }
        }
    }

    /// <summary>Reads a stored string value for a player (returns <paramref name="fallback"/> if absent).</summary>
    private string Settings_Get(CCSPlayerController player, string key, string fallback)
    {
        if (_settings is null) return fallback;
        try { return _settings.GetPlayerSettingsValue(player, key, fallback); }
        catch { return fallback; }
    }

    /// <summary>Persists a string value for a player (no-op if the core is absent).</summary>
    private void Settings_Set(CCSPlayerController player, string key, string value)
    {
        if (_settings is null) return;
        try { _settings.SetPlayerSettingsValue(player, key, value); }
        catch { /* ignore - stays in-memory for the session */ }
    }

    /// <summary>Exposes a togglable setting in the PlayerSettings menu (best-effort).</summary>
    private void Settings_RegisterToggle(string displayName, string description)
    {
        if (_settings is null) return;
        try { _settings.RegisterTogglableSetting(displayName, description); }
        catch { /* older cores may not support this - ignore */ }
    }
}
