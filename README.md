# Maligno

A pixel-art, Among Us–style social deduction game set in **Sitio Maligno**, a Philippine folklore-inspired barrio. Villagers finish chores around the sitio while hidden **Maligno** — folkloric creatures disguised as neighbors — mislead and eliminate them from within.

Built in Unity 6 with Netcode for GameObjects. Players connect over the internet with a short room code (Unity Relay), so friends don't need to be on the same network. Desktop-first; mobile input groundwork (virtual joystick) is in place.

## Status: Online multiplayer, playable end to end

Host a room, share the code, play a full match (tasks, meetings, kills, abilities, win conditions), and return to the lobby to play again.

### Implemented

**Online play**
- Host / join with a room code through Unity Gaming Services (Relay + Sessions), with sign-in handled automatically
- Lobby with room code, player counter and a host-only settings window
- **Capacity** setting (4–15): the room code stops accepting players once the lobby is full
- **Disconnect handling**: a player who leaves is removed cleanly (their tasks, votes and role slot); win conditions are re-checked
- **Host migration**: if the host drops, the group is moved into a fresh lobby under a new host
- Return to Lobby after a match without restarting the app

**Roles**
- Server-authoritative role assignment with private reveal (each client only learns its own role)
- **Maligno types**: Tikbalang (Bewilder), Duwende (Curse Trap), Aswang (Shapeshift), Kapre (Smoke Screen), Bal-Bal (consume a victim to leave no body; the ability's cooldown is twice the kill cooldown)
- **Villager classes**: Third Eye (see footprints and reveal hidden Duwende mounds), Reiki Practitioner (aura sense), Albularyo (cure curses), Babaylan (condemn one player in a meeting, once per match), Tarot User (random buff or curse)
- Role chances are **independent probabilities** per slot with a per-role cap (a 5% role appears in about 1 match in 20, 100% is guaranteed up to its cap); a Maligno that rolls no type is a plain Maligno
- Your own role or class is shown under your nametag during a match; Maligno get no chores and see no task list
- Dead players become ghosts: they can finish chores, cannot use abilities, stay inside the walls, and are invisible to the living

**Tasks**
- **Per-player tasks**: each Villager gets their own random list (common tasks are shared, long and short ones are random per player); the shared progress bar counts every Villager's tasks
- Mini-games for tasks, per-player markers and prompts, a personal task list on the HUD
- Server validates every completion against that player's assigned list

**Meetings & voting**
- Emergency meetings (button at the cafeteria table) and body reports; Among Us–style round table
- Discussion phase, then voting; open or anonymous votes; confirm-ejects on or off
- Emergency meeting count and cooldown are configurable

**Game Settings window** (host edits, everyone can view)
- Presets: Classic, Quick Match, Hardcore, Social
- Lobby: capacity
- Maligno: count, kill cooldown, Maligno vision, kill distance
- Villagers: player speed, Villager vision
- Meetings: emergency meetings and cooldown, discussion time, voting time, anonymous votes, confirm ejects
- Tasks: common, long and short task counts, task bar updates (always / meetings only / never)
- Roles tab: per-role count and % chance for every Maligno type and Villager class

**Other systems**
- Much darker map with fog-of-war vision (per-player lights); other players and their nametags are only drawn inside your vision radius, fading in and out as they enter and leave it
- **Duwende mounds** are invisible to everyone except Maligno. Villagers get a faint pulsing sound that grows louder as they get close, and Third Eye reveals the real mound for a short time
- **Curse**: stepping on a mound curses a Villager with a red glow, slower movement and reduced vision. It does not wear off: the player dies after 20 seconds unless an Albularyo cures them
- HUD: task bar and list, contextual USE button, REPORT, ABILITY with cooldown overlay, kill cooldown badge
- Win/loss conditions, all server-checked: tasks complete, all Maligno ejected, or Maligno reach parity
- Sound effects and a game-over screen with the full role reveal

### Not yet built
- **Visual tasks** setting (task animations visible to others)
- Sabotage, vents, and a larger map with more task spots (the current pool is 14 task locations)
- Player customization (colors, hats) — colors are currently fixed per player slot
- Saving settings between sessions
- Quit-to-menu from inside a match
- Final character art (a placeholder sprite set is in use)
- Music and further visual polish
- Anti-cheat hardening (movement and vision are owner-authoritative, not server-validated)
- Broad multi-player and over-the-internet testing: most behavior has been verified in the editor with a few virtual players

## Tech stack
- Unity 6 (6000.6.6f1)
- Universal Render Pipeline (URP) 2D, including Light2D-based fog-of-war
- Netcode for GameObjects (NGO) — server-authoritative multiplayer
- Unity Gaming Services: Authentication, Relay, Multiplayer Sessions
- Unity Input System (new, not legacy)
- TextMeshPro for UI and world-space labels

## Project structure
- `Assets/Scripts/Core/` — match flow and settings (`GameStartManager`), meetings, tasks, win conditions, connection and disconnect handling
- `Assets/Scripts/Player/` — movement, roles, vision, nametags and the role tag
- `Assets/Scripts/Abilities/` — Maligno abilities and Villager class abilities
- `Assets/Scripts/UI/` — HUD, meeting screen, lobby and the Game Settings window
- `Assets/Scripts/Editor/` — editor build and preview tools (see the **Maligno** menu)
- `Assets/Scenes/` — `MainMenu`, `Lobby`, `Game`

## Setup
1. Clone the repo
2. Open with Unity Hub → Unity 6000.6.6f1 (or closest compatible version)
3. Link the project to a Unity cloud project (needed for Relay and Sessions)
4. Confirm the project's `UniversalRP` asset is assigned under Project Settings → Graphics and Quality
5. Open the `MainMenu` scene and press Play → **PLAY** → **HOST GAME**
6. To test with others, use Multiplayer Play Mode virtual players, or build the game (**Maligno → 20. Build Windows Player**) and send the whole build folder to friends; they enter your room code under **JOIN GAME**
7. Host clicks **Start** once enough players have joined (4+ recommended) — roles and tasks are assigned automatically

## Team
Built by Jem and collaborators as a learning project exploring Unity, server-authoritative multiplayer architecture, and Philippine-folklore-inspired game design.
