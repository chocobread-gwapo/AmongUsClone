# Maligno

A pixel-art, Among Us–style social deduction game set in **Sitio Maligno**, a Philippine folklore-inspired barrio. Crewmate villagers ("Villagers") finish chores around the sitio while hidden **Maligno** — folkloric creatures disguised as neighbors — sabotage, mislead, and eliminate them from within.

Built in Unity 6 with Netcode for GameObjects. Desktop-first for now; mobile input groundwork (virtual joystick) is already in place.

## Status: Networked multiplayer prototype

Core gameplay loop, four of the game's Maligno abilities, a full HUD, fog-of-war vision, and a return-to-lobby flow are all implemented and tested across multiple networked clients.

### Implemented
- **Movement & interaction**: server-aware 2D top-down movement, proximity-based task interaction, kill range, report range
- **Task system**: multi-room task tracking, synced progress bar, grouped/colored task breakdown panel (`TaskListUI`)
- **Roles**: server-authoritative role assignment with per-Impostor Maligno type, private role reveal via targeted ClientRpc (no leaks to other clients)
- **Meetings & voting**: emergency meeting + report-body triggers, full vote tally and resolution, ejection
- **Maligno abilities** (all four implemented, server-authoritative, with friendly-fire guards):
  - **Tikbalang — Liligawin (Bewilder)**: inverts a nearby Crewmate's movement for 5s, 25s cooldown
  - **Duwende — Nuno Mound Trap**: camouflaged trap (max 3 active), slows + shrinks the victim's vision on trigger
  - **Aswang — Anyo (Shapeshift)**: assumes a nearby player's look for 10s (deliberately usable on a fellow Maligno for misdirection)
  - **Kapre — Tabako Smoke**: conjures a smokescreen that shrinks nearby players' vision radius
  - **Bal-bal**: alternate "consume the body" kill path, shares the kill cooldown clock with the normal kill
- **Kill cooldown system**: configurable (default 15s) via `GameStartManager`, shared across both kill paths
- **Ability cooldown UI**: on-screen countdown badge for kills, and a dimming overlay + countdown directly on the ABILITY button for Tikbalang/Aswang/Kapre
- **Fog-of-war vision**: per-player URP 2D light punches a visible radius out of a darkened map; only your own light is ever shown to you, so other players' positions aren't given away. World-space labels (task names, player nametags) hide themselves outside your vision radius too
- **HUD**: task breakdown panel, contextual USE button (kill-priority-then-interact, Among Us–style), REPORT button, ABILITY button, colored player nametags
- **Win/loss conditions**: tasks complete, all Maligno ejected, or Maligno parity — all server-checked
- **Return to Lobby**: host can reset a finished match — players revive, tasks reset, roles clear — and start a fresh game without restarting the app

### Not yet built
- Survivor Classes (Shaman, Sprinter — scoped in the task list, not started)
- UGS Relay/Lobby for matchmaking without manual IP/port setup
- Real character art (currently a placeholder sprite; a teammate's custom art is in progress)
- Task mini-games (currently simple proximity-interact placeholders)
- Sound, music, and most visual polish
- Anti-cheat hardening (movement/vision confirmation is currently owner-authoritative, not server-raycast-validated)

## Tech stack
- Unity 6 (6000.6.6f1)
- Universal Render Pipeline (URP) 2D, including Light2D-based fog-of-war
- Unity Netcode for GameObjects (NGO) — server-authoritative multiplayer
- Unity Input System (new, not legacy)
- TextMeshPro for UI and world-space labels

## Project structure
- `Assets/Scripts/` — all gameplay code: player movement/input, roles, abilities, task system, meetings, HUD bridges, vision
- `Assets/Prefabs/` — Player, trap, smoke cloud, and other networked prefabs
- `Assets/Sprites/` — current placeholder and in-progress character art
- `Assets/Scenes/` — `SampleScene`, the current multiplayer test scene

## Setup
1. Clone the repo
2. Open with Unity Hub → Unity 6000.6.6f1 (or closest compatible version)
3. Confirm the project's `UniversalRP` asset is assigned under Project Settings → Graphics and Quality (required for the fog-of-war lighting to render)
4. Open `SampleScene`
5. Press Play as Host to test solo, or build + run a second instance (or ParrelSync) to test real multiplayer
6. Host clicks **Start Game** once 2+ players have joined — roles are shuffled and assigned automatically

## Team
Built by Jem and collaborators as a learning project exploring Unity, server-authoritative multiplayer architecture, and Philippine-folklore-inspired game design.