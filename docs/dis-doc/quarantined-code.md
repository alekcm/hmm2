# Quarantined port code

The following code is kept in the repository for history and compilation context, but it is not a source-backed
parity implementation.

## `UnityProject/Assets/Scripts/HoMM5.Runtime/Map/H5FogOfWar.cs`

The component currently builds two Unity mesh layers and obtains `IsTileExplored`, `IsTileVisible`, and object
visibility from `AdventureMapGameState`. Its two-layer structure, Euclidean/cell coverage, material alpha values,
renderer gating, and lifecycle are not established by the native fog evidence currently available.

Status: **rejected as a parity baseline / do not extend**.

The file must not be cited as evidence for any of the following native meanings:

- explored versus currently visible storage;
- fog byte polarity;
- circle radius geometry;
- object render gating;
- terrain fog render pipeline;
- update timing.

It may be deleted or replaced only after the native record in
[`evidence/native-fog-visibility.md`](evidence/native-fog-visibility.md) is completed and the replacement is
linked to the resulting claim IDs.

## Existing generated decompilation prose

`decompiled_src/src/NWorld/NWorldDecompiled.cpp` and `docs/EXE_DECOMPILATION.md` are valuable evidence leads,
but a prose formula or reconstructed C++ body is not automatically proof. Before a Unity behavior is based on a
record, its exact machine-code range, field accesses, call sites, and relevant data inputs must be cited in a
subsystem evidence document.
