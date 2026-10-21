using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace AdminUtils;

/// <summary>
/// ESP glow implementation (port of aquevadis/bg-koka-cs2-xray-esp). Each glowing player gets two
/// follower prop_dynamic entities (a hidden relay + a glowing model) that track the player's pawn.
/// Admins with ESP on receive these props; everyone else has them filtered out in CheckTransmit.
/// </summary>
public sealed partial class AdminUtils
{
    // player slot -> (relay prop, glow prop)
    public Dictionary<int, Tuple<CBaseModelEntity, CBaseModelEntity>> glowingPlayers = new();

    public List<CCSPlayerController> cachedPlayers = new();

    // ESP enable state per slot, whether a persistent ESP is wanted, and whether glow props exist.
    public bool[] toggleAdminESP = new bool[64];
    public bool[] wantESP = new bool[64];
    public bool togglePlayersGlowing = false;

    public void SetAllPlayersGlowing()
    {
        for (int i = 0; i < cachedPlayers.Count; i++)
        {
            if (cachedPlayers[i] is null || cachedPlayers[i].IsValid is not true || cachedPlayers[i].PawnIsAlive is not true)
                continue;

            SetPlayerGlowing(cachedPlayers[i], cachedPlayers[i].TeamNum);
        }

        togglePlayersGlowing = true;
    }

    public void RemoveAllGlowingPlayers()
    {
        foreach (var glowingProp in glowingPlayers.Values)
        {
            if (glowingProp.Item1 is not null && glowingProp.Item1.IsValid is true
                && glowingProp.Item2 is not null && glowingProp.Item2.IsValid is true)
            {
                glowingProp.Item1.AcceptInput("Kill");
                glowingProp.Item2.AcceptInput("Kill");
            }
        }

        glowingPlayers.Clear();
        togglePlayersGlowing = false;
    }

    public bool AreThereEsperingAdmins()
    {
        foreach (var toggled in toggleAdminESP)
            if (toggled)
                return true;

        return false;
    }

    /// <summary>
    /// Parses a glow colour from config: a hex string ("#RRGGBB" / "RRGGBB") or a named .NET color
    /// ("Orange", "SkyBlue", ...). Returns <paramref name="fallback"/> if it can't be parsed.
    /// </summary>
    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();

        var hex = value.StartsWith('#') ? value[1..] : value;
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        try
        {
            var named = Color.FromName(value);
            if (named.IsKnownColor || named.A != 0 || named.R != 0 || named.G != 0 || named.B != 0)
                return named;
        }
        catch { /* fall through */ }

        return fallback;
    }

    public void SetPlayerGlowing(CCSPlayerController player, int team)
    {
        if (player is null || player.IsValid is not true
            || player.Connected is not PlayerConnectedState.Connected) return;

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn is null || playerPawn.IsValid is not true) return;

        CBaseModelEntity? modelGlow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        CBaseModelEntity? modelRelay = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");

        if (modelGlow is null || modelRelay is null
            || modelGlow.IsValid is not true || modelRelay.IsValid is not true) return;

        var playerCBodyComponent = playerPawn.CBodyComponent;
        if (playerCBodyComponent is null) return;

        var playerSceneMode = playerCBodyComponent.SceneNode;
        if (playerSceneMode is null) return;

        string modelName = playerSceneMode.GetSkeletonInstance().ModelState.ModelName;

        modelRelay.SetModel(modelName);
        modelRelay.Spawnflags = 256u;
        modelRelay.RenderMode = RenderMode_t.kRenderNone;
        modelRelay.DispatchSpawn();

        modelGlow.SetModel(modelName);
        modelGlow.Spawnflags = 256u;
        modelGlow.DispatchSpawn();

        switch (team)
        {
            case 2:
                modelGlow.Glow.GlowColorOverride = ParseColor(Config.Esp.GlowColorT, Color.Orange); // T
                break;
            case 3:
                modelGlow.Glow.GlowColorOverride = ParseColor(Config.Esp.GlowColorCT, Color.SkyBlue); // CT
                break;
        }

        modelGlow.Glow.GlowRange = 5000;
        modelGlow.Glow.GlowTeam = -1;
        modelGlow.Glow.GlowType = 3;
        modelGlow.Glow.GlowRangeMin = 100;

        modelRelay.AcceptInput("FollowEntity", playerPawn, modelRelay, "!activator");
        modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator");

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

        glowingPlayers.Add(player.Slot, new Tuple<CBaseModelEntity, CBaseModelEntity>(modelRelay, modelGlow));
    }
}
