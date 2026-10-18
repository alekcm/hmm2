# `bin/H5_Game_PEST.exe` & `bin/granny2.dll` Decompilation Reference

This document records the reverse-engineered architecture, MSVC 7.1 RTTI class hierarchy, virtual function tables, Lua C-API bindings, and decompiled `NWorld` gameplay formulas extracted directly from `bin/H5_Game_PEST.exe` and `bin/granny2.dll` (Heroes of Might and Magic V: Tribes of the East 3.1).

Reproducible static analysis and extraction is provided by `tools/homm5_decompile` (`homm5-decompile`), which generates `UnityProject/Assets/StreamingAssets/homm5_extracted/decompiled_exe_db.json`.

---

## 1. PE32 Binary Overview (`bin/H5_Game_PEST.exe`)

| Property | Value |
|---|---|
| File Size | `13,089,792` bytes (~12.48 MB) |
| Format | 32-bit Windows PE (`pei-i386`), MSVC 7.1 (Visual C++ .NET 2003, `msvcp71.dll` / `msvcr71.dll`) |
| ImageBase | `0x00400000` |
| EntryPoint VA | `0x00d11e1c` |
| Original PDB Path | `D:\ContinuousIntegration\work\H5_code_a2-p1\src\Release\H5_Game.pdb` |
| Sections | `.text` (`0x00401000`, 10.03 MB), `.rdata` (`0x00e0a000`, 1.08 MB), `.data` (`0x00f1f000`, 1.79 MB), `.tls` (`0x010ea000`), `.rsrc` (`0x010eb000`), `.reloc` (`0x01136000`) |
| Exported Symbols | 11 `SFLB*` symbols (including `?SFLB0_GetUnitExperienceValue@NWorld@@YIHABUSCreatureKillInfo@1@ABK@Z` at `0x004d8ba0`) |

---

## 2. MSVC 7.1 RTTI & Virtual Function Tables

Scanning `.data` for `TypeDescriptor` records whose vftable pointer equals `0x00e98370` (`const type_info::'vftable'`) and cross-referencing `.rdata` `CompleteObjectLocator` structures yields:

- **3,347** RTTI `TypeDescriptor` classes/structs
- **5,577** resolved C++ virtual function tables (`vftable`s)
- **59,205** total vtable slots across **19,618** unique virtual methods in `.text`

### Key Namespace Breakdown

| Engine Namespace | RTTI Classes | Responsibility |
|---|---:|---|
| `NWorld` | 656 | Core game logic: Adventure Map, Heroes, Towns, Pathfinding, Combat, ATB, AI |
| `NUI` | 531 | Adventure, Town, Combat, Duel, and Main Menu UI screens & observers |
| `NDb` | 482 | XML/XDB reflection (`types.xml`), `RPGStats` (`DefaultStats.xdb`), resource streaming |
| `NScene` | 194 | 3D scene graph, camera controllers, lighting, terrain rendering |
| `NGFX` | 158 | Direct3D9 rendering pipeline, shaders, particle effects, Granny3D mesh/skeleton binding |
| `NGame` | 98 | Application state machine, session management, turn orchestration |
| `N Sound` / `NSound` | 61 | FMOD audio system |
| `NNet` / `NUbi` | 112 | Multiplayer LAN / Ubi.com lobby & synchronization |

### Core `NWorld` VTables

| Class | Primary VTable VA | Virtual Methods | Description |
|---|---|---:|---|
| `NWorld::CHero` | `0x00e7e484` | 115 | Hero state, XP/level, primary stats, skills, army, artifacts, movement & mana |
| `NWorld::CHeroMovementCost` | `0x00e935b0` | 6 | Hero step movement cost calculator (roads, terrain penalties, Pathfinding, Snatch, diagonal) |
| `NWorld::CTetraMovementCost` | `0x00e4ce2c` | 6 | Base terrain movement cost calculator |
| `NWorld::CTown` | `0x00e19674` | 97 | Town buildings, daily gold income, weekly creature growth, garrison |
| `NWorld::CPlayer` | `0x00e1a09c` | 62 | Player 7-resource economy, turn state, owned heroes and towns |
| `NWorld::CCombat` | `0x00e79d3c` | 84 | Tactical arena controller, damage calculation, spell casting, retaliation |
| `NWorld::CCombatCreature` | `0x00e4fba4` | 122 | Combat stack state, health pool, shots, Wait/Defend/Morale/Luck |
| `NWorld::CArenaATB` | `0x00e6e250` | 14 | Active Time Battle (ATB) initiative timeline (`100000.0f / Initiative`) |
| `NWorld::CWorld` | `0x00e6260c` | 68 | Top-level adventure map container, `CTileGameInfoMap`, objects, day/week/month clock |

---

## 3. Lua C-API Bindings (`0x00f2def0 .. 0x00f7bd48`)

The executable registers **314 unique Lua C-API functions** in `.data` registration tables. Selected adventure & combat bindings:

| Lua Function Name | Handler VA | Description |
|---|---|---|
| `GetHeroStat` | `0x005c33a0` | Reads hero stat by `EHeroStat` ID (`0..8`) |
| `ChangeHeroStat` | `0x005c3620` | Modifies hero stat by `EHeroStat` ID (`0..8`) |
| `CalcHeroMoveCost` | `0x005ceaf0` | Runs A* pathfinder and returns movement cost to target `(x, y, floor)` |
| `IsTilePassable` | `0x005e11a0` | Checks tile passability in `CTileGameInfoMap` (`floor`: `0` = Ground, `1` = Underground) |
| `AddHeroCreatures` | `0x005c4a10` | Adds creature stack to hero army |
| `RemoveHeroCreatures` | `0x005c4c80` | Removes creatures from hero army |
| `TeachHeroSpell` | `0x005c6440` | Grants spell to hero spellbook |
| `GiveArtefact` | `0x005c69b0` | Equips/adds artifact to hero |
| `GetTownBuildingLevel` | `0x005c8120` | Queries town building level |
| `SetPlayerResource` | `0x005cb2e0` | Sets player resource (`Wood=0, Ore=1, Mercury=2, Crystal=3, Sulfur=4, Gem=5, Gold=6`) |

---

## 4. Decompiled `NWorld` Gameplay Formulas

### 4.1 Global `DefaultStats.xdb` Accessor (`NWorld::GetDefaultStats` at `0x00bcb3f0`)
- Disassembly:
  ```asm
  00bcb3f0: a1 00 3a 04 01    mov 0x01043a00, %eax
  00bcb3f5: c3                ret
  ```
- Called from **499 sites** across `.text`. Returns the `NDb::RPGStats*` singleton loaded from `/GameMechanics/RPGStats/DefaultStats.xdb` (overridden by `a2p1-data/GameMechanics/RPGStats/DefaultStats.xdb` in ToTE 3.1).

### 4.2 Adventure Map Coordinate Packing & Passability (`0x00419690`, `0x00bcc310`, `0x005e11a0`)
- **30-bit Packed Tile Coordinate (`0x00419690`)**:
  ```c
  packed = (x & 0x3FF) | ((y & 0x3FF) << 10) | ((floor & 0x3FF) << 20);
  ```
- **Floor Indexing (`data/scripts/common.lua` & `0x005e11a0`)**:
  - `GROUND = 0` (Surface terrain: `GroundTerrain.bin`)
  - `UNDERGROUND = 1` (Subterranean terrain: `UndergroundTerrain.bin`)
- **`CTileGameInfoMap` Lookup (`0x00bcc310`, `0x005e11a0`)**:
  - Floor array at `[world + 0x20] + floor * 88 + 4`
  - Row pointer `rows[y] + x * 36` (36 bytes per `STileGameInfo`)
  - Passable when `(tile[+0x08] == 1 || tile[+0x08] == 4) && tile[+0x10] == 1`.

### 4.3 Terrain Step Movement Cost (`0x00cab090` -> `0x00ca9a70`, Jump Table `0x00ca9be8`)
- **Base Cost (`0x00ca9ac3..0x00ca9ae2`)**:
  - Road (`roadType` in `{1, 2, 3}` / terrain road flags `{6, 7, 8}`): `baseCost = 75`, `penalty = 0` (`0x00ca9ad3`).
  - Off-road: `baseCost = 100` (`0x00ca9ade`).
- **Non-Native Terrain Penalty (`0x00ca9ae3..0x00ca9be7`, switch table at `0x00ca9be8`)**:
  - If `terrainType == heroNativeTerrain`: `penalty = 0`.
  - Otherwise, by `terrainType` (`0..14`):
    - `{1, 6, 7, 8, 9, 10, 11, 12}` -> `+0` (`0x00ca9be5`)
    - `{0, 4, 5, 13, 14}` (Dirt/Grass, Lava, Subterranean, Snow, Taiga) -> `+25` (`0x00ca9aed: mov $0x19, %eax`)
    - `{2, 3}` (Sand, Swamp) -> `+50` (`0x00ca9af7: mov $0x32, %eax`)
- **Hero Skill Modifiers (`0x00cab136..0x00cab16b`)**:
  - `Snatch` (`[this+0x14] & 1`): `penalty = 0`.
  - `Pathfinding` (`[this+0x14] & 2`): `penalty *= 0.5f`.
- **Diagonal Multiplier (`0x00cab18c..0x00cab196`)**:
  - If `x1 != x2 && y1 != y2`: `cost = (int)((baseCost + penalty) * 1.41421354f)` (`.rdata` `0x00e557b8`, truncated via `cvttss2si`).

### 4.4 Hero Experience Progression Table (`0x00b4c6b0`, `.data` `0x00f755e4` -> `0x01026d90`)
- **Levels 1..12 (Seed Table at `0x00f755e4`)**:
  `[0, 1000, 2000, 3200, 4600, 6200, 8000, 10000, 12200, 14700, 17500, 20600]`
- **Levels 13..24 (`0x00b4c6b9..0x00b4c6df`)**:
  `delta = (int)((XP[i-1] - XP[i-2]) * 1.2f)` (`.rdata` `0x00e4ff30 = 1.2f`)
- **Levels 25..40 (`0x00b4c6e1..0x00b4c712`)**:
  `delta = (int)((XP[i-1] - XP[i-2]) * (i - 13) * 0.1f)` (`.rdata` `0x00e0ae5c = 0.1f`, where `i` is 1-based level `25..40`)

### 4.5 Hero Primary & Secondary Stats (`0x00b5ad70`, `0x00b4ca10..0x00b4cde0`)
- `Attack` (`0x00b4ca60`): clamped `>= 0`
- `Defense` (`0x00b4cab0`): clamped `>= 0`
- `SpellPower` (`0x00b4cb00`): clamped `>= 1`
- `Knowledge` (`0x00b4cb50`): clamped `>= 1`
- `Morale` (`0x00b4cb60`) & `Luck` (`0x00b4cd40`): clamped to `[-5, +5]`
- `MovePoints` (`+0x13c`): `BaseHeroMovement = 2500` (`DefaultStats.xdb`)
- `ManaPoints` (`+0x140`): `Knowledge * HeroManaPerKnowledge` (`10` in `DefaultStats.xdb`, `* 1.5` with Intelligence)

### 4.6 Combat Physical Damage & Kill XP (`0x00a58d30`, `0x00a58de0`, `0x00a597a0`, `0x004d8ba0`)
- **Attack vs. Defense Multiplier (`0x00a58d30`, `0x00a58de0`)**:
  - `Attack > Defense`: `1.0f + 0.05f * (Attack - Defense)` (`0.05f` at `0x00e0cd50`)
  - `Defense >= Attack`: `1.0f / (1.0f + 0.05f * (Defense - Attack))`
- **Base Stack Damage Roll (`0x00a597a0`)**:
  - If `MinDamage == MaxDamage`: `MinDamage * count`
  - If `count <= 3`: sum of `count` random rolls in `[MinDamage, MaxDamage]`
  - If `count > 3`: `(roll1 + roll2 + roll3) * count * 0.33333334f` (`1/3` at `0x00e0ae38`)
- **Range / Melee / Jousting / Luck Modifiers (`0x00a5a900`, `0x00a5c870`)**:
  - Shooter in melee without `ABILITY_NO_MELEE_PENALTY`: `* 0.5f`
  - Ranged half-range or obstacle penalty without `ABILITY_NO_RANGE_PENALTY`: `* 0.5f`
  - `ABILITY_JOUSTING` (`0x00a5c8ca`): `* (1.0f + 0.05f * tilesMoved)`
  - Good Luck: `* 2.0f`; Bad Luck: `* 0.5f`
- **Kill Experience (`NWorld::SFLB0_GetUnitExperienceValue` at `0x004d8ba0`)**:
  ```c
  int exp = info->isSummoned ? 0 : (creature->Exp > 0 ? creature->Exp : creature->Power) * info->count;
  ```

### 4.7 Active Time Battle (`NWorld::CArenaATB` at `0x00a2d6c0`, `0x00a2d700`, `0x00a5e4f0..0x00a5e688`)
- **Creature Period (`0x00a2d6c0`)**: `100000.0f / max(Initiative, 1.0f)` (`ATBBaseTime = 100000`)
- **Hero Period (`0x00a2d700`)**: `100000.0f / 10.0f` (`HeroInitiative = 10`)
- **Good Morale (`0x00a5e515`)**: sets ATB to `0.5f` (half turn period)
- **Wait Command (`0x00a5e610`)**: sets ATB to `0.5f` (half turn period)
- **Defend Command (`0x00a5e660`)**: sets ATB to `0.0f` (full turn period) and grants `+30%` Defense (`DefenceBonusOnDefend = 0.3`)

---

## 5. Granny3D (`bin/granny2.dll`) Oodle1 (`comp=2`) Decompression

All **2,242** skeleton files in `data/bin/Skeletons/` and **577** animation files in `data/bin/animations/` use GR2 v6 container sections compressed with `compression = 2` (**Oodle1Decompress w/ stops**).

Decompiled from `bin/granny2.dll` (ImageBase `0x50000000`):
- `GrannyDecompressData` (`0x5001f090`): dispatches `comp == 0` (memcpy), `comp == 1` (`0x5002ecd0`, Oodle0), and `comp == 2` (`0x5002f3d0`, Oodle1).
- `Oodle1Decompress` (`0x5002f3d0`):
  - Initializes the 7-bit carry arithmetic decoder (`0x5003f980`, `0x5003f9e0`, `0x5003fa40`, `0x5003fa80`, `0x5003fae0`) at `comp + 0x24`.
  - Iterates across 3 stop phases (`first_16bit`, `first_8bit`, `decompressed_size`), each configured by a 12-byte parameter header at `comp + phase * 12` (`0x50040db0`).
  - Decodes adaptive frequency-sorted symbol models (`0x500460b0`, `0x50046480`, `0x50046530`, `0x50046790`) for literals (`lit_m[pos & 3]`), match lengths (`len_m[0..64]`), and 3-part LZ offsets (`((o_hi << 8) + o_mid) * 4 + o_low + 1` at `0x50040f80..0x5004111d`).
- Implemented in `tools/homm5_import/homm5import/gr2.py` (`decompress_oodle1` & `parse_gr2_bytes`).
