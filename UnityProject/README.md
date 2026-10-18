# HoMM5 ToTE 3.1 — Unity 6.6 project

Target: **Unity 6000.6.6f1** (`ProjectSettings/ProjectVersion.txt`). Built-in
render pipeline, so the project opens without any URP asset to reconstruct.

## Opening it

1. Open this folder in Unity Hub with 6000.6.6f1 installed.
2. Run the importer once — the project ships without generated assets:

   ```bash
   cd ../tools/homm5_import
   python3 -m homm5import.cli all -o ../../UnityProject/Assets/StreamingAssets/HoMM5
   ```

   Or from inside the editor: **Tools ▸ HoMM5 ▸ Run Importer (all)**.
3. Open `Assets/Scenes/AdventureMap.unity` and press Play.

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
| left click on an object | select it and open its source-backed details |
| left click on ground with the player hero selected | calculate a source-backed path and move the hero |
| MAIN MENU | return to the campaign/mission screen |
| F1 | switch surface / underground |

The campaign screen lists the 15 exported campaign missions.  The legacy map list
remains available in the HUD for imports that contain no Campaign records.

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
│   ├── Map/AdventureMapController.cs  camera + selection + ground clicks
│   ├── Map/H5AdventureController.cs  hero selection, pathfinding and movement
│   ├── UI/HoMM5Hud.cs             selection panel, stats, map list
│   ├── UI/HoMM5MainMenu.cs        campaign and mission screen
│   └── H5Bootstrap.cs             entry point
└── HoMM5.Editor/H5EditorMenu.cs   importer runner, scene builder, summary
```

Two assembly definitions. The runtime depends only on
`Unity.Nuget.Newtonsoft-Json`; the glTF reader is in-house, so there is no
dependency on a glTF package's API.

## How data gets to the screen

`H5Bootstrap` loads `Game/*.json` into a `GameDatabase`, shows the exported
campaign missions, and builds only the map the player explicitly selects.
`MapSceneBuilder` loads the terrain planes, hands them to `TerrainMeshBuilder`,
and walks the map's object list, resolving each object's geometry UID through
`MeshCache` → `GlbMeshLoader`.  `H5AdventureController` binds the exported
`PLAYER_1` hero and uses `NWorldPathfinder` for confirmed movement costs; it does
not invent encounter, combat, or town behavior that is not yet reconstructed
from the executable/source.

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

## What this is

A **source-backed vertical slice over the real game data** — the terrain, 198,014
placed objects, creature and artifact tables, campaign missions, and Russian
localization are rendered from what the shipped engine reads.  The player enters
through the campaign screen, selects a mission, selects the exported player
hero, and can move it across reachable terrain using the reconstructed
`NWorld` path-cost code.

This is not yet the complete game.  Encounter resolution, tactical combat,
turn order, AI, town screens, and Lua mission scripting remain separate systems;
those live in `bin/H5_Game_PEST.exe` and `data/scripts/*.lua` and are not filled
with guessed behavior. See `../PORT_STATUS.md` for the full accounting.

## Known limits

- Creatures are static meshes in their bind pose — skeletons and animations
  (`bin/Skeletons`, `bin/animations`) are Granny GR2 and not imported.
- Materials render with the Standard shader. The engine's alpha modes
  (`AM_ALPHA_TEST`, `AM_OVERLAY`) and additive blending are not translated, so
  foliage cards and banners render opaque.
- Terrain is vertex-coloured by ground kind. The shipped maps carry no splat
  weights that survive import.
- Water is a single flat plane at level 1.5, just under the 1.6 shore ring.
- No audio. `sound/`, `soundsfx/` and `music/` are untouched.
