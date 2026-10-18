# Native movement command construction — static partial record

This record documents native movement-command payloads and the currently recovered command-construction paths.
It does **not** claim that those paths are the adventure-screen mouse handler or that they implement the required
first-click preview / second-click confirmation sequence.

## Source and runtime status

- Scope: HoMM5: Tribes of the East 3.1 with the checked-in `a2p1-data/` and `a2p1-texts/` overlay.
- Binary: `bin/H5_Game_PEST.exe` (SHA-256 recorded in `docs/dis-doc/SOURCE_CORPUS.md`).
- RTTI source: `decompiled_src/include/RTTI/NWorld_Classes.h`.
- Lua source lead: `decompiled_src/src/LuaAPI/LuaCApiBindings.cpp` and the native handler for
  `Lua_MoveHeroRealTime`.
- Runtime: original executable not run; Unity PlayMode not run.
- Record status: `static partial`.
- Field names below are offsets only. No field is called `path`, `target`, `preview`, or `confirm` unless the
  native evidence establishes that meaning.

## MOV-CMD-001 — command identities, sizes, and Apply slots

**Status:** `static-confirmed`; **runtime:** `not-tested`.

RTTI declares these two `NWorld::CWorldCmd` subclasses:

```text
NWorld::CSetWalkPathCmd
vtable: 0x00e0d730
slot 0: 0x00436db0
slot 7 / Apply: 0x004308f0

NWorld::CStartMoveHeroCmd
vtable: 0x00e0d778
slot 0: 0x00436e70
slot 7 / Apply: 0x00426d50
```

The allocation constructors are:

```text
CSetWalkPathCmd:    0x00436d80, allocation size 0x28
CStartMoveHeroCmd:  0x00436eb0, allocation size 0x18
```

The constructors initialize the inherited command fields and their pointer-like fields. The destructor/slot-0
bodies release the referenced values and restore the corresponding vtable. The class names come from RTTI;
the payload field meanings remain unassigned.

**Evidence:** `decompiled_src/include/RTTI/NWorld_Classes.h` around the `CSetWalkPathCmd` and
`CStartMoveHeroCmd` declarations; binary `0x00436d80-0x00436ddf` and `0x00436eb0-0x00436eaa`.

## MOV-CMD-002 — exact `CSetWalkPathCmd` initializer payload

**Status:** `static-confirmed`; **runtime:** `not-tested`.

The initializer at `0x005d50e0` has seven stack arguments after `ECX = cmd` and returns with `ret 0x1c`.
For neutral argument names `arg_1` through `arg_7`, it performs:

```text
cmd+0x04 = 0
cmd+0x08 = 0
cmd+0x00 = 0x00e0d730
cmd+0x0c = *(arg_1)
cmd+0x10 = *(arg_2)
cmd+0x14 =  byte_at(arg_3)
cmd+0x18 = *(arg_4)
cmd+0x1c = *(arg_5)
cmd+0x20 =  byte_at(arg_6)
cmd+0x24 = *(arg_7)
```

The initializer increments the native reference count for the values stored at `+0x0c`, `+0x10`, and `+0x18`
when those values are non-null. It does not establish source-level meanings for the two copied bytes or the
other pointer-sized fields. The seven-argument stack shape and `ret 0x1c` are part of the ABI evidence.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x005d50e0-0x005d516e`.

## MOV-CMD-003 — exact `CStartMoveHeroCmd` initializer payload

**Status:** `static-confirmed`; **runtime:** `not-tested`.

The initializer at `0x005d51c0` has two stack arguments after `ECX = cmd` and returns with `ret 0x8`:

```text
cmd+0x04 = 0
cmd+0x08 = 0
cmd+0x00 = 0x00e0d778
cmd+0x0c = *(arg_1)
cmd+0x10 = *(arg_2)
```

It increments the native reference count for the non-null values stored at `+0x0c` and `+0x10`. The
constructor also zeroes `+0x14`; the initializer does not write that field.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x005d51c0-0x005d520a`; constructor `0x00436eb0-0x00436ed8`.

## MOV-CMD-004 — observed Apply-side field consumption

**Status:** `static-confirmed` for the listed reads/calls; **runtime:** `not-tested`.

`CSetWalkPathCmd::Apply` at `0x004308f0` validates `+0x10`, resolves an object through that object's virtual
`+0xa4`, prepares temporary values, and branches on the validity of `+0x18`. It then passes values derived from
`+0x0c`, `+0x10`, `+0x14`, `+0x18`, and `+0x20` into `0x0043eea0`, followed by either `0x00cebd50` or
`0x00ceb710`. It finally reads `+0x24`, invokes virtual `+0x18` on the resolved object, and returns `true`
after cleanup. This proves that the fields are consumed by the Apply path; it does not prove which one is the
visual path preview or the final destination.

`CStartMoveHeroCmd::Apply` at `0x00426d50` validates both `+0x0c` and `+0x10`, resolves an object through
virtual offsets including `+0x2c`/`+0x30`, checks a virtual `+0x54`/`+0x44` result, then passes a value from
virtual `+0x9c` into another object's `+0x64`. The routine returns `true` on that native path. The exact
source-level event meaning is not assigned.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x004308f0-0x00430a31` and `0x00426d50-0x00426e1b`.

## MOV-CMD-005 — native command construction and append boundary

**Status:** `static-confirmed`; **runtime:** `not-tested`.

The native routine at `0x00600060` directly calls the `CSetWalkPathCmd` initializer at:

```text
0x006003c6
0x006005a9
0x00600729
```

It directly calls the `CStartMoveHeroCmd` initializer at `0x00600461`. In the first visible construction
sequence, the CSet object is appended through `0x00a95320` at `0x006003e4`, then the CStart object is appended
through the same helper at `0x0060047c`. The later CSet construction sites also append through
`0x00a95320` (`0x006005cc` and `0x0060074c`). The helper receives the address of the newly constructed command
pointer and links it into a vector-like native list; this establishes collection linkage, not execution timing.

`Lua_MoveHeroRealTime` at `0x005cf310` also constructs a CSetWalk/unknown/CStart sequence. That is script/API
coverage and must not be treated as proof of adventure-screen input ordering.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x00600060-0x006007e5`, direct call sites listed above,
`0x00a95320`, and native Lua handler `0x005cf310`.

## MOV-CMD-006 — state dispatch into the two construction paths

**Status:** `static-confirmed` for the dispatch and caller addresses; **runtime:** `not-tested`.

The aligned routine at `0x00600e70` validates an object at `+0x3c`, checks virtual state results, and branches
through its late path:

```text
0x00601734 -> 0x006007f0 when 0x00603560 returns nonzero
0x00601734 -> 0x00600060 otherwise
```

Direct callers of `0x00600e70` recovered from the binary are `0x005945e1`, `0x005973ef`, and `0x0059e48d`.
The state values tested in the aligned routine include `0`, `1`, `3`, and `5`; no source-level names are
assigned to these values. The calls prove a native state-dependent route to command construction but do not
identify the originating UI event.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x00600e70-0x0060177c`, direct caller scan over the executable, and
`0x00603560` call at `0x00601734`.

## MOV-CMD-007 — two-click input remains unproven

**Status:** `unknown`; **runtime:** `not-tested`.

The recovered evidence does not yet connect `CAdventureScreen`, `CAdventureController`, or a native mouse/tile
input callback to `0x00600e70`, `0x00600060`, or `0x006007f0`. It therefore does not prove any of the
following:

- that the first map click only builds/displays a route;
- that a repeated click on the same destination confirms it;
- that a different click replaces or cancels the preview;
- which command is queued on the first click and which is queued on the second;
- whether command Apply occurs immediately or at a later native queue boundary;
- when the hero animation and map path visualization change relative to those events.

The required two-step behavior must not be implemented from the command class names or from the current Unity
input code alone. The current Unity movement implementation is not evidence-backed by this record.

## Current implementation consequence

The command field layouts and native append boundary may be used as ABI fixtures and regression-test inputs.
No Unity preview/confirmation state machine is authorized by these claims until the adventure input dispatch and
native queue execution boundary are recovered and, where possible, observed in the original executable.

## Next trace

1. Follow the direct and indirect callers of `0x00600e70` into the adventure controller/screen RTTI and NUI
   event methods.
2. Locate tile hit-testing and the first/second activation event, then connect each event to the command append
   helper and the Apply boundary.
3. Map the native list's execution/draining method and the order of CSetWalk versus CStartMoveHero Apply.
4. Compare the resulting native event sequence with the Unity adventure input after the original runtime is
   available.
