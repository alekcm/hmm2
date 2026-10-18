# Native fog and visibility pipeline — first evidence record

This is the first subsystem record in the dis-doc. It is intentionally incomplete. It records native facts
already established from the ToTE 3.1 executable and keeps semantic gaps open. It does **not** authorize replacing
the Unity fog implementation yet.

## Scope and runtime status

- Binary: `bin/H5_Game_PEST.exe`.
- Relevant generated declarations: `decompiled_src/include/RTTI/NWorld_Classes.h` and
  `decompiled_src/include/RTTI/Engine_Subsystems_Classes.h`.
- Relevant Lua registration: `decompiled_src/src/LuaAPI/LuaCApiBindings.cpp`.
- Runtime status: **original executable not run in this environment; Unity PlayMode not run**.
- Status of this record: `static-confirmed` for the operations explicitly described below,
  `unknown` for semantic polarity, complete ABI, update lifecycle, and render consumers.

## Evidence commands

```bash
objdump -d -M intel \
  --start-address=0x00426930 --stop-address=0x00426b80 \
  bin/H5_Game_PEST.exe

objdump -d -M intel \
  --start-address=0x0043f8e0 --stop-address=0x0043fe20 \
  bin/H5_Game_PEST.exe

objdump -d -M intel \
  --start-address=0x004fc100 --stop-address=0x004fc3c0 \
  bin/H5_Game_PEST.exe

objdump -d -M intel \
  --start-address=0x0098f4e0 --stop-address=0x0098f738 \
  bin/H5_Game_PEST.exe

objdump -d -M intel \
  --start-address=0x009d2550 --stop-address=0x009d25a0 \
  bin/H5_Game_PEST.exe

objdump -d -M intel \
  --start-address=0x005da1e0 --stop-address=0x005da495 \
  bin/H5_Game_PEST.exe

objdump -d -M intel \
  --start-address=0x005e9250 --stop-address=0x005e9350 \
  bin/H5_Game_PEST.exe
```

## FOG-CMD-001 — command types, vtables, sizes, and serialization

**Status:** `static-confirmed`; **runtime:** `not-tested`.

Evidence:

- `decompiled_src/include/RTTI/NWorld_Classes.h`, declarations for
  `CModifyPlayerFogCmd` and `CModifyPlayerFogInCircleCmd`.
- `CModifyPlayerFogCmd` vtable: `0x00e0d9b8`; `Apply` slot: `0x00426980`.
- `CModifyPlayerFogInCircleCmd` vtable: `0x00e0d9dc`; `Apply` slot: `0x00426930`.
- Constructor/destructor bodies and allocation sizes observed in the executable:
  - `CModifyPlayerFogCmd`: `0x004375c0` / `0x004375f0`, size `0x24`.
  - `CModifyPlayerFogInCircleCmd`: `0x00437650` / `0x00437680`, size `0x1c`.
- Serializers:
  - `CModifyPlayerFogCmd::vfunc_003`: `0x0043f900`.
  - `CModifyPlayerFogInCircleCmd::vfunc_003`: `0x0043fcd0`.

The serializer code proves that the command records include the following offsets, but it does not by itself
prove the final semantic names of every field:

```text
CModifyPlayerFogInCircleCmd: +0x0c, +0x10, +0x14, +0x18
CModifyPlayerFogCmd:         +0x0c, +0x10, +0x14..+0x1c, +0x20
```

Use `field_0c`, `field_10`, `field_14`, `field_18`, and `field_20` until the common ABI and all consumers
prove more specific names.

## FOG-CMD-002 — circle command Apply

**Status:** `static-confirmed`; **runtime:** `not-tested`.

At `0x00426930` the command's `Apply` body performs the following exact operations:

```asm
mov  al, [ecx+0x18]
mov  edx, [ecx+0x14]
push 0
push 0
push eax
push edx
lea  edx, [ecx+0x10]
mov  ecx, [ecx+0x0c]
call 0x0098f4e0
```

This proves the execution layout:

- `[cmd+0x0c]` is passed as the object/player-like `ecx` input to `0x98f4e0`;
- `edx` is a pointer to `[cmd+0x10]` storage;
- `[cmd+0x14]` is passed as a scalar stack argument;
- `[cmd+0x18]` is loaded as a byte and passed as another stack argument;
- two zero stack values are also supplied by this call site.

The field meanings are not renamed here. The Lua constructor path shows that the four command payload values
are populated from four parsed values, but the final native meaning of the byte and of each helper argument still
requires the full ABI trace.

## FOG-CMD-003 — list/rectangle command Apply

**Status:** `static-confirmed`; **runtime:** `not-tested`.

At `0x00426980`, the command iterates over a byte range `[cmd+0x14, cmd+0x18)` in records of `0x10` bytes.
For each record it:

1. reads four coordinate words from the record;
2. takes `[cmd+0x10] & 0x0f` and inserts that value into bits 10–13 of the packed coordinates;
3. forms two packed endpoint values;
4. calls `0x0098f610` with the object/player-like reference at `[cmd+0x0c]` and the byte at `[cmd+0x20]`.

The `0x10`-byte record layout and packed endpoint construction are proven. Calling the first command field a
floor/plane field is still a hypothesis until the common coordinate ABI is closed.

## FOG-HELPER-001 — native helper dispatch

**Status:** `static-confirmed`; **runtime:** `not-tested`.

`0x0098f4e0` and `0x0098f610` are native write/update helpers. They are not Unity-style visibility queries.
Both validate the incoming object through the executable's object/RTTI machinery and dispatch through the object
vtable:

```text
0x0098f4e0 -> vtable offset +0x1b0 for its direct branch
0x0098f610 -> vtable offset +0x1b4 for its direct branch
```

Both helpers also have a second path that obtains another player-like object and repeats the corresponding virtual
call. The helper stack arguments include a mode value; code paths explicitly accept mode values `0`, `1`, and
`2`, but the semantic names and the complete ABI are not established.

The direct player class is identified by the RTTI vtable record:

```text
NWorld::CPlayer vtable #1: 0x00e1a09c
this offset: +0x1c
slot 108 / +0x1b0: 0x004fc170
slot 109 / +0x1b4: 0x004fc2f0
```

Evidence: `decompiled_src/include/RTTI/NWorld_Classes.h` and the corresponding executable vtable entries.

## FOG-HELPER-002 — machine-level helper ABI (neutral names)

**Status:** `static-confirmed`; **runtime:** `not-tested`.

The stack layout can be stated without assigning source-level semantic names:

```text
0x0098f4e0:
  ecx   = object/player-like reference
  edx   = coordinate/payload pointer
  [esp+4]  = a1
  [esp+8]  = a2
  [esp+0c] = a3
  [esp+10] = a4 (dispatch mode)
  return cleanup = 0x10 bytes

0x0098f610:
  ecx   = object/player-like reference
  edx   = coordinate/payload pointer
  [esp+4]  = a1
  [esp+8]  = a2
  [esp+0c] = a3 (dispatch mode)
  return cleanup = 0x0c bytes
```

This follows from the prologues, the `ret` cleanup values, and the stack offsets used before the virtual calls.
The direct calls are:

```text
0x0098f4e0, mode a4 = 0 or 1:
  CPlayer slot 108(pos = edx, p1 = a1, p2 = a2, p3 = a3)

0x0098f610, mode a3 = 0 or 1:
  CPlayer slot 109(pos = edx, p1 = a1, p2 = a2)
```

For mode `1`, the helper also walks its native related-object path and calls the same corresponding player
slot on accepted objects. For mode `2`, the direct call is skipped and the related-object path is entered.
The loop counter reaches eight candidates in the observed code. The relationship test and the exact meaning of
those candidates are not named here.

At the circle command call site `0x00426930`, the helper receives `a1 = field_14`, `a2 = field_18`,
`a3 = 0`, and `a4 = 0`. This is an ABI fact; it does not establish the source-level meaning of `field_14`,
`field_18`, or the values written by slot 108.

## FOG-PLAYER-001 — CPlayer slot 108 static operations

**Status:** `static-confirmed` for memory operations and loop geometry; **runtime:** `not-tested`.

`CPlayer::vfunc_108_0x004fc170` visibly performs these operations:

1. It selects one of two player-relative array families based on a byte stack argument:
   - nonzero selects the object at `this + 0xc8`;
   - zero selects the object at `this + 0xbc`.
2. It reads a packed coordinate word through its first pointer argument.
3. It extracts a four-bit selector from bits 20–23 of that word and bounds-checks it against the selected
   array's number of entries.
4. It obtains an element containing:
   - a data pointer at element `+0x20`;
   - two dimensions at element `+0x24` and `+0x28`;
   - a change counter at element `+0x2c`, observed in the inner write helper.
5. It extracts `x = packed & 0x3ff` and `y = (packed >> 10) & 0x3ff`.
6. It computes a bounded rectangular iteration around `(x, y)` and performs a squared-distance test before
   processing a cell. The machine code compares `dx*dx + dy*dy` with a squared stack parameter.
7. For cells passing that test it calls the internal helper at `0x004fc100` with a packed-cell pointer and
   additional values inherited from the native call ABI.

The following names are deliberately **not** assigned by this record:

- the semantic name of `this+0xbc` or `this+0xc8`;
- the polarity of the byte written to a cell;
- the meaning of the selector in bits 20–23;
- the exact source-level order of every stack argument;
- whether the loop is a gameplay visibility update, an explored-memory update, or another fog mode.

### Inner write helper at `0x004fc100`

The helper receives a packed-cell pointer and a byte argument. It:

- extracts the selector from bits 20–23;
- selects one of the player-relative arrays at `this+0xd8` or `this+0xe4` based on another byte argument;
- obtains the selected element's row pointer at `+0x20`;
- bounds-checks `x` and `y` against the element dimensions at `+0x24`/`+0x28`;
- compares and, if different, writes one byte to the cell;
- increments the element counter at `+0x2c` and calls `0x0050c020` for the element and for the player-side
  object.

This proves a native cell-write and notification path. It does not prove what the byte represents or which
renderer consumes the notification.

## FOG-PLAYER-002 — CPlayer slot 109 static operations

**Status:** `static-confirmed` for the observed rectangular write loop; **runtime:** `not-tested`.

`CPlayer::vfunc_109_0x004fc2f0`:

- reads a packed coordinate word and extracts its four-bit selector from bits 20–23;
- obtains an element from the player-relative array rooted at `this+0xbc`;
- extracts the low-ten-bit `x` and `y` values from a packed endpoint;
- loops over inclusive coordinate ranges derived from the endpoint words;
- writes a byte into the element's row/data storage at `+0x20`;
- increments the element counter at `+0x2c` and calls `0x0050c020` during/after the update.

The exact endpoint argument order, byte polarity, relationship to slot 108, and reason why this slot directly
uses the `+0xbc` family are not yet resolved. Do not label this as `OpenRegionFog`, `explored`, or `visible` yet.

## FOG-QUERY-001 — packed-cell query at `0x009d2550`

**Status:** `static-confirmed` for extraction and bounds/read behavior; `cross-confirmed` only for the existence
of a player/selector context; **runtime:** `not-tested`.

At `0x009d2550`, the function:

1. obtains a packed record pointer from its stack argument;
2. extracts a four-bit selector using `(word >> 20) & 0x0f` and passes it to a virtual call at offset `+0x1a0`;
3. extracts `x = word & 0x3ff` and `y = (word >> 10) & 0x3ff`;
4. compares them against dimensions at `+0x24` and `+0x28`;
5. obtains row pointers through the data pointer at `+0x20`;
6. returns the byte at the selected cell, or zero for a failed/out-of-range lookup.

The bit layout is proven. Call-site context identifies the four-bit selector as player-specific in the visibility
query path, but the semantic polarity of the returned byte and the existence of a second explored-memory layer
are still unknown.

## FOG-ARRAY-001 — CWarFogArray type identity

**Status:** `static-confirmed` for type identity and inheritance; **runtime:** `not-tested`.

RTTI identifies `CWarFogArray` as:

```text
CFuncRMBase<CArray2D<bool>>
  -> NWorld::CRMBase
  -> NVisit::IVisObj
  -> CObjectBase
```

Its primary vtable is `0x00e1a048` with `this_offset=+0x34`. The inheritance and the `bool` element type are
RTTI/declaration evidence. They do not by themselves prove dimensions, update scheduling, cell polarity, or
render behavior.

## FOG-LUA-001 — OpenCircleFog registration and command construction

**Status:** `static-confirmed` for registration, validation branch, and command-field copying;
**runtime:** `not-tested`.

Evidence:

- `decompiled_src/src/LuaAPI/LuaCApiBindings.cpp` registers `OpenCircleFog` with handler `0x005da1e0`.
- `0x005da1e0` parses the Lua call through the native argument parser and checks a parsed scalar for negativity.
- The `.rdata` string at `0x00e27414` is `"Range is negative"` and is referenced by this handler.
- The handler allocates a `0x1c`-byte command and calls `0x005e9300` to initialize the
  `CModifyPlayerFogInCircleCmd` payload.
- `0x005e9300` writes the command vtable `0x00e0d9dc`, copies the first word of its first pointer argument to
  `[cmd+0x0c]`, the first word of its second pointer argument to `[cmd+0x10]`, the first word of its third
  pointer argument to `[cmd+0x14]`, and one byte from its fourth pointer argument to `[cmd+0x18]`.

This connects the public Lua binding to the native command layout. It does not complete the world-command queue
or prove the visual result of the command.

## FOG-QUEUE-001 — not yet closed

The handler's subsequent command submission calls and the scenario/world queue still need a dedicated trace.
The following must be identified before implementing Lua-driven fog in Unity:

- the exact command owner/queue;
- execution timing relative to a world tick;
- serialization/replay/network behavior;
- whether command execution updates only the player arrays or also a render/query cache;
- behavior when the target player is an ally or when fog-of-war is disabled.

## Known invalid inferences

The following are explicitly rejected until new evidence appears:

- treating `H5FogOfWar.cs`'s `_fogExplored`/`_fogVisible` model as the native storage model;
- assuming Euclidean tile circles are equivalent to the native squared-distance loop without matching all bounds,
  endpoint, and coordinate conventions;
- assuming a byte value `1` means visible/open or `0` means fogged/closed;
- treating `BaseHeroLookRange`, Scouting, or `RPGAdventureParams` as proof of the native fog update path;
- assuming the native query at `0x009d2550` proves an explored-memory layer;
- calling the arrays at `+0xbc`, `+0xc8`, `+0xd8`, or `+0xe4` `visible`/`explored` without consumer evidence.

## Next investigation sequence

1. Trace all callers of `0x98f4e0` and `0x98f610`, recording the exact stack at every call site.
2. Trace all reads/writes of `CPlayer` offsets `0xbc`, `0xc8`, `0xd8`, and `0xe4`.
3. Disassemble the `CWarFogArray` vtable methods and the `0x0050c020` notification target in context.
4. Trace `0x009d2550` callers and the object providing its `+0x1a0` virtual method.
5. Trace `CEnablePlayerFogOfWarCmd`, `CAllowOpenFogOfWarForAllyCmd`, and `CSetWarfogBehaviour`.
6. Only after those steps, document the native render-side consumer and define a Unity parity test.
