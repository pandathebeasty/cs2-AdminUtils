using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace AdminUtils;

/// <summary>
/// AdminUtils - two admin spectator features combined into one plugin:
///   - AllSpec (Features/AllSpec.cs): spectate everyone while spectating, gated by a flag.
///   - Esp     (Features/Esp*.cs):    toggleable glow/ESP on all players while spectating.
///
/// Each feature lives in its own file (partial class) and is independently enabled via the single
/// shared config (AdminUtils.json). Based on qstage/AdminAllSpec and aquevadis/bg-koka-cs2-xray-esp.
/// </summary>
public sealed partial class AdminUtils : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "AdminUtils";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "panda";
    public override string ModuleDescription => "Admin utilities: all-spec + ESP.";

    public Config Config { get; set; } = new();

    public void OnConfigParsed(Config config)
    {
        Config = config;
    }

    /// <summary>
    /// Sends a localized, prefixed message to a single player. The key is looked up in lang/*.json via
    /// the CounterStrikeSharp Localizer; {color} tags in the translation are rendered by CSSharp.
    /// All plugin chat goes only to the player passed in (never broadcast).
    /// </summary>
    private void Message(CCSPlayerController? player, string key)
    {
        if (player is null || player.IsValid is not true) return;
        player.PrintToChat($" {Localizer["Prefix"]}{Localizer[key]}");
    }

    public override void Load(bool hotReload)
    {
        Logger.LogInformation("Loading {Name} v{Version} (hotReload: {HotReload})", ModuleName, ModuleVersion, hotReload);

        if (Config.AllSpec.Enabled)
        {
            AllSpec_Load(hotReload);
            Logger.LogInformation("AllSpec feature enabled (flag: {Flag})", Config.AllSpec.Flag);
        }

        if (Config.Esp.Enabled)
        {
            Esp_Load(hotReload);
            Logger.LogInformation("ESP feature enabled (flag: {Flag})", Config.Esp.AdminFlag);
        }

        Logger.LogInformation("Loaded {Name} v{Version}", ModuleName, ModuleVersion);
    }

    public override void Unload(bool hotReload)
    {
        if (Config.AllSpec.Enabled)
        {
            AllSpec_Unload(hotReload);
        }

        if (Config.Esp.Enabled)
        {
            Esp_Unload(hotReload);
        }
    }
}
