# Maligno — Project Summary

## What it is
Maligno is a pixel-art social deduction game set in **Sitio Maligno**, a Philippine folklore-themed barrio. It plays like Among Us: Villagers complete chores around the sitio while a hidden minority — the **Maligno**, folkloric creatures disguised as neighbors — sabotage and eliminate them from within. Villagers win by finishing every chore or exiling every Maligno through group votes; the Maligno win by killing enough Villagers to reach parity.

What sets it apart from a straight reskin is that each Maligno type is a distinct folklore creature with its own ability, not a single generic "Impostor" kit — closer to a hidden-role game with asymmetric powers than a pure clone.

## Current state
A networked multiplayer prototype — not a demo, not single-player-only. Two or more real clients can host, join, get assigned roles, play a full round (tasks, meetings, kills, abilities, win condition), and reset back to a lobby to play again, all tested live.

## The Maligno roster (all four implemented)
| Type | Ability | Effect |
|---|---|---|
| **Tikbalang** | Liligawin (Bewilder) | Inverts a nearby Villager's controls for 5s |
| **Duwende** | Nuno Mound Trap | Camouflaged trap; slows and blinds whoever triggers it |
| **Aswang** | Anyo (Shapeshift) | Wears another player's look for 10s — including a teammate's, for misdirection |
| **Kapre** | Tabako Smoke | Conjures smoke that shrinks nearby players' vision |
| **Bal-bal** | (passive) | Kills by consuming the body instead of leaving a corpse to report |

Every ability is server-authoritative (a client can't fake a cooldown or a hit), cooldown-gated, and friendly-fire-protected — Maligno can't accidentally kill, bewilder, or trap each other (Aswang's disguise is the deliberate exception, since it's not harmful).

## Systems built this phase
- **Fog-of-war vision** — the map is dark beyond your own line of sight; other players can't be spotted by their own light, and labels (task names, nametags) hide outside your vision radius
- **Full HUD** — task breakdown panel, contextual USE button (kill takes priority over interact when a target's in range, matching Among Us's single-button convention), REPORT, and a shared ABILITY button with a live cooldown overlay
- **Kill + ability cooldowns**, visibly counted down on-screen, not just enforced silently server-side
- **Return to Lobby** — a finished match can be reset and replayed without restarting the game: players revive, tasks and roles clear, and the host can start fresh

## What's next
- Survivor Classes (Shaman, Sprinter) — scoped but not started
- Matchmaking via Unity Gaming Services (Relay + Lobby), removing manual IP/port setup
- Real character art (a teammate's custom sprite is in progress; current art is placeholder)
- Task mini-games with actual mechanics instead of proximity-interact placeholders
- Audio and broader visual polish
- Tightening server-side anti-cheat (movement/vision validation)

## Tech
Unity 6, Netcode for GameObjects (server-authoritative), URP 2D (including Light2D-driven fog-of-war), the new Input System, TextMeshPro.