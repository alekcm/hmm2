# HoMM5 ToTE 3.1 — Unity 6.6 project

Target: **Unity 6000.6.6f1** (`ProjectSettings/ProjectVersion.txt`). Built-in
render pipeline, so the project opens without any URP asset to reconstruct.

## Opening it

1. Open this folder in Unity Hub with 6000.6.6f1 installed.
2. Run the importer once — the project ships without generated assets (~1.3 GB,
   gitignored, reproducible from the game data):

   ```bash
   python import_to_unity.py        # from the repository root
   ```

   On Windows: `py -3 import_to_unity.py`, or double-click `import_to_unity.bat`.

   Or from inside the editor: **Tools ▸ HoMM5 ▸ Run Importer (all)** — it looks
   for `py`, `python3` then `python`, since `python3` does not exist on Windows.

   Until this has run, Play mode logs "No import found" together with the exact
   command for your machine. The scene itself is fine: it is one GameObject with
   `H5Bootstrap`, which builds the camera, lights and HUD on Start.
3. Open **`Assets/Scenes/MainMenu.unity`** and press Play. This is the first
   enabled build scene. Starting a new game / Continue transitions to
   `AdventureMap`; Escape opens the in-game pause menu without destroying the map.

If you already imported the project before the native-UI update, only this
small additional stage is needed (no full textures reimport):

```bash
python import_to_unity.py ui
```

Or **Tools ▸ HoMM5 ▸ Run Importer (original UI)**. It exports 160 original
DDS-derived images and 24 XDB layouts, including ToTE placement overrides,
button states, resource icons, town icons, and campaign/multiplayer portraits.
Generated PNGs remain gitignored. With no UI import the fallback HUD remains
available, and the menu shows the command above.

Play normally starts `MainMenu` even when a map-inspection scene is open.
**Tools ▸ HoMM5 ▸ Play From Current Scene (debug)** opts out; direct map startup
also needs `startDirectlyOnMap` explicitly enabled on `H5Bootstrap`.
Use Game view **Scale = Fit**, not a zoomed 1.3× view, to inspect the entire HUD;
Unity's Game-view zoom can crop off the top/side UI.

If Unity refuses the scene file, **Tools ▸ HoMM5 ▸ Create Bootstrap Scene**
rebuilds it from scratch — the scene is one GameObject with `H5Bootstrap`, so
there is nothing to lose.

**Tools ▸ HoMM5 ▸ Import Summary** reports what the import produced.

## Controls

| input | action |
| --- | --- |
| WASD / arrows | pan |
| pointer at screen edge | edge scroll |
| middle mouse drag | pan directly |
| wheel | zoom |
| left click | select a map object (or click the minimap to pan the camera) |
| right click | issue a pathfinding move to a selected hero |
| F1 | switch surface / underground |
| F5 / Save | quick save |
| F9 / Load | quick load |
| End Day | advance the prototype calendar and restore hero movement |

In battle: left click a highlighted cell to move, click an enemy stack to
attack (a walk to the nearest attack cell resolves with the blow); click a
wall/gate/tower to strike it during a siege. Wait, Defend, Cast Spell, Quick
Combat and Flee buttons along the bottom; the queue at the top shows who acts
next. Before the first round a tactics phase lets you deploy your stacks in
the front columns.

The main menu offers imported scenarios; the debug map list is no longer
shown on the adventure HUD. The importer now
reads the shipped base hero movement (`2500` MP) and movement bonuses from
`GameMechanics/RPGStats/DefaultStats.xdb`. The runtime currently approximates a
flat tile as 100 MP; terrain, army, road, artifact and skill cost calculations
are still being reconstructed from the native engine, so this conversion is
explicitly provisional rather than a claimed original rule.

## Layout

```
Assets/Scripts/
├── HoMM5.Runtime/
│   ├── Core/H5Paths.cs            StreamingAssets layout, file access
│   ├── Data/GameDefs.cs           DTOs matching the importer's JSON
│   ├── Data/GameDatabase.cs       creatures / spells / artifacts / localization
│   ├── Map/H5Terrain.cs           terrain planes, ground kinds, cut detection
│   ├── Map/TerrainMeshBuilder.cs  terrain → chunked meshes with cliff walls
│   ├── Map/GlbMeshLoader.cs       binary glTF reader + coordinate fix-up
│   ├── Map/MeshCache.cs           meshes by geometry UID
│   ├── Map/MapSceneBuilder.cs     map JSON → scene
│   ├── Map/H5MapObject.cs         game identity of a placed object
│   ├── Map/AdventureMapController.cs  camera + selection
│   ├── UI/HoMM5Hud.cs             selection panel, stats, map list
│   └── H5Bootstrap.cs             entry point
└── HoMM5.Editor/H5EditorMenu.cs   importer runner, scene builder, summary
```

Two assembly definitions. The runtime depends only on
`Unity.Nuget.Newtonsoft-Json`; the glTF reader is in-house, so there is no
dependency on a glTF package's API.

## How data gets to the screen

`H5Bootstrap` loads `Game/*.json` into a `GameDatabase`, then builds the first
map. `MapSceneBuilder` loads the terrain planes, hands them to
`TerrainMeshBuilder`, and walks the map's object list, resolving each object's
geometry UID through `MeshCache` → `GlbMeshLoader`.

Three things are worth knowing about that path:

**Terrain is steps, not a heightfield.** `H5Terrain.IsCutCell` decides a cell is
a cliff when its four corners straddle a *ground kind* boundary (water / ground /
plateau) and the step is ≥ 0.8. Steepness alone is not the test: a smoothed
hillside reaches 12 units of relief and interpolating it is correct, while
cutting it turns the map into a staircase. Ramps are never cut.

**Coordinates.** The importer writes standard glTF. `GlbMeshLoader` applies
`(x, y, z) → (x, z, y)` and reverses triangle winding on the way in. Both halves
are needed; the swap alone reverses orientation and leaves every face culled.

**Meshes are shared, not instanced.** A 200-object arena reuses a couple of dozen
models, so `MeshCache` hands out the same `Mesh` to every object and
`ObjectScale` is a single constant rather than a per-object transform.

## Input

All device access goes through `Core/H5Input.cs`, which compiles against both
backends and picks one at compile time:

- **Input Manager** (the default) — the project opens and runs as-is.
- **Input System** — set *Project Settings ▸ Player ▸ Active Input Handling* to
  *Both* or *Input System Package (New)*. No code change; the same file switches.

`com.unity.inputsystem` 1.20.0 is in the manifest, the version Unity 6.6 ships.
Until you flip that setting the Input Manager is still active, so Unity keeps
logging that it is deprecated — that message is informational, not a fault.

The guards test **two** symbols, because either one alone is wrong:

| symbol | set by | guarantees |
| --- | --- | --- |
| `H5_INPUT_SYSTEM` | `versionDefines` in `HoMM5.Runtime.asmdef` | the package is present, so the `Unity.InputSystem` reference resolves and the types compile |
| `ENABLE_INPUT_SYSTEM` | Unity, from Active Input Handling | devices actually report, so `Keyboard.current` is not null |

Unity sets `ENABLE_INPUT_SYSTEM` project-wide, with no regard for whether a
given assembly can see the backend. Guarding on it alone produced `CS0234`: the
branch compiled and every `UnityEngine.InputSystem` type in it failed to
resolve. The first symbol without the second would compile and then find every
device null.

The same pair picks the EventSystem's input module. `StandaloneInputModule`
reads the old manager; under the Input System it logs an error and swallows
every click, so the map list would render and never respond.

## What this is

A **map viewer over the real game data** — the terrain, all 198001 placed map
objects across 21 kinds, the creature and artifact tables, and the Russian localization, all
rendered from what the shipped engine reads. Selecting a monster shows its
shipped stats because they are its shipped stats.

It is not the game. There is a prototype day/week/month counter and a movement
budget seeded from the game's `2500` MP base value for heroes owned by the
configured local player (default `PLAYER_1`), using basic A* over imported
passability. Per-tile cost and the original turn scheduler remain incomplete.
Object visits run from the shipped shared records: treasure pickups, artifact
grants, mine capture, stat-boosting buildings, shrines, magic wells and the
weekly Windmill/Water Wheel all fire when a hero steps on an object's active
tile, with modal choices (chests, the Arena) in the HUD. Walking onto a
monster starts a turn-based battle on real combat-arena scenery -- initiative
rounds, Wait/Defend, luck and morale wired to the shipped `DefaultStats`
constants, creature abilities, hero spellcasting, war machines (ballista,
catapult, first aid tent), sieges with walls, gate, towers and a moat, a
tactics phase, a greedy opponent, Quick Combat, and army/experience
consequences that persist to saves. Enemy towns, garrisons, guarded dwellings
and the classic banks (Crypt, Dragon Utopia, ...) are battle sites too.
Quick save/load (F5/F9) persists the world state, and
the minimap shows terrain, objects and the camera viewport. Town screen,
campaign flow, secondary skills and Lua execution are still
absent. The remaining systems live in
`bin/H5_Game_PEST.exe`; `data/scripts/*.lua` holds the campaign and map scripting
and is imported nowhere. See `../PORT_STATUS.md` for the full accounting.

## Known limits

- Adventure-map idle and move clips play (extracted from Granny GR2); other
  animation sets are not imported, and clips that start far from the bind pose
  deform incorrectly (see `H5Rig`).
- Battle rules are a reconstruction: damage coefficients, proc chances and
  ability probabilities are inferred (marked in `CombatRules`/`CombatAbilities`),
  the 14x10 grid and initiative rounds approximate the engine's ATB model.
  Siege wall layouts and bank guard rosters are prototype tables. Secondary
  skills (magic masteries, Tactics, War Machines tiers) are not implemented.
- Materials render with the Standard shader. The engine's additive blending,
  lightmaps and particle effects are not translated.
- Terrain is chosen per cell from the dominant painted layer; smooth weight
  blending is not implemented.
- Water is a single flat plane at level 1.5, just under the 1.6 shore ring.
- Treasure payout constants that the shipped tables do not carry are inferred
  and marked in `H5ObjectVisits` (gold roll scaling, chest XP).
- No audio. `sound/`, `soundsfx/` and `music/` are untouched.

## Visual fidelity limits

The native chrome is now consumed by Unity, not just exported: resource bar at
centre-top, minimap at lower-left, command dial at lower-right, and hero/town
roster beside the minimap. Menu uses the supplied ToTE logo and parchment.
The animated 3D main-menu backdrop, original fonts, town/hero modal layouts,
and full original effects/lighting are **not** reproduced. The Move Hero dial
button is disabled until queued routes exist; right-click movement still works.
The book opens the existing hero panel, not a completed adventure-spell system.

Terrain now uses continuous source heights and global seam-consistent normals,
with per-vertex coverage textures blended in a Built-in surface shader. GPUs
without 2D texture arrays retain the older dominant-layer fallback. Editor-only
random-object cube models have not been hidden: replacing them requires resolving
random towns/treasures/monsters to gameplay-consistent assets first.
This source update has not been compiled/rendered in Unity in the sandbox.
