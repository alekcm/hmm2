# `decompiled_src/` — Reconstructed C++ Headers & Decompiled Engine Code

This directory contains the C++ class declarations, virtual function tables (`vftable`s),
`NDb` data reflection schemas, Lua C-API registration tables, and decompiled `NWorld` /
`Granny2` C++ implementations extracted from `bin/H5_Game_PEST.exe`, `bin/granny2.dll`,
and `data/types.xml` (Heroes of Might and Magic V: Tribes of the East 3.1).

## Directory Layout

| Path | Contents |
|---|---|
| `include/NDb/NDbTypes.h` | All **94 enums**, **389 structs**, and **284 classes** in `namespace NDb` reconstructed from `data/types.xml` with field types, base classes, default values, and original Nival developer comments. |
| `include/RTTI/NWorld_Classes.h` | All **656** `NWorld::*` C++ classes & structs recovered from `bin/H5_Game_PEST.exe` MSVC 7.1 RTTI, including base class inheritance and virtual method table (`vftable`) slots (`CHero`, `CCombat`, `CCombatCreature`, `CArenaATB`, `CHeroMovementCost`, `CTown`, `CPlayer`, `CWorld`, etc.). |
| `include/RTTI/NUI_Classes.h` | All **531** `NUI::*` UI screen, widget, and observer classes and vftables. |
| `include/RTTI/NDb_Runtime_Classes.h` | All **482** `NDb::*` runtime serialization & pointer classes and vftables. |
| `include/RTTI/Engine_Subsystems_Classes.h` | Remaining **1,678** engine classes (`NScene`, `NGFX`, `NGame`, `NSound`, `NNet`, `NUbi`, etc.) and vftables. |
| `src/LuaAPI/LuaCApiBindings.cpp` | All **314** Lua C-API functions registered in `.data` (`0x00f2def0 .. 0x00f7bd48`), with their handler VA in `.text` and direct call targets. |
| `src/NWorld/NWorldDecompiled.cpp` | Decompiled C++ functions and formulas for Hero XP (`0x00b4c6b0`), Hero stats (`0x00b5ad70`), Terrain & Hero movement cost (`0x00ca9a70`, `0x00cab090`, jump table `0x00ca9be8`), 30-bit tile coordinates (`0x00419690`), Combat damage & ATB (`0x00a58d30`, `0x00a597a0`, `0x00a2d6c0`), and `SFLB0_GetUnitExperienceValue` (`0x004d8ba0`). |
| `src/Granny2/Granny2Oodle1Decompress.cpp` | Decompiled C/C++ implementation of `Oodle1Decompress w/ stops` (`0x5002f3d0` in `bin/granny2.dll`) used to decompress HoMM5 `.gr2` skeletons and animations. |

## Regenerating

Run from the repository root:
```bash
homm5-decompile --exe bin/H5_Game_PEST.exe --cpp-out decompiled_src
```
