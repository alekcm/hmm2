# HoMM5 ToTE 3.1 → Unity 6.6 — port status

What is here, what is verified, and what is not done.

## Scope, stated plainly

Both the shipped data formats **and** the compiled engine binary (`bin/H5_Game_PEST.exe`
and `bin/granny2.dll`) have been reverse-engineered and decompiled so that core
gameplay formulas, RTTI class hierarchies, virtual function tables, Lua C-API
bindings, and Oodle1-compressed Granny GR2 skeletons/animations are extracted
directly from the shipped binaries rather than guessed. See
[`docs/EXE_DECOMPILATION.md`](docs/EXE_DECOMPILATION.md) for the complete binary
reference.

## Delivered

### 1. Executable & DLL Decompiler — `tools/homm5_decompile/`

Static PE32 analyzer and x86 decompiler (`homm5-decompile`) targeting
`bin/H5_Game_PEST.exe` (ImageBase `0x00400000`, MSVC 7.1) and exporting
`UnityProject/Assets/StreamingAssets/homm5_extracted/decompiled_exe_db.json`:

- **3,347** MSVC 7.1 RTTI `TypeDescriptor` classes (`0x00e98370` `type_info` vftable)
- **5,577** resolved virtual function tables across **19,618** unique virtual methods
- **314** unique Lua C-API function registrations (`0x00f2def0 .. 0x00f7bd48`)
- **499** call cross-references to `NWorld::GetDefaultStats()` (`0x00bcb3f0` -> `0x01043a00`)
- **16** decompiled `NWorld` gameplay formulas and tables:
  - 40-level Hero XP progression (`0x00b4c6b0`, seed table at `0x00f755e4` -> `0x01026d90`)
  - Hero primary/secondary stat clamping (`0x00b5ad70`, `0x00b4ca10..0x00b4cde0`)
  - 30-bit packed tile coordinates (`0x00419690`, `0x00bcc310`) & passability (`0x005e11a0`)
  - Terrain step movement cost (`0x00cab090 -> 0x00ca9a70`, jump table `0x00ca9be8`: Road `75`, Off-road `100`, non-native penalty `{0, 25, 50}`, Pathfinding `* 0.5f`, Snatch, positive reduction at `SHeroMoveModifiers+0x14`, diagonal `* 1.41421354f` at `0x00e557b8`)
  - Combat Attack/Defense damage multiplier (`0x00a58d30`, `0x00a58de0`), base stack damage roll (`0x00a597a0`), range/melee/Jousting/Luck modifiers (`0x00a5a900`, `0x00a5c870`), and `NWorld::SFLB0_GetUnitExperienceValue` (`0x004d8ba0`)
  - Active Time Battle (`NWorld::CArenaATB` at `0x00a2d6c0`, `0x00a2d700`, `0x00a5e4f0..0x00a5e688`: unit period `100000.0f / Initiative`, hero period `100000.0f / 10.0f`, Good Morale `0.5f`, Wait `0.5f`, Defend `1.0f` + `+30%` Defense)

### 2. Offline importer — `tools/homm5_import/`

Reads the shipped formats (with automatic Tribes of the East Patch 3.1 `a2p1-data/`
and `a2p1-texts/` overlay):

| format | what it is | status |
| --- | --- | --- |
| `.xdb` (62144 + `a2p1-data`) | XML descriptors, 3 href kinds, Patch 3.1 overlay | **decoded** |
| `DefaultStats.xdb` | Global `NDb::RPGStats` (`NWorld::GetDefaultStats`) | **decoded** |
| `GroundTerrain.bin` (293) | height / flags / river / passability planes | **decoded, 293/293** |
| `bin/Geometries` (3567) | Nival container meshes | **decoded, 3561 exported** (6 are empty) |
| `.dds` (9258) | DXT1/3/4/5, BGRA, RGB565 | **decoded, 9258/9258** |
| `texts/` + `a2p1-texts/` | UTF-16 LE localization with Patch 3.1 overlay | **decoded** |
| `GameMechanics` | creatures, spells, artifacts, buildings, defaultStats | **decoded** |
| `AdvMapDesc` (142) | map objects (`Floor`: `0`=Ground, `1`=Underground) | **decoded, 197936 objects** |
| `bin/Skeletons` (2242) | Granny GR2 v6, Oodle1 (`comp=2`) decompressed | **decoded (`gr2.py`, decompiled from `granny2.dll`)** |
| `bin/animations` (3393) | Granny GR2 v6 (`comp=0` & Oodle1 `comp=2`) | **decoded (`gr2.py`, decompiled from `granny2.dll`)** |
| `bin/effects` (1919) | particle systems | **not decoded** |
| `sound/`, `music/` | FMOD-packed audio | **not touched** |
| `data/scripts/*.lua` | campaign and map scripting | **Lua C-API bindings extracted (314 functions)** |
| `data/types.xml` | 775 type declarations | **present** |

### 3. Unity 6.6 project — `UnityProject/`

`6000.6.6f1`, built-in render pipeline, two assemblies, no dependency on any
glTF package:
- **Adventure Map vertical slice (`HoMM5.Map`, `HoMM5.UI`)**: campaign/mission menu from exported Campaign XDB records, explicit map loading (no first-map heuristic), chunked terrain with cliff walls, water, 198,014 placed objects (with radians-to-degrees `<Rot>` conversion, `BoxCollider` raycast selection, and `GROUND=0` / `UNDERGROUND=1` floor handling), HUD/object inspector, and source-linked XDB material slices/textures.  A `PLAYER_1` hero can be selected and moved with the confirmed `NWorldPathfinder` cost/passability code.
- **Decompiled `NWorld` Gameplay Systems (`HoMM5.World`)**:
  - `NWorldHero.cs` (`NWorld::CHero` `0x00e7e484`: 40-level XP curve, stat clamping, movement points, mana, 7 army slots, artifacts)
  - `NWorldPathfinder.cs` (`NWorld::CHeroMovementCost` `0x00e935b0`: 30-bit packed tile coords, `STileGameInfo::CalcMoveCost` jump table `0x00ca9be8`, passability plane values `1/4`, penalty-reduction modifier, A* pathfinder)
  - `NWorldCombat.cs` (`NWorld::CCombat` `0x00e79d3c`, `NWorld::CCombatCreature` `0x00e4fba4`, `NWorld::CArenaATB` `0x00e6e250`, `SFLB0_GetUnitExperienceValue` `0x004d8ba0`)
  - `NWorldTown.cs` (`NWorld::CTown` `0x00e19674` & `NWorld::CPlayer` `0x00e1a09c`: resource spending/building state only; unsupported hard-coded income and growth tables removed)

## Verified, and how

`cd tools/homm5_import && python3 -m unittest discover -s tests` — **43 tests,
all passing, ~56 s**, every one against the real shipped data. These are
cross-checks between independent sources, not golden files:

- **Container grammar** — every geometry file ends in the 6-byte marker
  `00 00 02 00 05 00`, and a record stream consumes itself exactly.
- **Mesh geometry** — decoded positions fit the bounding box the geometry's own
  `.xdb` declares; `<NumMeshes>` equals both the binary's declared count and the
  count decoded; all triangle indices in range.
- **Normal byte order** — authored normals agree with face normals **99.6%** of
  the time over 53224 triangles. Read as x,y,z instead of z,y,x that falls to
  ~0.27, which is how the order was pinned down.
- **Winding** — after the Z-up→Y-up swap, keeping the winding leaves **0.4%** of
  faces outward and reversing it restores **99.6%**. That measurement is what
  `GlbMeshLoader` implements, and the test guards it.
- **Terrain** — every plane is exactly `(tiles+1)²`, cross-checked against the
  map descriptor's `<TileX>`; height is `V²` f32, river is `(2V−1)²`; the flat
  arena sits at 2.0 (not sea level); `BoatArena` is 100% ground flag 0 at height
  0.0.
- **Map selection** — only `AdvMapDesc` roots are maps: 142 of the 1176 `.xdb`
  under `data/Maps`, the rest being sounds (466), statics (243), textures (150)
  and `AdvMapDescTag` files (62). Every map gets a distinct id.
- **Data integrity** — a creature's localized name equals the file its
  `CreatureVisual` points at; its geometry UID names a real file in
  `bin/Geometries`; `ANGEL_WINGS` comes out `ARTF_CLASS_RELIC` / `SHOULDERS` /
  40000 gold from the reference table.
- **Material links** — `Model.Materials` is exported in its source order for
  creatures, artifacts and map statics; every texture record retains its XDB,
  DDS and generated PNG path, with render flags copied from `NDb::Material`.
  Unity binds ordinal slices only when the source/count relationship is
  unambiguous (or one source material covers all slices), and otherwise falls
  back without assigning by filename or PNG order.
- **PNG output** — signature, IHDR dimensions, and an IDAT that inflates to
  exactly `h × (1 + w × 4)`.

Conversion counts actually produced in this repository:

```
gamedata   196 creatures, 321 spells, 97 artifacts, 328 town buildings, 15947 strings
terrain    293 planes, 0 failed
maps       142 maps, 198014 objects
meshes     3561 .glb, 6 skipped (empty), 320 MB
textures   9268 .png, 0 failed
```

## Not verified

**The C# does not compile here.** This sandbox has no Unity and no .NET SDK
(`dotnet`, `mcs` and `mono` are all absent), so the runtime and editor assemblies
were written but never built. The Python pipeline is tested; the Unity side is
not. Expect to fix compile errors on first open — the API surface used is
deliberately conservative for that reason.

The `.unity` scene is hand-written YAML. If Unity rejects it, **Tools ▸ HoMM5 ▸
Create Bootstrap Scene** rebuilds it; the scene is one GameObject with
`H5Bootstrap`, so nothing is lost.

Nothing was run in the Unity editor, so no visual claim is made about how the
maps look.

## Not done

- **Gameplay is partial.** Confirmed Hero XP/stat clamps, mana/movement base values,
  source-backed campaign mission selection, map hero movement, movement-cost/passability
  primitives, combat formulas and ATB helpers exist.  Campaign bonus choice/application,
  encounter resolution, turn orchestration, spells/abilities, AI, Lua execution,
  networking and a complete town screen are still absent.
- **No animation.** Skeletons are Granny GR2 with per-section Oodle compression;
  reading them needs an Oodle1 decompressor. Creatures sit in bind pose.
- **Materials are partial.** XDB Model/Material/Texture links and core opaque,
  alpha-test, transparent, sidedness, depth and shadow flags now reach Unity.
  `AM_OVERLAY`, effect-specific shaders, lightmaps and fog opt-out still require
  a confirmed render-path reconstruction; they are preserved as source metadata
  rather than assigned heuristically.
- **No terrain texturing.** Vertex colour by ground kind; the shipped maps carry
  no splat weights that survive import.
- **No audio, no UI art, no video.**

## Suggested order from here

1. Get it compiling and open a campaign mission — cheapest way to find what the
   missing compile check left behind.
2. Confirm the PE adventure interaction/encounter handlers before adding monster,
   resource, artifact, and town visit states; do not substitute guessed triggers.
3. Continue only with confirmed combat UI/state and turn orchestration, then bind
   the already reconstructed damage/ATB formulas.
4. Confirm the PE render handlers for `AM_OVERLAY`, effects and `AddPlaced`, and
   GR2 + Oodle1 runtime sampling so creatures animate instead of staying in bind
   pose.  Town income/growth remains data-dependent and is not hard-coded.
