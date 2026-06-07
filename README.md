# There Can Only Be One — Godot Edition

A local-multiplayer (1–16 player) split-screen arena brawler, ported from Unity to **Godot 4.6.3 (.NET / C#)**.

Players fight in a shared arena; knock opponents down and **drain their aura** — the first to reach the combined aura pool **wins the round**. There can only be one.

---

## Requirements

- **Godot 4.6.3 — Mono / .NET build** (C# support required)
- **.NET SDK 8** (for `dotnet build`)

## Running

1. Open the project in the Godot 4.6.3 (.NET) editor.
2. Press **F5** (or run `Game.tscn`, the main scene).
3. Or from the CLI: `dotnet build` then run the project with the Godot executable.

Flow: **Splash → Main Menu → Lobby → Character Select → Battle → Post-Game**.

## Controls

Gamepad per player (keyboard mirrors **device 0** for solo testing):

| Action | Gamepad | Keyboard (P1) |
|---|---|---|
| Join / Confirm | A | Space / Enter |
| Leave / Back | B | Backspace / Esc |
| Start / Advance | Start | Enter |
| Move | Left stick | — |
| Camera | Right stick | — |
| Light / Heavy / Special | X / Y / B | — |
| Jump (launcher is combo-only) | A | — |
| Lock-on (orbit/aim) | R1 | — |
| Team invite (on a locked target) | R3 | — |
| Player menu (combo list) | Start (in battle) | — |
| Browse character | D-Pad / Arrows | Arrows |

**Win the round:** knock an opponent prone, stand in their aura field, and hold **B** to drain their aura into yours. First to the combined-max aura wins.

## Features

- **1–16 players**, automatic split-screen (SubViewport grid sharing one 3D world)
- Per-player **orbit / follow / lock-on / side-view** camera
- **Combo-driven attack system** (light/heavy/special, launchers, juggles) with active-overlap hit detection
- **Aura drain win loop**, health/stamina/aura stats
- **Teaming** — invite/join/kick/mutiny, leader/follower roles (crown / figure)
- Full game-state flow with **Main Menu** and **Post-Game** results screen
- Per-viewport **HUD** — aura ring with player symbol, health/stamina bars, animated aura flames, floating name tags, team roster
- 16 color-coded characters with unique identity symbols

## Debug tooling

- **Controllers pane** (top-right, collapsible): spawn virtual controllers to test multiplayer solo — add/remove, full-lobby-flow, patrol bots. Hotkeys **F1** add / **F2** remove / **F3** patrol / **F4** jump.
- **Force-Win pane** (top-left, during Battle): pick a player # and force the win → Post-Game.

> A separate Godot editor MCP plugin (`addons/godot_dotnet_mcp`) was used during development and is **git-ignored** — install it yourself if you want editor automation.

## Project layout

```
Scripts/
  Core/         shared types (events, stats, physics layers, utils)
  Player/       LocalPlayerManager (per-player hub), PlayerInput, detection, symbols
  Stats/        Stat + StatController
  Movement/     MovementController
  Camera/       CameraControler + state wrapper
  Attack/       attack data, combos, AttackController
  Teaming/      Team, TeamRules, TeamController
  States/       per-player state machine + states
  UserInterface/ HUD, dialogs, menus, debug panes
  GameStates/   GameManager + Splash/Menu/Lobby/CharacterSelect/PreGame/Battle/PostGame + split-screen rig
  Testing/      virtual-controller debug tooling
Game.tscn       main scene
Player.tscn     character (CharacterBody3D)
```

See **[PORT_NOTES.md](PORT_NOTES.md)** for the Unity → Godot port details and engine-specific adaptations.
