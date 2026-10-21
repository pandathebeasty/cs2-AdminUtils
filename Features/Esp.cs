using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;

namespace AdminUtils;

/// <summary>
/// ESP feature (port of aquevadis/bg-koka-cs2-xray-esp, simplified to a single persistent toggle).
///
/// css_esp toggles a persistent glow on all players. It is allowed for admins only, and only while
/// the admin is dead or on the spectator team (AllowEspWhenDead controls whether dead-but-not-spec
/// counts). Once on, it re-applies after death and stays on until toggled off, the admin becomes
/// alive (e.g. bot takeover / respawn), or they disconnect. Glow props are filtered out of every
/// non-ESP viewer's transmit, so only the admin with ESP on sees them.
///
/// All feedback is sent only to the admin who triggered the command.
/// </summary>
public sealed partial class AdminUtils
{
    private void Esp_Load(bool hotReload)
    {
        Esp_InitPlayerSettings();
        Esp_RegisterListeners();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p is not null
                                                                     && p.IsValid is true
                                                                     && p.Connected is PlayerConnectedState.Connected))
            {
                if (cachedPlayers.Contains(player) is not true)
                    cachedPlayers.Add(player);

                // Restore the persisted ESP preference for already-connected players on hot reload.
                if (player.IsBot is not true)
                    wantESP[player.Slot] = Esp_LoadPreference(player);
            }
        }
    }

    private void Esp_Unload(bool hotReload)
    {
        Esp_DeregisterListeners();
        Esp_ShutdownPlayerSettings();
        RemoveAllGlowingPlayers();
    }

    /// <summary>True if the player may currently use ESP: dead, or on the spectator team.</summary>
    private static bool Esp_CanUse(CCSPlayerController player)
    {
        return player.Team is CsTeam.Spectator || player.PawnIsAlive is not true;
    }

    // ----- Command ----------------------------------------------------------------------------

    [ConsoleCommand("css_esp", "Toggle persistent Admin ESP (admins only, while dead or spectating)")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnToggleAdminEsp(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || player.IsValid is not true) return;

        if (!Config.Esp.Enabled)
        {
            Message(player, "Esp.Disabled");
            return;
        }

        if (AdminManager.PlayerHasPermissions(player, Config.Esp.AdminFlag) is not true)
        {
            Message(player, "Esp.NoPermission");
            return;
        }

        if (!Esp_CanUse(player))
        {
            Message(player, "Esp.OnlyDeadOrSpec");
            return;
        }

        // Persistent toggle - also saved to PlayerSettings so it survives reconnects/restarts.
        wantESP[player.Slot] = !wantESP[player.Slot];
        toggleAdminESP[player.Slot] = wantESP[player.Slot];
        Esp_SavePreference(player, wantESP[player.Slot]);

        if (toggleAdminESP[player.Slot])
        {
            togglePlayersGlowing = true;
            SetAllPlayersGlowing();
            Message(player, "Esp.Enabled");
        }
        else
        {
            if (AreThereEsperingAdmins() is not true)
                RemoveAllGlowingPlayers();
            Message(player, "Esp.Toggled.Off");
        }
    }

    // ----- Listeners --------------------------------------------------------------------------

    private void Esp_RegisterListeners()
    {
        RegisterListener<Listeners.OnClientAuthorized>(Esp_OnClientAuthorized);
        RegisterListener<Listeners.OnClientConnected>(Esp_OnClientConnected);
        RegisterListener<Listeners.OnClientPutInServer>(Esp_OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(Esp_OnClientDisconnected);
        RegisterListener<Listeners.CheckTransmit>(Esp_CheckTransmitListener);

        RegisterEventHandler<EventPlayerSpawn>(Esp_OnPlayerSpawn, HookMode.Pre);
        RegisterEventHandler<EventPlayerDeath>(Esp_OnPlayerDeath);
        RegisterEventHandler<EventRoundStart>(Esp_OnRoundStart);
    }

    private void Esp_DeregisterListeners()
    {
        RemoveListener<Listeners.OnClientAuthorized>(Esp_OnClientAuthorized);
        RemoveListener<Listeners.OnClientConnected>(Esp_OnClientConnected);
        RemoveListener<Listeners.OnClientPutInServer>(Esp_OnClientPutInServer);
        RemoveListener<Listeners.OnClientDisconnect>(Esp_OnClientDisconnected);
        RemoveListener<Listeners.CheckTransmit>(Esp_CheckTransmitListener);

        DeregisterEventHandler<EventPlayerSpawn>(Esp_OnPlayerSpawn, HookMode.Pre);
        DeregisterEventHandler<EventPlayerDeath>(Esp_OnPlayerDeath);
        DeregisterEventHandler<EventRoundStart>(Esp_OnRoundStart);
    }

    private void Esp_OnClientAuthorized(int slot, SteamID steamid)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || player.IsValid is not true) return;
        if (cachedPlayers.Contains(player) is not true)
            cachedPlayers.Add(player);
    }

    private void Esp_OnClientConnected(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || player.IsValid is not true) return;
        if (cachedPlayers.Contains(player) is not true)
            cachedPlayers.Add(player);
    }

    private void Esp_OnClientPutInServer(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is null || player.IsBot is not true) return;
        if (cachedPlayers.Contains(player) is not true)
            cachedPlayers.Add(player);
    }

    private void Esp_CheckTransmitListener(CCheckTransmitInfoList infoList)
    {
        foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
        {
            if (player is null || player.IsValid is not true) continue;

            // If the viewer with ESP on is now alive (respawn / bot takeover), disable it instantly.
            if (player.PawnIsAlive && toggleAdminESP[player.Slot])
            {
                toggleAdminESP[player.Slot] = false;
                if (AreThereEsperingAdmins() is not true)
                    RemoveAllGlowingPlayers();
                Message(player, "Esp.DisabledAlive");
            }

            // If this viewer has ESP on, let them see the glow props. Otherwise, hide every glow prop
            // from them.
            if (toggleAdminESP[player.Slot] == true)
                continue;

            foreach (var glowingProp in glowingPlayers)
            {
                if (glowingProp.Value.Item1 is not null && glowingProp.Value.Item1.IsValid is true
                    && glowingProp.Value.Item2 is not null && glowingProp.Value.Item2.IsValid is true)
                {
                    info.TransmitEntities.Remove((int)glowingProp.Value.Item1.Index);
                    info.TransmitEntities.Remove((int)glowingProp.Value.Item2.Index);
                }
            }
        }
    }

    public HookResult Esp_OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is null || player.IsValid is not true
            || player.Connected is not PlayerConnectedState.Connected) return HookResult.Continue;

        // Becoming alive turns ESP off for the viewer (it is a spectate-only feature).
        if (toggleAdminESP[player.Slot])
        {
            toggleAdminESP[player.Slot] = false;
            if (AreThereEsperingAdmins() is not true)
                RemoveAllGlowingPlayers();
            Message(player, "Esp.DisabledSpawn");
        }

        return HookResult.Continue;
    }

    public HookResult Esp_OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // Keep ESP on for admins who still want it and are still eligible (dead or spectating);
        // drop it for everyone else.
        for (int i = 0; i < cachedPlayers.Count; i++)
        {
            if (cachedPlayers[i] is null || cachedPlayers[i].IsValid is not true) continue;
            var slot = cachedPlayers[i].Slot;

            if (wantESP[slot] && Esp_CanUse(cachedPlayers[i]))
                continue;

            toggleAdminESP[slot] = false;
        }

        togglePlayersGlowing = false;

        // Glow props are tied to this round's pawns, so rebuild them next frame for the still-active
        // espers (or clear them if nobody has ESP on anymore).
        Server.NextFrame(() =>
        {
            if (AreThereEsperingAdmins() is true)
                SetAllPlayersGlowing();
            else
                RemoveAllGlowingPlayers();
        });

        return HookResult.Continue;
    }

    public HookResult Esp_OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is null || player.IsValid is not true
            || player.Connected is not PlayerConnectedState.Connected) return HookResult.Continue;

        // Remove this player's own glow prop on death.
        if (glowingPlayers.ContainsKey(player.Slot) is true)
        {
            if (glowingPlayers[player.Slot].Item1 is not null && glowingPlayers[player.Slot].Item1.IsValid is true
                && glowingPlayers[player.Slot].Item2 is not null && glowingPlayers[player.Slot].Item2.IsValid is true)
            {
                glowingPlayers[player.Slot].Item1.AcceptInput("Kill");
                glowingPlayers[player.Slot].Item2.AcceptInput("Kill");
            }

            glowingPlayers.Remove(player.Slot);
        }

        // Persistent ESP: re-enable automatically after death if this admin wanted it on.
        AddTimer(0.5f, () =>
        {
            if (player is null || player.IsValid is not true || player.PawnIsAlive) return;
            if (wantESP[player.Slot] && Esp_CanUse(player))
            {
                toggleAdminESP[player.Slot] = true;
                togglePlayersGlowing = true;
                SetAllPlayersGlowing();
                Message(player, "Esp.ReEnabled");
            }
        });

        return HookResult.Continue;
    }

    private void Esp_OnClientDisconnected(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || player.IsValid is not true) return;

        // Clear the live/active state, but NOT wantESP: the preference is persisted in PlayerSettings
        // and restored on the next connect, so it survives reconnects and restarts.
        toggleAdminESP[slot] = false;

        if (cachedPlayers.Contains(player) is true)
            cachedPlayers.Remove(player);
    }
}
