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
│   └── cli.py          entry point
└── tests/test_pipeline.py
```

## Running it

From the repository root, with the game data already unpacked into `data/` and
`texts/` (as it is in this repository):

```bash
cd tools/homm5_import
python3 -m homm5import.cli info                     # what the data root holds
python3 -m homm5import.cli all -o ../../UnityProject/Assets/StreamingAssets/HoMM5
```

Or stage by stage, which is what you want the first time, because `textures` is
the slow one (~15 min in pure Python for all 9258 files):

```bash
python3 -m homm5import.cli gamedata -o <out>   # 1 s   game tables + localization
python3 -m homm5import.cli terrain  -o <out>   # 14 s  293 terrain planes
python3 -m homm5import.cli maps     -o <out>   # 17 s  1176 maps, 197903 objects
python3 -m homm5import.cli meshes   -o <out>   # 58 s  3561 meshes
python3 -m homm5import.cli textures -o <out>   # slow  9258 textures
```

`--limit N` caps any stage, which is how you smoke-test without waiting.

## Output

```
HoMM5/
├── Game/creatures.json  spells.json  artifacts.json  townBuildings.json  Localization.json
├── Maps/<mapId>.json  _index.json
├── Terrain/<mapId>/GroundTerrain.{json,heights,flags,passability}
├── Meshes/<geometryUid>.glb
└── Textures/<original path>.dds.png  _manifest.json
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

31 tests, ~40 s, all against the real shipped data. They are cross-checks
between independent sources rather than golden files:

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

## Not imported

Documented gaps, not silent ones:

- **`bin/Skeletons` and `bin/animations`** are RAD Granny GR2, not the Nival
  container. They carry their own type tree and are Oodle-compressed by section,
  so they need an Oodle1 decompressor. Nothing here reads them, so imported
  creatures are static meshes in their bind pose.
- **`bin/effects`**, lightmaps, and the `types.xml` schema (2.4 MB, 775 type
  declarations) are present in the data and unused.
- Textures are exported as PNG without mipmaps or the engine's alpha modes;
  `AM_ALPHA_TEST` and `AM_OVERLAY` materials render opaque.
