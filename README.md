# AmongUsClone

A pixel-art social deduction game inspired by Among Us, built in Unity. Mobile-first, with plans to port to other platforms after core mechanics are proven out.

## Status: Single-player prototype

Current build includes:
- Top-down movement (keyboard for editor testing, on-screen joystick for mobile)
- Task system: proximity-triggered interactions, progress tracking, victory state
- HUD: live task counter, victory panel
- Impostor/Crewmate role system with a working kill mechanic (solo-testable)
- Boundary walls / basic room layout
- Mobile touch controls (virtual joystick + Interact button)

## Not yet built
- Multiplayer networking (Netcode for GameObjects — in progress)
- Real pixel art (currently using placeholder colored squares)
- Meeting/voting phase
- Multiple rooms / full map
- Sound and animation

## Tech stack
- Unity 6 (6000.6.6f1)
- Universal Render Pipeline (URP) 2D
- Unity Input System (new, not legacy)
- TextMeshPro for UI

## Project structure
- `Assets/Scripts/` — all gameplay code (PlayerController, TaskZone, TaskManager, PlayerRole, Killable, TaskCounterUI)
- `Assets/Sprites/` — placeholder art
- `Assets/Scenes/` — SampleScene (current single-player test scene)

## Setup
1. Clone the repo
2. Open with Unity Hub → Unity 6000.6.6f1 (or closest compatible version)
3. Open `SampleScene`
4. Press Play — WASD/arrows to move, E to interact with tasks, Q to kill (if role is set to Impostor on the Player GameObject)

## Team
Built by EPIPHRAME as a learning project exploring Unity, multiplayer game architecture, and pixel-art game design.
