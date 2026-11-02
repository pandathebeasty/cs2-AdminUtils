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
///   - see the ENEMY team's team-chat (say_team from the other team is echoed to them), and
///   - can HEAR the enemy team's voice (ListenAll voice flags), toggleable with css_voicespy.
///
/// Flags support "@permission", "#group" and plain command overrides. The command is always
/// registered; if the feature is disabled in config it replies that it's off.
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

    // The ChatManager API, resolved in OnAllPluginsLoaded. Null = ChatManager not installed, so the
    // Spy feature uses its own say_team hook instead of an observer.
    private IChatManagerApi? _spyChatManager;

    // True once we've registered our observer with ChatManager (so Unload knows to deregister).
    private bool _spyObserverRegistered;

    private void Spy_Load(bool hotReload)
    {
        RegisterEventHandler<EventPlayerConnectFull>(Spy_OnPlayerConnect);

        // On hot reload, apply voice flags to already-connected players.
        if (hotReload && Config.Spy.VoiceSpy)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (Spy_ClientIsValid(player))
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
    /// admin on the ENEMY team who holds a spy flag. Used by both the ChatManager observer and the
    /// standalone say_team hook.
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
            if (admin.Team == player.Team) continue; // only the ENEMY team's chat

            admin.PrintToChat(formatted);
        }
    }

    private HookResult Spy_OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (!Config.Spy.VoiceSpy)
            return HookResult.Continue;

        var player = @event.Userid;
        if (player is { IsValid: true })
            Spy_ApplyVoiceFlags(player);

        return HookResult.Continue;
    }

    /// <summary>Admins hear everyone; everyone else hears only their team.</summary>
    private void Spy_ApplyVoiceFlags(CCSPlayerController player)
    {
        player.VoiceFlags = Spy_HasPermission(player) ? VoiceFlags.ListenAll : VoiceFlags.ListenTeam;
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

        if (player.VoiceFlags.HasFlag(VoiceFlags.ListenAll))
        {
            player.VoiceFlags = VoiceFlags.ListenTeam;
            Message(player, "spy.voice.team");
        }
        else
        {
            player.VoiceFlags = VoiceFlags.ListenAll;
            Message(player, "spy.voice.all");
        }
    }

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
