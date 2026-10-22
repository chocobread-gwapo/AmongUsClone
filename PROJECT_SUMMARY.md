# Maligno — Project Summary

## What it is
Maligno is a pixel-art social deduction game set in **Sitio Maligno**, a Philippine folklore-themed barrio. It plays like Among Us: Villagers complete chores around the sitio while a hidden minority — the **Maligno**, folkloric creatures disguised as neighbors — eliminate them from within. Villagers win by finishing every chore or exiling every Maligno through group votes; the Maligno win by killing enough Villagers to reach parity.

What sets it apart from a straight reskin is that roles are folklore-driven: each Maligno type has its own ability, and Villagers can roll a special class, so it plays closer to a hidden-role game with asymmetric powers than a pure clone.

## Current state
An online multiplayer game that is playable from lobby to win screen. A host creates a room and shares a short code; friends join from anywhere through Unity Relay, no shared network needed. A full round (tasks, meetings, kills, abilities, win condition) and a return to the lobby all work. Most behavior has been tested in the editor with a few virtual players; broad testing across real internet connections is still to do.

## Roles
**Maligno**
| Type | Ability | Effect |
|---|---|---|
| **Tikbalang** | Bewilder | Inverts a nearby Villager's controls briefly |
| **Duwende** | Curse Trap | Hidden mound; slows and half-blinds whoever steps on it |
| **Aswang** | Shapeshift | Copies a nearby player's look and name for a short time |
| **Kapre** | Smoke Screen | Smoke that shrinks nearby Villagers' vision |
| **Bal-Bal** | (passive) | Kills leave no body to report |

**Villager classes**
| Class | Ability |
|---|---|
| **Third Eye** | Sees recent Maligno footprints and hidden traps nearby |
| **Reiki Practitioner** | Senses roughly where Maligno are |
| **Albularyo** | Cures a cursed Villager |
| **Babaylan** | Condemns one player in a meeting, once per match; a wrong guess means exile |
| **Tarot User** | Draws a card for a random buff or curse |

Each role's chance is an independent percentage with a cap, set by the host. A Maligno with no type is a plain Maligno. Abilities are server-authoritative, cooldown-gated and friendly-fire protected. Dead players are ghosts: they can still do chores but cannot use abilities.

## Systems built
- **Online play** — Relay room codes, host migration if the host drops, disconnect handling, a Capacity setting (4–15) that locks the room when full
- **Per-player tasks** — each Villager gets their own random common, long and short chores with mini-games; one shared progress bar counts everyone's work
- **Meetings** — Among Us–style emergency table and body reports, optional discussion phase, open or anonymous votes, confirm-ejects option
- **Game Settings window** — presets (Classic, Quick Match, Hardcore, Social), Maligno and Villager tuning, meeting rules, task counts, task bar mode, and per-role count and chance
- **Fog-of-war vision** with per-player lights; labels hide outside your sight
- **HUD** — task list and bar, contextual USE, REPORT, ABILITY with cooldown overlay, and your own role shown under your nametag
- **Win conditions** and a game-over screen that reveals every role; **Return to Lobby** to replay

## What's next
- Test over real internet connections with friends, and a larger multi-player pass on kills, meetings and abilities
- Sabotage, vents and a bigger map with more task spots
- Visual tasks, player customization and saving settings between sessions
- Final character art, music and visual polish
- Quit-to-menu during a match
- Tighter server-side anti-cheat (movement and vision validation)

## Tech
Unity 6, Netcode for GameObjects (server-authoritative), Unity Gaming Services (Authentication, Relay, Multiplayer Sessions), URP 2D (Light2D fog-of-war), the new Input System, TextMeshPro.
