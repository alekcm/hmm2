# Native end-of-turn dispatch and calendar mutation — static partial record

This record separates the native command dispatch, the raw calendar mutation, and the unresolved gameplay
semantics. It is scoped to HoMM5: Tribes of the East 3.1 and the checked-in `a2p1` overlay; no other Heroes
version is used as evidence.

## Source and runtime status

- Binary: `bin/H5_Game_PEST.exe` (SHA-256 recorded in `docs/dis-doc/SOURCE_CORPUS.md`).
- RTTI/source leads: `decompiled_src/include/RTTI/NWorld_Classes.h` and
  `decompiled_src/src/LuaAPI/LuaCApiBindings.cpp`.
- Runtime: original executable not run; Unity PlayMode not run.
- Record status: `static partial`.
- Neutral names such as `field_0c`, `field_10`, `world+0x48`, and `local_gate` are intentional. They do not
  assign unproven gameplay meanings.

## TURN-CMD-001 — command type and payload layout

**Status:** `static-confirmed`; **runtime:** `not-tested`.

RTTI declares:

```text
NWorld::CEndOfTurn
vtable #0: 0x00e0d8e0
slot 0: 0x00437320
slot 7 / Apply: 0x00427140
Base: NWorld::CWorldCmd -> CObjectBase
```

The allocation/initialization body at `0x004372f0` allocates `0x14` bytes, writes vtable `0x00e0d8e0`, and
zeros fields at `+0x0c` and `+0x10` together with the inherited `+0x04` and `+0x08` fields. The slot-0 body at
`0x00437320` releases the pointer-like values at `+0x0c` and `+0x10`, resets them, and restores the vtable.

Use `field_0c` and `field_10`; their source-level names are not assigned here.

**Evidence:** `decompiled_src/include/RTTI/NWorld_Classes.h`; binary ranges `0x004372f0-0x0043735a`.

## TURN-CMD-002 — `CEndOfTurn::Apply` dispatch

**Status:** `static-confirmed`; **runtime:** `not-tested`.

At `0x00427140`, `Apply`:

1. reads `[cmd+0x0c]` and rejects a null/invalid object after an RTTI/object validation sequence;
2. reads `[cmd+0x10]` and performs a second RTTI/object validation sequence;
3. calls virtual offset `+0xd0` on the object from `[cmd+0x0c]`, passing the object from `[cmd+0x10]` as one
   stack argument;
4. returns `true` after the call, or `false` on validation failure.

The final dispatch is visible as:

```asm
mov  eax, [ecx+0x0c]
mov  ecx, [ecx+0x10]
mov  edx, [eax]
push ecx
mov  ecx, eax
call [edx+0x0d0]
mov  al, 1
ret
```

This proves delegation to a virtual owner object. It does not by itself prove that every owner is `CWorld`,
nor that the argument is a player, turn token, or another state object.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x00427140-0x0042718a`.

## TURN-CMD-003 — `CWorld` slot reached by the same offset

**Status:** `static-confirmed` for the vtable address; **runtime:** `not-tested`.

RTTI declares `NWorld::CWorld` vtable #2 at `0x00e6260c`. Slot 52 (`+0xd0`) is `0x00998760`:

```text
NWorld::CWorld vtable #2: 0x00e6260c
slot 52 / +0xd0: 0x00998760
```

When `field_0c` is a `CWorld` object at runtime, the command dispatch resolves to
`CWorld::vfunc_052_0x00998760`. The conditional wording remains necessary because the command's validation
code does not itself name the complete owner type for every call site.

**Evidence:** `decompiled_src/include/RTTI/NWorld_Classes.h`, CWorld declaration around the vtable entry;
binary `0x00998760-0x009987ad`.

## TURN-CMD-004 — first branch in `CWorld::vfunc_052_0x00998760`

**Status:** `static-confirmed` for the listed calls/branches; **runtime:** `not-tested`.

At `0x00998760`, the function receives one stack argument (`ret 0x4`) and:

- rejects a null argument and performs native object/RTTI validity checks;
- calls virtual offset `+0x44` on the argument object;
- calls virtual offset `+0x190` on the same object;
- when `+0x190` returns nonzero, calls virtual offset `+0x50` on that object with stack value `1` and
  returns without entering `0x00998060`;
- when `+0x190` returns zero, calls `0x00998060` on the `CWorld` object.

The call from `CEndOfTurn::Apply` is visible at `0x00427177-0x00427187`: the command's `field_0c` is used
as the virtual owner and `field_10` is pushed as the argument. No source-level labels such as `player`,
`advance_day`, or `next_turn` are assigned to the virtual offsets here.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x00998760-0x009987ad` and `0x00427177-0x0042718a`.

## TURN-CMD-005 — complete turn semantics remain open

**Status:** `unknown`; **runtime:** `not-tested`.

The later records prove a raw date mutation and its native gate, but they do not yet prove the complete turn
contract. The following remain unresolved:

- which command construction/queue boundary supplies `CEndOfTurn` in the adventure UI;
- whether `0x00998060`'s candidate-selection result means no active AI, an exhausted state, or another native
  condition;
- how AI commands are scheduled, drained, or synchronized;
- whether the raw date mutation is reached for every scenario with no active AI turn;
- event ordering between player/object updates, calendar notifications, UI refresh, and command completion;
- whether the command is queued, replayed, synchronized, or applied immediately in each mode.

The current Unity `EndTurn`/`EndDay` logic must not be cited as evidence for any of these points.

## TURN-CMD-006 — raw date mutation in `0x009949d0`

**Status:** `static-confirmed`; **runtime:** `not-tested`.

The helper at `0x009949d0`, with `ECX = CWorld`, performs the following machine-level operations:

1. increments `[world+0x48]` by one at `0x009949dd`;
2. writes `1` to `[world+0x1dc]` at `0x009949e8`;
3. conditionally processes the world object at `[world+0x100]` when `[world+0x1a4]` is nonzero, including
   virtual calls at `+0xa8` and `+0x124`, calls to `0x008e7860` and `0x005091a0`, and a copy into
   `[world+0x58]` from the selected object's `+0x28`;
4. calls `0x0050c020` with `world+0x08`;
5. computes the remainder of the incremented raw value divided by seven, stores remainder-plus-one to
   `[world+0x1dc]`, and calls `0x00993bf0` with stack value `0` when that result is one;
6. runs a second seven-day-boundary branch, a further four-period calculation, and calls `0x0098da70`
   only on the latter branch; `0x00993cd0` is called after that calculation;
7. iterates the world-owned array `[world+0x24, world+0x28)` and invokes virtual slots including `+0x118`,
   `+0x6c`, `+0xc0`, and inner-object slot `+0x224`; subsequent world-owned arrays are also traversed.

This proves the raw increment and the downstream machine-level update order in the function. It does not
assign names such as `day_of_week`, `week_start`, `month_start`, or `daily_reset` to the fields/calls.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x009949d0-0x00994bd0` and continuation of the same routine;
world RTTI at `decompiled_src/include/RTTI/NWorld_Classes.h`.

## TURN-CMD-007 — the date-transition gate in `0x00998060`

**Status:** `static-confirmed` for byte/data flow; **runtime:** `not-tested`.

`0x00998060` does not call `0x009949d0` unconditionally. The call at `0x00998406` is preceded by a test of
local byte `[esp+0x12]` at `0x009983fd`.

The relevant data flow is:

- at `0x0099807c-0x0099809d`, another local byte is initialized to the conjunction
  `([world+0x88] != 0) && ([world+0x90] != 0)`;
- the routine scans the array beginning at `world+0x24` using `0x0098d480`, `0x0098e5d0`, and
  `0x0098e620`, with virtual predicates at offsets `+0x44`, `+0x6c`, `+0x190`, `+0x184`, `+0xc4`, and
  related slots;
- local `[esp+0x12]` starts at zero, is set to one on several fallback/candidate-selection paths
  (`0x00998114`, `0x00998123`, and the `sete` at `0x0099816e`), and is tested again at `0x00998358`;
- local `[esp+0x13]` is reset/set during a second array pass (`0x0099821f`, `0x00998363`), but it is not the
  byte tested at the date call;
- only when `[esp+0x12] != 0` does the function call `0x009949d0`; otherwise it skips that call and proceeds
  to the later virtual dispatch at `+0xd4`.

This is the exact static guard needed for the no-active-AI investigation. The disassembly does **not** prove
that the guard means “no active AI”; that interpretation requires mapping the candidate object predicates and
an original runtime observation. It is therefore forbidden to replace the guard with a Unity boolean chosen
from the current implementation.

**Evidence:** `bin/H5_Game_PEST.exe`, `0x00998060-0x00998418`; helper `0x0098d480-0x0098d4fe`,
`0x0098e410-0x0098e4dd`, `0x0098e5d0-0x0098e610`, and `0x0098e620-0x0098e682`.

## TURN-CMD-008 — native date query reads the same raw field

**Status:** `static-confirmed`; **runtime:** `not-tested`.

`CWorld` vtable slot `+0xa8` is `0x00c61ab0`; its body is exactly `lea eax,[ecx+0x48]; ret`, so the returned
pointer is `&[world+0x48]`. The Lua registration identifies `0x005d7170` as `GetDate` in
`decompiled_src/src/LuaAPI/LuaCApiBindings.cpp`. The native handler's date switch consumes the raw value and
produces these observed projections:

```text
DAY         = raw + 1
WEEK        = floor(raw / 7) + 1
MONTH       = (floor(raw / 28) % 12) + 1
DAY_OF_WEEK = raw % 7 + 1
```

This connects the raw mutation at `0x009949dd` to the native date query's source field. It does not prove when
UI text is refreshed relative to the mutation or which transition event owns that refresh.

**Evidence:** `decompiled_src/include/RTTI/NWorld_Classes.h`; binary `0x00c61ab0-0x00c61ab3` and
`0x005d7170-0x005d7333`; Lua binding declaration/registration at lines around 121 and 699.

## Current implementation consequence

No Unity calendar or AI implementation is authorized by this record alone. A source-backed port must first
model the neutral fields and virtual/callback ordering above, then prove the candidate-selection meaning and
no-active-AI branch. The raw `world+0x48` mutation may be used as a native fact in tests, but not as a complete
turn implementation.

## Next trace

1. Identify every constructor/call site of `CEndOfTurn` and record assignments to `field_0c` and `field_10`.
2. Map the relevant player/object virtual implementations at `+0x44`, `+0x190`, `+0x50`, `+0x184`, `+0x6c`,
   and `+0xc4` without assigning meanings from slot numbers alone.
3. Recover the queue submission/execution boundary and the source of the command's argument.
4. Use a controlled original-executable observation to distinguish an active-AI state from a no-active-AI
   state, then connect that observation to `local_gate`.
5. Trace the adventure-screen calendar/UI notification after the native mutation.
