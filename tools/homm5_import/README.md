# HoMM5 ToTE 3.1 → Unity importer

Offline pipeline that turns the unpacked game data in this repository into plain
files a Unity runtime can load. No Unity, no .NET and no third-party Python
packages: the standard library only, so it runs anywhere Python 3.8+ does.

```
tools/homm5_import/
├── homm5import/
│   ├── container.py    Nival record container (the grammar every .bin uses)
│   ├── terrain.py      GroundTerrain.bin / UndergroundTerrain.bin
│   ├── geometry.py     bin/Geometries/<UID> meshes
│   ├── gltf.py         binary glTF 2.0 writer
│   ├── dds.py          DDS decoder + PNG writer
│   ├── xdb.py          .xdb loading, href resolution (absolute, relative, inline)
│   ├── localization.py UTF-16 text tables
│   ├── gamedata.py     creatures, spells, artifacts, town buildings
│   ├── maps.py         AdvMapDesc → map objects
│   ├── gr2.py          RAD Granny GR2 skeletons and animations
│   ├── sounds.py       sound / music catalog and the audio it names
│   ├── ui.py           original UI XDB layouts, button states, DDS art / portraits
│   └── cli.py          entry point
└── tests/test_pipeline.py
```

## Running it

From the **repository root**, one command — no install step, no directory to be
in, works on any platform:

```bash
python import_to_unity.py          # everything
python import_to_unity.py gamedata # one stage
python import_to_unity.py ui       # small original HUD/menu skin export
python import_to_unity.py --help
```

On Windows use `py -3 import_to_unity.py`, or just double-click
`import_to_unity.bat`.

The script puts the package on `sys.path` and resolves `data/`, `texts/` and the
output folder relative to itself, so the working directory does not matter.
Calling the package directly also works, from inside `tools/homm5_import`:

```bash
python -m homm5import all
python -m homm5import.cli info --data <repo>
```

`--data` accepts either the data folder or the repository folder that contains
it. (Testing for a `bin` subfolder to tell them apart does not work: the
repository root has one too, holding the game's own executables. `GameMechanics`
is the discriminator.)

Or stage by stage, which is what you want the first time, because `textures` is
the slow one (~15 min in pure Python for all 9258 files):

```bash
python3 -m homm5import.cli gamedata -o <out>   # 1 s   game tables + localization
python3 -m homm5import.cli terrain  -o <out>   # 14 s  293 terrain planes
python3 -m homm5import.cli maps     -o <out>   # 142 maps, 198001 objects across 21 kinds
python3 -m homm5import.cli meshes   -o <out>   # 58 s  3561 meshes
python3 -m homm5import.cli textures -o <out>   # slow  9258 textures
python3 -m homm5import.cli animations -o <out> # ~2 min  2816 clips, 577 Oodle-packed skipped
python3 -m homm5import.cli sounds   -o <out>   # a few s  2013 effects, 155 music tracks
```

`all` and the stages that take more than a few seconds report progress as they
go (`textures 1200/9258 (13%)`, once every two seconds), so a run that is merely
slow cannot be mistaken for one that has hung. A rerun of a single stage is
safe: files are overwritten in place.

`gamedata` also exports `Game/rules.json`: raw adventure constants and
four human/computer starting-resource tiers from `DefaultStats.xdb`, plus mine
yields read from shipped mine descriptions. `townBuildings.json` carries each
building's type/upgrade and daily gold income when stated in its shipped
localized description. Those are data values; the native engine's formulas that
combine them with hero/army/terrain state are a separate reverse-engineering task.

`--limit N` caps any stage, which is how you smoke-test without waiting.

`info` also counts what already sits under `-o` (textures, meshes, maps, terrain
planes, animations), so `import_to_unity.bat info` is the quickest answer to
"did the import actually write the textures?" without opening a file manager.

## Output

```
HoMM5/
├── Game/creatures.json  spells.json  artifacts.json  townBuildings.json  rules.json  Localization.json
├── Maps/<mapId>.json  _index.json
├── Terrain/<mapId>/GroundTerrain.{json,heights,flags,passability}
├── Meshes/<geometryUid>.glb
├── Animations/<animationUid>.json.gz  _index.json
├── Audio/Sfx/<soundUid>.<wav|ogg>  Music/<path>.ogg
├── Textures/<original path>.dds.png  _manifest.json
└── Game/sounds.json
```

Terrain planes ship as raw little-endian arrays (`.heights` = `V²` float32,
`.flags` and `.passability` = `V²` uint8, `V = tiles + 1`) so the runtime copies
them straight into a `float[]` with no parsing.

Output is regenerable from the data in this repository, so it is gitignored —
see `UnityProject/.gitignore`.

## Formats

### The record container

`bin/Geometries`, `bin/AIGeometries`, `bin/Lights` and every `GroundTerrain.bin`
share one self-describing grammar:

```
record := <tag:u8> <sizefield> <payload>
sizefield, even byte B := B          payload length = B / 2
sizefield, odd  byte B := <u32 LE>   the odd byte IS the u32's low byte;
                                     length = (u32 - 1) / 2
```

A stream ends with the 6-byte marker `00 00 02 00 05 00`. The scalar form
`tag 08 <u32>` is the same rule with size byte 8.

A payload is either further records or a raw leaf, and the container carries no
discriminator. `container._maybe_branch` parses as records when that succeeds,
which is wrong in one specific and important way: a plane of constant bytes
parses cleanly as records. An all-water map's height plane is all zeros, so both
`terrain._plane` and `geometry.decode_group` read the raw payload and validate by
*length* instead of trusting the heuristic.

### Which files are maps

`data/Maps` holds 1176 `.xdb` files, of which only **142 are `AdvMapDesc` maps**.
The rest are sounds (466), static object records (243), textures (150),
`AdvMapDescTag` files (62), heroes, scripts, tents and fog volumes. Treating
every `.xdb` there as a map produces 1034 zero-tile empty maps -- and worse,
`Maps/Multiplayer/S1` and `Maps/SingleMissions/S1` both reduce to the id `S1`,
so one silently overwrites the other on disk. `maps.is_map_desc` filters on the
root tag and `maps.unique_map_ids` prefixes colliding ids with their parent
folder. Terrain has the same hazard plus a second one: arena folders ship
`GroundTerrain.bin` under basenames like `S1` without containing any map
descriptor. `cli._terrain_ids` gives those a path-derived folder name, so 293
planes produce 293 distinct files instead of 262 (the multiplayer maps S1-S3
used to be overwritten by an arena of the same name).

### Terrain planes

| tag | plane | type | size |
| --- | --- | --- | --- |
| `05` | height | f32 | `V²` |
| `07` | ground flags | u8 | `V²` |
| `08` | reserved | u8 | `V²` |
| `0a` | river | u8 | `(2V−1)²` |
| `0f` | passability | u8 | `V²` |

Heights are discrete steps: **2.0 is the default ground level**, 0.0 a dug-out
bed, 1.6 the shore ring. Flags say what *kind* of ground a vertex is — 0 water,
16 ground, 32 plateau, bit 3 ramp — and it is the kind, not the steepness, that
makes an edge a cliff. A smoothed hillside reaches 12 units of relief and must
not be cut; a 0.8 step where water meets land must be.

### Geometry meshes

```
tag02 <u32>       mesh count (== <NumMeshes> in the .xdb)
tag01 x N         one block per mesh; multi-material meshes carry one inner
                  group per material slice
  tag02 { count, f32[3]*n }   positions, unique XYZ
  tag03 { count, 20B*n }      attributes
  tag04 { count, 24B*n }      skin binding
  tag05 { count, u16*n }      render vertex -> position index (the remap)
  tag06 { count, u16*n }      render vertex -> first vertex at that position
  tag07 { count, u16*3n }     triangle list
```

Positions are *unique* XYZ; the engine splits them into render vertices so each
can carry its own normal and UV, addressed through the tag05 remap. The remap is
unambiguous: it is the u16 leaf whose every value is `< positionCount`.

The 20-byte attribute record is UV (2 × s16 ÷ 2048), an unused 4-byte slot, then
normal / tangent / binormal as byte triples stored **z, y, x** (D3DCOLOR order),
decoded `(b − 128) / 127`.

### Coordinate conversion

The game is Z-up and wound counter-clockwise against a right-handed cross
product; Unity is Y-up and culls counter-clockwise faces. The importer writes
standard glTF and leaves both alone. The **Unity** loader applies
`(x, y, z) → (x, z, y)` and reverses triangle winding. Measured over 53224
shipped triangles, the swap alone leaves 99.6% of faces pointing away and the
reversal restores them to 99.6% facing out — see
`GeometryTest.test_winding_needs_reversing_for_unity`.

### XDB references

Three kinds, all handled by `xdb.XDB`:

- **absolute** — `href="/GameMechanics/.../CombatMage.xdb#xpointer(/Creature)"`,
  rooted at the data folder.
- **inline** — `href="#n:inline(Geometry)"`, where the value is the element's own
  child.
- **relative** — `href="T4_Combat_Mage.xdb#xpointer(/Model)"` inside
  `/Characters/.../(Character).xdb`. Resolving these against the data root finds
  nothing at all, silently. `XDB.absolutize` needs the containing file's href as
  the base, which is why `resolve_field` takes one.

### Textures

9258 `.dds` files, all six layouts decodable: BGRA (5752), DXT3 (2651),
DXT1 (843), RGB565 (9), DXT5 (2), DXT4 (1). DXT2/DXT4 share their block layout
with DXT3/DXT5 and differ only in premultiplied alpha, which the game does not
preserve through a PNG round trip anyway.

## Tests

```bash
cd tools/homm5_import && python3 -m unittest discover -s tests
```

144 tests, ~3 min, all against the real shipped data. (Five drive the C# static
checkers and need `pip install tree-sitter tree-sitter-c-sharp`; without it they
fail rather than skip.) They are cross-checks between independent sources rather
than golden files:

- decoded positions fit the bounding box the geometry's own `.xdb` declares
- `<NumMeshes>` equals both the binary's declared count and the count decoded
- every triangle index is in range (0 stray edges)
- authored normals agree with face normals 99.6% of the time (wrong byte order
  drops this to ~27%)
- every terrain plane is exactly `(tiles+1)²`, cross-checked against the map
  descriptor's `<TileX>`
- `BoatArena` is 100% ground flag 0 at height 0.0, as the format notes claim
- a creature's localized name equals the file its `CreatureVisual` points at
- a creature's geometry UID names a file that exists in `bin/Geometries`
- only `AdvMapDesc` roots count as maps (142 of the 1176 `.xdb` under
  `data/Maps`), and every map gets a distinct id
- every `AdvMap` object kind found in the map corpus is represented in the
  importer, and floor IDs match the shipped constants (`GROUND=0`, `UNDERGROUND=1`)

## Not imported

Documented gaps, not silent ones:

- **`bin/Skeletons`** are RAD Granny GR2, not the Nival container, and every
  section in them is Oodle1-packed; that codec is not implemented, so they are
  unread. `bin/animations` use the same format, but 2816 of the 3393 files store
  their sections uncompressed and are exported to `Animations/` (bones, rest pose
  and curves in one pass); the other 577 are Oodle1-packed and counted as
  skipped. Adventure-map models that name an `idle`/`move` clip are rigged and
  animated by the Unity side (`H5Rig`); combat units still use the bind pose.
- **`bin/effects`**, lightmaps, and the `types.xml` schema (2.4 MB, 775 type
  declarations) are present in the data and unused.
- Textures are exported as PNG without mipmaps or the engine's alpha modes;
  `AM_ALPHA_TEST` and `AM_OVERLAY` materials render opaque.

## Native UI stage

`ui` (also included as stage 8 of `all`) exports `UI/skin.json` and 160 PNGs.
It resolves the supplied `a2p1-data` overrides before the base `data`, then
follows shared-window backgrounds and Normal/Pushed/Disabled visual states.
Positions retain the source coordinate/alignment enums for the Unity adapter;
HIGH_END means the far edge, not the top-left of an element. Native-size DDS
padding is clipped rather than squeezed (697×52 resource bar from 1024×64).
Main-menu logo/parchment transparent padding is trimmed. Campaign hero portraits
also have a shared-href lookup, independent of the multiplayer hero pool.
No screenshot textures, image generation, external libraries or full texture
manifest are required for this stage. This is an explicit subset of the UI,
not a general interpreter for every original window / animated menu scene.
