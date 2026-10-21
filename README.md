# AdminUtils

Admin spectator utilities for Counter-Strike 2, built on
[CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp). Two features in one plugin,
each independently toggleable from a single config:

- **AllSpec** — admins with a flag can spectate everyone, including the enemy team.
- **ESP** — admins can toggle a team-coloured glow on all players while dead or spectating.

> Author: **panda** · Version: **1.0.0** · Target: **.NET 10 / CounterStrikeSharp 1.0.376**

---

## Features

### AllSpec
Admins holding the configured flag can spectate any connected, alive player on either team. Fully
automatic — no command needed, it just works for anyone with the flag while they spectate.

### ESP
`css_esp` toggles a glow on all players, visible through walls. Admins-only, and usable only while
**dead or on the spectator team**. Once on, it stays on and re-applies after each death for as long
as the admin stays eligible; it turns off when toggled again or when the admin becomes alive
(respawn / bot takeover). Glows are team-coloured (configurable) and only the admin with ESP on can
see them. All ESP feedback is sent only to the admin who ran the command.

The ESP choice is **persistent across reconnects and server restarts** when the optional
PlayerSettings core plugin is installed (see [Persistence](#persistence)); otherwise it lasts for the
current session.

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

| Command   | Who    | Description                                                                          |
|-----------|--------|--------------------------------------------------------------------------------------|
| `css_esp` | admins | Toggle ESP. Allowed only while dead or spectating; re-applies automatically on death. |

AllSpec has no command — any admin with its flag spectates everyone automatically.

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

- Each feature is independent: set a section's `"Enabled"` to `false` to turn just that feature off.
- Glow colours accept a hex string (`#RRGGBB`) or a named colour (`Orange`, `SkyBlue`, ...).

---

## Translations

Chat messages are localized via CounterStrikeSharp's `Localizer`. The `lang/` folder ships `en.json`,
`ro.json` and `ru.json`; add a `<code>.json` to support another language. The active language follows
the server/CSSharp language setting. Translations may use `{color}` tags (e.g. `{green}`, `{red}`,
`{default}`). All plugin chat is sent only to the player it concerns.

---

## Persistence

When the PlayerSettings core plugin is installed, each admin's `css_esp` choice is stored per player
and restored on their next connect — surviving reconnects and server restarts. The ESP toggle also
shows up in the PlayerSettings menu.

This is a soft dependency: if PlayerSettings is not installed, AdminUtils still works, but the ESP
choice only lasts for the current session.

---

## Gamedata

AllSpec needs one native signature, shipped in `gamedata/AdminUtils.json`:

| Key                                               | Purpose                                         |
|---------------------------------------------------|-------------------------------------------------|
| `CPlayer_ObserverServices::IsValidObserverTarget` | Lets flagged admins spectate everyone.          |

Windows and Linux signatures are both included. If a CS2 update breaks it, edit the signature in this
file — no recompile needed. CounterStrikeSharp also loads gamedata from
`addons/counterstrikesharp/gamedata/`, so the file may live there instead; keeping it beside the
plugin is recommended. The hook is wrapped in try/catch, so a stale signature leaves AllSpec idle
(logged) rather than crashing the server.

---

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0); all dependencies come
from NuGet.

```bash
dotnet build AdminUtils.csproj -c Release
```

Output: `bin/Release/net10.0/AdminUtils.dll`, plus the `gamedata/` and `lang/` folders it needs.
