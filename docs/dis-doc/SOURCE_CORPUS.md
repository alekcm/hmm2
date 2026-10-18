# Source corpus and reproducibility

## Version boundary

- Game: Heroes of Might and Magic V: Tribes of the East 3.1.
- Native executable: `bin/H5_Game_PEST.exe`.
- Native animation/runtime DLL used by the import work: `bin/granny2.dll`.
- Base data: `data/`.
- Expansion overlay: `a2p1-data/` and `a2p1-texts/`.
- Port target: Unity 6.6 (`UnityProject/`).
- Baseline repository revision before this dis-doc initialization: `15f8434404dbf5e02aca5d9e9bce74e64357c921`.

## Input hashes

```text
bin/H5_Game_PEST.exe
f4a06daa2d49658982f96fb918d4a7424f32f4babd392ce20b3d48f4343e31dc

bin/granny2.dll
b40c1636c298ba0af1bfda8cc6db38c73924a2f85397976e40cf2fc2365aae00
```

If either native hash changes, static claims tied to that artifact must be revalidated.

## Checked-in static reconstructions

| Path | Use | Authority level |
|---|---|---|
| `decompiled_src/include/RTTI/NWorld_Classes.h` | MSVC RTTI class/vtable declarations | static inventory; slot addresses are direct evidence |
| `decompiled_src/include/RTTI/NUI_Classes.h` | NUI RTTI declarations and vtables | static inventory |
| `decompiled_src/include/RTTI/Engine_Subsystems_Classes.h` | engine subsystem RTTI declarations | static inventory |
| `decompiled_src/include/NDb/NDbTypes.h` | XDB reflection-derived NDb types | data/schema evidence |
| `decompiled_src/src/LuaAPI/LuaCApiBindings.cpp` | Lua registration table and handler addresses | registration evidence |
| `decompiled_src/src/NWorld/NWorldDecompiled.cpp` | reconstructed formulas and candidate implementations | evidence lead; each formula must still be checked against the binary before use |
| `UnityProject/Assets/StreamingAssets/homm5_extracted/decompiled_exe_db.json` | generated extraction metadata | generated inventory, not semantic proof by itself |

## Reproducible commands

Run from the repository root:

```bash
# RTTI/decompilation regeneration (requires the local project tooling)
homm5-decompile --exe bin/H5_Game_PEST.exe --cpp-out decompiled_src

# Native disassembly for a cited range
objdump -d -M intel \
  --start-address=0x00426930 \
  --stop-address=0x00426a20 \
  bin/H5_Game_PEST.exe

# Native data/RTTI range
objdump -s -j .rdata \
  --start-address=0x00e24f00 \
  --stop-address=0x00e27500 \
  bin/H5_Game_PEST.exe
```

The exact address ranges used for the initial fog record are listed in
[`evidence/native-fog-visibility.md`](evidence/native-fog-visibility.md).

## Corpus counts recorded for orientation

These are inventory numbers, not semantic conclusions:

- 85,541 files under `data/`, `a2p1-data/`, and `a2p1-texts/` in the current checkout.
- 48 C# source files under `UnityProject/Assets/Scripts/` in the current checkout.
- The checked-in `decompiled_src/README.md` reports reconstructed NDb, RTTI, Lua, and Granny outputs; those
  generated counts still need independent audit before being used as behavioral claims.

## Source precedence

When files overlap, the specific ToTE overlay is part of the target and must be consulted. An imported Unity
asset, a generated metadata record, or a previous port implementation never outranks the native binary and the
version-matched data.
