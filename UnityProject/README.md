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
| left click | select a map object |
| F1 | switch surface / underground |

The map list on the right loads any of the 142 imported maps.

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
backends and picks one from `ENABLE_INPUT_SYSTEM`:

- **Input Manager** (the default) — the project opens and runs as-is.
- **Input System** — set *Project Settings ▸ Player ▸ Active Input Handling* to
  *Both* or *Input System Package (New)*. No code change; the same file switches.

`com.unity.inputsystem` 1.20.0 is in the manifest, the version Unity 6.6 ships.
Until you flip that setting the Input Manager is still active, so Unity keeps
logging that it is deprecated — that message is informational, not a fault.

## What this is

A **map viewer over the real game data** — the terrain, the 197936 placed
objects, the creature and artifact tables, and the Russian localization, all
rendered from what the shipped engine reads. Selecting a monster shows its
shipped stats because they are its shipped stats.

It is not the game. There is no turn order, no movement, no combat resolution,
no AI, no town screen, no campaigns and no Lua. Those live in
`bin/H5_Game_PEST.exe`; `data/scripts/*.lua` holds the campaign and map scripting
and is imported nowhere. See `../PORT_STATUS.md` for the full accounting.

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
