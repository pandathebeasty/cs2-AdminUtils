# AdminUtils

Admin utilities for Counter-Strike 2, built on
[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

- **AllSpec** — admins with a flag can spectate everyone, including the enemy team.
- **ESP** — admins can toggle a team-coloured glow on all players while dead or spectating.
- **Spy** — admins with a flag can toggle seeing the enemy team's chat (`css_chatspy`) and hearing the enemy team's voice (`css_voicespy`); both toggles persist per player.

> Author: **panda** · Version: **1.0.1** · Target: **.NET 10 / CounterStrikeSharp 1.0.376**

---

## Features

### AllSpec
Admins can spectate any connected, alive player on either team.

### ESP
`css_esp` toggles a glow on all players, visible through walls. Admins-only, and usable only while
**dead or on the spectator team**. Once on, it stays on and re-applies after each death for as long
as the admin stays eligible; it turns off when toggled again or when the admin becomes alive
(respawn / bot takeover). Glows are team-coloured (configurable) and only the admin with ESP on can
see them. All ESP feedback is sent only to the admin who ran the command.

The ESP choice is **persistent across reconnects and server restarts** when the optional
PlayerSettings core plugin is installed; otherwise it lasts for the current session.

### Spy
Admins holding one of the configured flags can see the **enemy team's team-chat** echoed to them
(`css_chatspy`) and, when voice spy is on, **hear the enemy team's voice** (`css_voicespy`, useful when
`sv_alltalk 0`). Each toggle is per admin and **persists per player** via the PlayerSettings core, so a
choice survives reconnects and restarts; with no stored choice, the config defaults apply. Chat spy and
voice spy can also each be turned off server-wide in config.

**Works with ChatManager.** If [ChatManager](https://github.com/pandathebeasty/cs2-ChatManager) is
installed, chat spy automatically switches to a read-only ChatManager observer, so it sees team-chat
even from players who have a tag (ChatManager would otherwise consume their chat before a plain hook
could read it). Without ChatManager, chat spy uses its own `say_team` listener. Either way no setup is
needed — detection is automatic at load. Voice spy does not depend on ChatManager.

---

## Installation

1. Install CounterStrikeSharp (Metamod + CSSharp) and confirm it loads.
2. Copy the **AdminUtils** folder into `game/csgo/addons/counterstrikesharp/plugins/`, so you end up
   with `.../plugins/AdminUtils/AdminUtils.dll`.
3. *(Optional, for persistent ESP)* Install the
   [PlayerSettings](https://github.com/NickFox007/PlayerSettingsCS2) core plugin.
4. Start the server, or hot-load with `css_plugins load AdminUtils`. The config is generated on first
   load.

Windows and Linux are both supported.

---

## Commands

| Command        | Who    | Description                                                                     |
|----------------|--------|---------------------------------------------------------------------------------|
| `css_esp`      | admins | Toggle ESP (while dead or spectating).                                          |
| `css_chatspy`  | admins | Toggle seeing the enemy team's team-chat.                                       |
| `css_voicespy` | admins | Toggle hearing the enemy team's voice (listen all ↔ team only).                 |

AllSpec has no command — it applies automatically to admins with the flag. Each admin's `css_chatspy`
and `css_voicespy` choice is **persisted per player** through the PlayerSettings core, so it survives
reconnects and server restarts; when a player has no stored choice yet, the config defaults
(`Spy.ChatSpy` / `Spy.VoiceSpy`) apply.

---

## Configuration

`addons/counterstrikesharp/configs/plugins/AdminUtils/AdminUtils.json` (generated on first load):

```jsonc
{
  "ChatPrefix": "{GREEN}[AdminUtils]",     // {COLOR} tags supported
  "DebugMode": false,                       // extra console logging

  "AllSpec": {
    "Enabled": true,
    "Flag": "@css/generic"                  // flag required to spectate everyone
  },

  "Esp": {
    "Enabled": true,
    "AdminFlag": "@css/ban",                // flag required to use css_esp
    "GlowColorT": "#FFA500",                // Terrorist glow colour (hex or named color)
    "GlowColorCT": "#87CEEB"                // Counter-Terrorist glow colour (hex or named color)
  }
}
```

- Glow colours accept a hex string (`#RRGGBB`) or a named colour (`Orange`, `SkyBlue`, ...).

---

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); all dependencies come
from NuGet.

```bash
dotnet build AdminUtils.csproj -c Release
```
