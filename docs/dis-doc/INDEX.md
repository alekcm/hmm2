# Dis-doc index

This is an index, not a claim that every subsystem is already understood.

| Area | Evidence record | Current status | Main blockers |
|---|---|---|---|
| Source corpus / versioning | [`SOURCE_CORPUS.md`](SOURCE_CORPUS.md) | inventory | audit generated counts and overlay precedence per subsystem |
| Evidence rules | [`EVIDENCE_SCHEMA.md`](EVIDENCE_SCHEMA.md) | established | must be followed by all later records |
| Fog commands and native visibility | [`evidence/native-fog-visibility.md`](evidence/native-fog-visibility.md) | static partial | ABI meanings, array polarity, `CWarFogArray` updates, consumers, render path |
| Map coordinate packing / tile lookup | existing `decompiled_src/src/NWorld/NWorldDecompiled.cpp`; new record pending | static candidate | audit every formula against binary and map asset layout |
| Movement cost / pathfinding | [`evidence/native-movement.md`](evidence/native-movement.md) | static partial | adventure input dispatch, queue execution, path preview and confirmation event order |
| Calendar / turns / AI | [`evidence/native-end-of-turn.md`](evidence/native-end-of-turn.md) | static partial | trace queue, candidate predicates, calendar event/UI ordering, no-active-AI path |
| Campaign/menu/scenario loading | no complete record yet | unknown/partial | native state machine and NUI flow |
| Adventure UI / spellbook | partial records in repository | static partial | original button tree, descriptor selection, initial state and event order |
| Animation import/playback | partial records in repository | static partial | native playback lifecycle and Unity runtime verification |
| Combat | existing formula record | static candidate | full event/state path and runtime parity |
| NDb/XDB/overlay | `decompiled_src/include/NDb/NDbTypes.h`, data tree | inventory/partial | per-subsystem schema and override proof |

## Stable evidence IDs created in the first pass

- `CORPUS-001` — native binary and data scope/hash.
- `CORPUS-002` — generated decompilation is an evidence lead, not a substitute for binary proof.
- `FOG-CMD-001` — fog command RTTI, vtables, sizes, and serializers.
- `FOG-CMD-002` — circle command `Apply` field movement and call into `0x98f4e0`.
- `FOG-CMD-003` — rectangle/list command `Apply` record packing and call into `0x98f610`.
- `FOG-HELPER-001` — native helper dispatch to player virtual slots `+0x1b0` / `+0x1b4`.
- `FOG-HELPER-002` — neutral machine-level stack ABI and mode dispatch for both helpers.
- `FOG-PLAYER-001` — static behavior visible in `CPlayer::vfunc_108_0x004fc170`.
- `FOG-PLAYER-002` — static behavior visible in `CPlayer::vfunc_109_0x004fc2f0`.
- `FOG-QUERY-001` — packed visibility query at `0x009d2550`.
- `FOG-LUA-001` — `OpenCircleFog` registration, parser, and range validation.
- `TURN-CMD-001` — `CEndOfTurn` payload initialization and RTTI.
- `TURN-CMD-002` — `CEndOfTurn::Apply` virtual dispatch.
- `TURN-CMD-003` — `CWorld` slot `+0xd0` address.
- `TURN-CMD-004` — observed operations in `CWorld::vfunc_052_0x00998760`.
- `TURN-CMD-005` — complete calendar/turn semantics and no-active-AI behavior remain unknown.
- `TURN-CMD-006` — raw date mutation and downstream update calls in `0x009949d0`.
- `TURN-CMD-007` — transition gate data flow in `0x00998060`.
- `TURN-CMD-008` — native `GetDate` projections from the raw date field.
- `MOV-CMD-001` — movement command RTTI, sizes, and Apply slots.
- `MOV-CMD-002` — `CSetWalkPathCmd` initializer payload and reference increments.
- `MOV-CMD-003` — `CStartMoveHeroCmd` initializer payload and reference increments.
- `MOV-CMD-004` — movement command Apply-side field consumption.
- `MOV-CMD-005` — native construction and append boundary.
- `MOV-CMD-006` — state dispatch into the movement construction paths.
- `MOV-CMD-007` — two-click adventure input remains unproven.

No Unity implementation may claim parity from these IDs alone; each is marked with its runtime limitations in
the relevant evidence record.
