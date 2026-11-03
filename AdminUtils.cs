using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace AdminUtils;

/// <summary>
/// AdminUtils - three admin features combined into one plugin:
///   - AllSpec (Features/AllSpec.cs): spectate everyone while spectating, gated by a flag.
///   - Esp     (Features/Esp*.cs):    toggleable glow/ESP on all players while spectating.
///   - Spy     (Features/Spy.cs):     see enemy team-chat and hear enemy voice (css_voicespy).
///
/// Each feature lives in its own file (partial class) and is independently enabled via the single
/// shared config (AdminUtils.json).
/// </summary>
public sealed partial class AdminUtils : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "AdminUtils";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "panda";
    public override string ModuleDescription => "Admin utilities: SpecAll, ESP, Spy other team.";

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

        if (Config.Spy.Enabled)
        {
            Spy_Load(hotReload);
            Logger.LogInformation("Spy feature enabled (chat: {Chat}, voice: {Voice})", Config.Spy.ChatSpy, Config.Spy.VoiceSpy);
        }

        Logger.LogInformation("Loaded {Name} v{Version}", ModuleName, ModuleVersion);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        // Spy's chat observer depends on ChatManager, which can only be resolved once every plugin
        // has loaded. When ChatManager is present, chat spy switches from the say_team hook to a
        // ChatManager observer so tagged players are not missed.
        if (Config.Spy.Enabled)
        {
            Spy_OnAllPluginsLoaded();
        }
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

        if (Config.Spy.Enabled)
        {
            Spy_Unload(hotReload);
        }

        // Shared PlayerSettings core: shut down once, after every feature has unhooked.
        Settings_Shutdown();
    }
}
