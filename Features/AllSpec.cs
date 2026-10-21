using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using Microsoft.Extensions.Logging;

namespace AdminUtils;

/// <summary>
/// AllSpec feature (port of qstage/AdminAllSpec). Hooks
/// CPlayer_ObserverServices::IsValidObserverTarget and forces it to return true for admins holding
/// the configured flag, letting them spectate any connected, alive player on either team.
///
/// The native signature lives in gamedata/AdminUtils.json under the key
/// "CPlayer_ObserverServices::IsValidObserverTarget".
/// </summary>
public sealed partial class AdminUtils
{
    private readonly bool[] _allSpecHasFlag = new bool[64];

    private MemoryFunctionWithReturn<CPlayer_ObserverServices, CCSPlayerPawn, bool>? _isValidObserverTarget;

    private void AllSpec_Load(bool hotReload)
    {
        try
        {
            _isValidObserverTarget = new MemoryFunctionWithReturn<CPlayer_ObserverServices, CCSPlayerPawn, bool>(
                GameData.GetSignature("CPlayer_ObserverServices::IsValidObserverTarget"));
            _isValidObserverTarget.Hook(Hook_IsValidObserverTarget, HookMode.Pre);
        }
        catch (Exception e)
        {
            // Bad/stale signature for this build: stay idle instead of crashing the server.
            Logger.LogWarning("AllSpec could not hook (bad/missing signature?): {Error}", e.Message);
            _isValidObserverTarget = null;
            return;
        }

        RegisterEventHandler<EventPlayerConnectFull>(AllSpec_OnPlayerConnectFull);

        if (hotReload)
        {
            AllSpec_RefreshAll();
        }
    }

    private void AllSpec_Unload(bool hotReload)
    {
        if (_isValidObserverTarget is not null)
        {
            try { _isValidObserverTarget.Unhook(Hook_IsValidObserverTarget, HookMode.Pre); }
            catch { /* ignore */ }
            _isValidObserverTarget = null;
        }

        DeregisterEventHandler<EventPlayerConnectFull>(AllSpec_OnPlayerConnectFull);
    }

    private void AllSpec_RefreshAll()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            AllSpec_CachePlayerFlag(player);
        }
    }

    private HookResult AllSpec_OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        AllSpec_CachePlayerFlag(@event.Userid);
        return HookResult.Continue;
    }

    private void AllSpec_CachePlayerFlag(CCSPlayerController? player)
    {
        if (player is not { IsValid: true, IsBot: false }) return;
        _allSpecHasFlag[player.Slot] = !string.IsNullOrEmpty(Config.AllSpec.Flag)
                                       && AdminManager.PlayerHasPermissions(player, Config.AllSpec.Flag);
    }

    private HookResult Hook_IsValidObserverTarget(DynamicHook hook)
    {
        var observerPawn = hook.GetParam<CPlayer_ObserverServices>(0).Pawn.Value.As<CCSPlayerPawn>();
        if (observerPawn is not { IsValid: true }) return HookResult.Continue;

        var observerPlayer = observerPawn.OriginalController.Value;
        if (observerPlayer is not { IsValid: true }) return HookResult.Continue;

        var targetPawn = hook.GetParam<CCSPlayerPawn>(1);
        if (targetPawn is not { IsValid: true, LifeState: (byte)LifeState_t.LIFE_ALIVE }) return HookResult.Continue;

        var targetPlayer = targetPawn.OriginalController.Value;
        if (targetPlayer is not { Connected: PlayerConnectedState.Connected, IsValid: true, TeamNum: > 1 })
            return HookResult.Continue;

        if (_allSpecHasFlag[observerPlayer.Slot])
        {
            hook.SetReturn(true);
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }
}
