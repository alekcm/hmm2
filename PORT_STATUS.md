# HoMM5 ToTE 3.1 → Unity 6.6 — port status

What is here, what is verified, and what is not done.

## Scope, stated plainly

A full port of the game is not something one pass produces. The engine is
`bin/H5_Game_PEST.exe` (13 MB of compiled code): turn order, pathfinding, combat
resolution, spells, abilities, hero levelling, town growth, AI, the Lua VM,
network play. None of that is in the data files, and none of it is reimplemented
here.

What **is** here is the other half — the content and the formats. Every number
the game plays with, every mesh it draws, every map it ships, decoded from the
real files and loading in a Unity 6.6 project. That is the foundation a port is
built on, and it is the part that cannot be shortcut.

## Delivered

### 1. Offline importer — `tools/homm5_import/`

Standard library only, no dependencies. Reads the shipped formats:

| format | what it is | status |
| --- | --- | --- |
| `.xdb` (62144) | XML descriptors, 3 href kinds | **decoded** |
| `GroundTerrain.bin` (293) | height / flags / river / passability planes | **decoded, 293/293** |
| `bin/Geometries` (3567) | Nival container meshes | **decoded, 3561 exported** (6 are empty) |
| `.dds` (9258) | DXT1/3/4/5, BGRA, RGB565 | **decoded, 9258/9258** |
| `texts/` (15860) | UTF-16 LE localization | **decoded** |
| `GameMechanics` | creatures, spells, artifacts, buildings | **decoded** |
| `AdvMapDesc` (142) | map objects | **decoded, 197936 objects** |
| `bin/Skeletons` (2242) | Granny GR2, Oodle-compressed | **not decoded** |
| `bin/animations` (3393) | Granny GR2, Oodle-compressed | **not decoded** |
| `bin/effects` (1919) | particle systems | **not decoded** |
| `sound/`, `music/` | FMOD-packed audio | **not touched** |
| `data/scripts/*.lua` | campaign and map scripting | **not touched** |
| `data/types.xml` | 775 type declarations | **present, unused** |

### 2. Unity 6.6 project — `UnityProject/`

`6000.6.6f1`, built-in render pipeline, two assemblies, no dependency on any
glTF package. Adventure-map viewer: chunked terrain with cliff walls, water,
197936 placed objects, click-to-select with the shipped stats, a map list for all
142 maps, surface/underground switch.

## Verified, and how

`cd tools/homm5_import && python3 -m unittest discover -s tests` — **34 tests,
all passing, ~46 s**, every one against the real shipped data. These are
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
- **PNG output** — signature, IHDR dimensions, and an IDAT that inflates to
  exactly `h × (1 + w × 4)`.

Conversion counts actually produced in this repository:

```
gamedata   196 creatures, 321 spells, 97 artifacts, 328 town buildings, 15860 strings
terrain    293 planes, 0 failed
maps       142 maps, 197936 objects
meshes     3561 .glb, 6 skipped (empty), 320 MB
textures   9258 .png, 0 failed
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

- **No game logic.** No turn order, movement, combat, spells, abilities,
  hero levelling, town screen, campaigns, AI, Lua or networking.
- **No animation.** Skeletons are Granny GR2 with per-section Oodle compression;
  reading them needs an Oodle1 decompressor. Creatures sit in bind pose.
- **No materials.** Everything renders with Standard. The engine's alpha modes,
  additive blending, lightmaps and effects are not translated — foliage cards and
  banners will render opaque.
- **No terrain texturing.** Vertex colour by ground kind; the shipped maps carry
  no splat weights that survive import.
- **No audio, no UI art, no video.**

## Suggested order from here

1. Get it compiling and open a map — cheapest way to find what the missing
   compile check left behind.
2. Materials: map `AlphaMode` / `Is2Sided` / `AddPlaced` onto shader keywords and
   bind the imported textures.
3. GR2 + Oodle1, so creatures animate.
4. Combat: the data is already here — `creatures.json` carries attack, defence,
   damage, initiative, speed and abilities, and `spells.json` the spell table.
