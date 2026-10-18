# Evidence and claim schema

Every non-trivial statement in `docs/dis-doc/` should be classifiable using the fields below. Markdown is the
human-readable format; the same vocabulary can be used by future machine-readable indexes.

## Required fields

```text
ID:              stable identifier, e.g. FOG-CMD-004
Subject:         native type, function, asset, or runtime path
Scope:           HoMM5 ToTE 3.1; state any overlay or mode restriction
Claim:           one narrow factual statement
Evidence:        exact file + VA/range, source path, XDB path, Lua path, or command output
Status:          inventory | static-confirmed | cross-confirmed | runtime-confirmed | ...
Runtime:         not-tested | original-tested | unity-tested
Unknown:         unresolved meaning, ABI, consumer, or boundary
Rejected:        hypotheses that must not be used
Implementation:  consequence for Unity; use "none" when no implementation is justified
```

A document may contain a group of related claims, but each claim must still have a stable ID or a clearly
bounded evidence block.

## Evidence hierarchy

### A. Raw artifact

The executable, DLL, data file, overlay file, or extracted bytes. Always record the path and, when practical,
the address/range or asset identifier.

### B. Static reconstruction

Disassembly, RTTI, vtable records, generated C++ declarations, serializer code, string references, or a
reproducible importer result. Static reconstruction can prove control flow and data movement, but not necessarily
semantic polarity or visual behavior.

### C. Cross-confirmation

The same fact is supported by independent evidence: for example a command serializer plus its `Apply` routine,
or a Lua binding plus a native consumer. Cross-confirmation does not turn an unresolved runtime meaning into a
proven one.

### D. Runtime observation

A controlled observation in the original executable. Record the setup, input order, relevant state, and output.
If the original cannot be run, say so explicitly.

### E. Unity parity

A Unity test or PlayMode observation compared to the original with the same inputs and state. This is the only
status that permits calling a ported behavior parity-confirmed.

## Naming policy

Use the strongest name proven by evidence:

- `CModifyPlayerFogInCircleCmd::field_14` is acceptable when only the offset and use are known.
- `radius` is acceptable only after the Lua parser, constructor/serializer, and native consumer establish that
  meaning together.
- `visible`, `explored`, `open`, and `fogged` are forbidden as storage names until their polarity and lifecycle
  are proven.
- `vfunc_108_0x004fc170` is preferable to an invented method name.

## What does not count as evidence

- a class name alone;
- a field name guessed from a decompiler or from an existing Unity implementation;
- a familiar Heroes mechanic from another version;
- a default in `RPGStats` without a native read/write path;
- a successful static test that does not exercise the original behavior;
- an implementation that merely looks visually plausible.
