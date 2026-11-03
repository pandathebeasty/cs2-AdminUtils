using ChatManagerApi;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace AdminUtils;

/// <summary>
/// Spy feature (port of itsAudioo/AdminSpy). Admins with a configured flag:
///   - see the ENEMY team's team-chat (toggle with css_chatspy), and
///   - can HEAR the enemy team's voice (ListenAll voice flags, toggle with css_voicespy).
///
/// Flags support "@permission", "#group" and plain command overrides. Both toggles are PERSISTED
/// per player through the shared PlayerSettings core (PlayerSettingsCore.cs), so an admin's choice
/// survives reconnects and server restarts. When a player has no stored choice yet, the feature
/// defaults to the config values (Config.Spy.ChatSpy / Config.Spy.VoiceSpy).
///
/// Chat spy + ChatManager: ChatManager hooks say/say_team in Pre and returns Handled for any player
/// who has a tag, which consumes the command before our say_team Post hook runs - so a bare hook
/// would miss every tagged player. To avoid that race, when ChatManager is present we register a
/// read-only chat observer through its API (which it invokes for EVERY message, tagged or not) and
/// skip our own hook. When ChatManager is absent, we fall back to the standalone say_team Post hook.
/// Voice spy is unaffected by ChatManager - it only sets VoiceFlags.
/// </summary>
public sealed partial class AdminUtils
{
    private const string SpyObserverName = "adminutils-spy";
    private const string SpyChatSettingsKey = "adminutils_chatspy";
    private const string SpyVoiceSettingsKey = "adminutils_voicespy";

    // The ChatManager API, resolved in OnAllPluginsLoaded. Null = ChatManager not installed, so the
    // Spy feature uses its own say_team hook instead of an observer.
    private IChatManagerApi? _spyChatManager;

    // True once we've registered our observer with ChatManager (so Unload knows to deregister).
    private bool _spyObserverRegistered;

    // Per-slot toggle state (the live, in-memory preference). Loaded from PlayerSettings on connect,
    // falling back to the config defaults when the player has no stored choice.
    private readonly bool[] _spyChatWanted = new bool[64];
    private readonly bool[] _spyVoiceWanted = new bool[64];

    private void Spy_Load(bool hotReload)
    {
        // Persisted per-player toggles via the shared PlayerSettings core.
        Settings_Init();
        Settings_RegisterToggle("Admin Chat Spy", "Admin Chat Spy");
        Settings_RegisterToggle("Admin Voice Spy", "Admin Voice Spy");
        Settings_AddLoadedCallback(Spy_OnSettingsLoaded);

        RegisterEventHandler<EventPlayerConnectFull>(Spy_OnPlayerConnect);

        // On hot reload, prime defaults and re-apply voice flags for already-connected players.
        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!Spy_ClientIsValid(player)) continue;
                Spy_InitDefaults(player.Slot);
                if (Config.Spy.VoiceSpy)
                    Spy_ApplyVoiceFlags(player);
            }
        }

        // The say_team Post hook is only installed as a FALLBACK. If ChatManager turns out to be
        // present (resolved in Spy_OnAllPluginsLoaded) we remove this hook and use the observer
        // instead, so tagged players are not missed.
        AddCommandListener("say_team", Spy_OnTeamChat, HookMode.Post);
    }

    /// <summary>
    /// Called from the plugin's OnAllPluginsLoaded. Resolves the ChatManager API; if present, we
    /// register a read-only chat observer and drop our own say_team hook so chat spy also catches
    /// players who have a tag (ChatManager would otherwise consume their chat with Handled).
    /// </summary>
    private void Spy_OnAllPluginsLoaded()
    {
        try
        {
            _spyChatManager = IChatManagerApi.Capability.Get();
        }
        catch
        {
            _spyChatManager = null;
        }

        if (_spyChatManager == null)
        {
            Logger.LogInformation("[Spy] ChatManager not found - chat spy uses the say_team hook (tagged players may be missed if another plugin consumes chat).");
            return;
        }

        // ChatManager owns the say hook; drop our fallback and observe instead.
        RemoveCommandListener("say_team", Spy_OnTeamChat, HookMode.Post);
        _spyChatManager.RegisterChatObserver(SpyObserverName, Spy_OnChatObserved);
        _spyObserverRegistered = true;
        Logger.LogInformation("[Spy] ChatManager detected - chat spy uses the ChatManager observer (catches tagged players too).");
    }

    private void Spy_Unload(bool hotReload)
    {
        if (_spyObserverRegistered)
        {
            try { _spyChatManager?.UnregisterChatObserver(SpyObserverName); } catch { /* ignore */ }
            _spyObserverRegistered = false;
        }
        _spyChatManager = null;

        RemoveCommandListener("say_team", Spy_OnTeamChat, HookMode.Post);
        DeregisterEventHandler<EventPlayerConnectFull>(Spy_OnPlayerConnect);
    }

    // ----- Persistence ------------------------------------------------------------------------

    /// <summary>Seeds a slot's live toggles from the config defaults (used before a value is loaded).</summary>
    private void Spy_InitDefaults(int slot)
    {
        if (slot < 0 || slot >= 64) return;
        _spyChatWanted[slot] = Config.Spy.ChatSpy;
        _spyVoiceWanted[slot] = Config.Spy.VoiceSpy;
    }

    /// <summary>
    /// Called by PlayerSettings once a player's stored settings are available. Restores the saved
    /// chat/voice spy toggles (or the config default when nothing is stored), then applies voice.
    /// </summary>
    private void Spy_OnSettingsLoaded(CCSPlayerController player)
    {
        if (player is null || player.IsValid is not true || player.IsBot) return;
        var slot = player.Slot;
        if (slot < 0 || slot >= 64) return;

        // "1"/"0" when the player has chosen; anything else -> fall back to the config default.
        _spyChatWanted[slot] = Spy_LoadPreference(player, SpyChatSettingsKey, Config.Spy.ChatSpy);
        _spyVoiceWanted[slot] = Spy_LoadPreference(player, SpyVoiceSettingsKey, Config.Spy.VoiceSpy);

        if (Config.Spy.VoiceSpy)
            Spy_ApplyVoiceFlags(player);
    }

    private bool Spy_LoadPreference(CCSPlayerController player, string key, bool fallback)
    {
        var raw = Settings_Get(player, key, "");
        return raw switch { "1" => true, "0" => false, _ => fallback };
    }

    private void Spy_SavePreference(CCSPlayerController player, string key, bool wanted)
    {
        Settings_Set(player, key, wanted ? "1" : "0");
    }

    // ----- Chat spy ---------------------------------------------------------------------------

    /// <summary>ChatManager observer: only team chat is spied (matches AdminSpy's say_team behaviour).</summary>
    private void Spy_OnChatObserved(CCSPlayerController player, string message, bool teamOnly)
    {
        if (!teamOnly) return; // chat spy mirrors team-chat only
        Spy_EchoTeamChat(player, message);
    }

    /// <summary>Fallback say_team Post hook, used only when ChatManager is not installed.</summary>
    private HookResult Spy_OnTeamChat(CCSPlayerController? player, CommandInfo info)
    {
        if (player is null || player.IsValid is not true)
            return HookResult.Continue;

        Spy_EchoTeamChat(player, info.GetArg(1));
        return HookResult.Continue;
    }

    /// <summary>
    /// Shared echo: forwards <paramref name="player"/>'s team-chat <paramref name="message"/> to every
    /// admin on the ENEMY team who holds a spy flag AND has chat spy toggled on. Used by both the
    /// ChatManager observer and the standalone say_team hook.
    /// </summary>
    private void Spy_EchoTeamChat(CCSPlayerController player, string message)
    {
        if (!Config.Spy.ChatSpy)
            return;

        if (string.IsNullOrEmpty(message))
            return;

        // Don't echo chat commands.
        var first = message[..1];
        if (first is "/" or "!" or "@")
            return;

        var teamColor = Spy_TeamColor(player.Team);
        var dead = player.PawnIsAlive ? "" : Localizer["spy.isdead"].Value;
        var tag = Localizer["Prefix"].Value;

        var formatted = $" {Localizer["spy.message", tag, teamColor, player.PlayerName, dead]} {ChatColors.Default}: {message}";

        foreach (var admin in Utilities.GetPlayers())
        {
            if (!Spy_ClientIsValid(admin) || !Spy_HasPermission(admin)) continue;
            if (admin == player) continue;
            if (admin.Team == player.Team) continue;       // only the ENEMY team's chat
            if (!_spyChatWanted[admin.Slot]) continue;      // admin turned chat spy off for themselves

            admin.PrintToChat(formatted);
        }
    }

    [ConsoleCommand("css_chatspy", "Toggles seeing the enemy team's team-chat (admins only)")]
    public void OnChatSpyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || player.IsValid is not true)
            return;

        if (!Config.Spy.Enabled || !Config.Spy.ChatSpy)
        {
            Message(player, "spy.chat.disabled");
            return;
        }

        if (!Spy_HasPermission(player))
        {
            Message(player, "spy.nopermission");
            return;
        }

        var slot = player.Slot;
        _spyChatWanted[slot] = !_spyChatWanted[slot];
        Spy_SavePreference(player, SpyChatSettingsKey, _spyChatWanted[slot]);

        Message(player, _spyChatWanted[slot] ? "spy.chat.on" : "spy.chat.off");
    }

    // ----- Voice spy --------------------------------------------------------------------------

    private HookResult Spy_OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not { IsValid: true })
            return HookResult.Continue;

        // Seed the live toggles from config until PlayerSettings restores the stored choice (if any).
        Spy_InitDefaults(player.Slot);

        if (Config.Spy.VoiceSpy)
            Spy_ApplyVoiceFlags(player);

        return HookResult.Continue;
    }

    /// <summary>
    /// Applies voice flags for the player: an admin who has voice spy on hears everyone (ListenAll);
    /// everyone else (non-admin, or an admin who turned it off) hears only their team (ListenTeam).
    /// </summary>
    private void Spy_ApplyVoiceFlags(CCSPlayerController player)
    {
        var slot = player.Slot;
        var wants = slot >= 0 && slot < 64 && _spyVoiceWanted[slot];
        player.VoiceFlags = (Spy_HasPermission(player) && wants) ? VoiceFlags.ListenAll : VoiceFlags.ListenTeam;
    }

    [ConsoleCommand("css_voicespy", "Toggles hearing the enemy team's voice (admins only)")]
    public void OnVoiceSpyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || player.IsValid is not true)
            return;

        if (!Config.Spy.Enabled || !Config.Spy.VoiceSpy)
        {
            Message(player, "spy.disabled");
            return;
        }

        if (!Spy_HasPermission(player))
        {
            Message(player, "spy.nopermission");
            return;
        }

        var slot = player.Slot;
        _spyVoiceWanted[slot] = !_spyVoiceWanted[slot];
        Spy_SavePreference(player, SpyVoiceSettingsKey, _spyVoiceWanted[slot]);
        Spy_ApplyVoiceFlags(player);

        Message(player, _spyVoiceWanted[slot] ? "spy.voice.all" : "spy.voice.team");
    }

    // ----- Shared helpers ---------------------------------------------------------------------

    private string Spy_TeamColor(CsTeam team) => team switch
    {
        CsTeam.Terrorist => Localizer["spy.team.t"].Value,
        CsTeam.CounterTerrorist => Localizer["spy.team.ct"].Value,
        _ => Localizer["spy.team.spec"].Value
    };

    /// <summary>True if the player holds any configured spy flag (@permission / #group / override).</summary>
    private bool Spy_HasPermission(CCSPlayerController player)
    {
        foreach (var flag in Config.Spy.AdminFlags)
        {
            if (string.IsNullOrEmpty(flag)) continue;

            bool ok = flag[0] switch
            {
                '@' => AdminManager.PlayerHasPermissions(player, flag),
                '#' => AdminManager.PlayerInGroup(player, flag),
                _ => AdminManager.PlayerHasCommandOverride(player, flag)
            };

            if (ok) return true;
        }

        return false;
    }

    private static bool Spy_ClientIsValid(CCSPlayerController? player)
    {
        return player?.IsValid == true
               && player.PlayerPawn?.IsValid == true
               && !player.IsBot
               && !player.IsHLTV
               && player.Connected == PlayerConnectedState.Connected;
    }
}
