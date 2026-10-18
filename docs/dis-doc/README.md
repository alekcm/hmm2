# Heroes V: Tribes of the East 3.1 — evidence-backed dis-doc

This directory is the working reverse-engineering record for the source-backed Unity 6.6 port.
It is deliberately **not** a design document and it is not permission to fill gaps with plausible game logic.

The target is the HoMM5: Tribes of the East 3.1 executable and its shipped data, including the `a2p1-data/`
and `a2p1-texts/` overlay. The native binary and the data files remain the source of truth. This directory
indexes evidence, records what is proven, and keeps unknown or rejected hypotheses visible.

## Non-negotiable rules

1. **No claim without an evidence anchor.** A claim must point to a binary VA/range, a decompiled source
   location, an XDB/Lua/overlay path, or a reproducible extraction result.
2. **No semantic names from intuition.** Until reads, writes, call sites, serialization, or runtime behavior
   prove a meaning, use neutral names such as `field_14`, `slot_108`, or `array_at_0xbc`.
3. **Static evidence is not runtime evidence.** A disassembled branch may prove that a byte is passed or
   written; it does not by itself prove whether `1` means visible, explored, open, or fogged.
4. **Unknown is a valid result.** Missing consumers, unresolved ABI details, and untested render behavior are
   recorded as `UNKNOWN`; they are not replaced with Unity approximations.
5. **Unity implementation is downstream.** A Unity change must cite the relevant claim IDs and must not use a
   quarantined approximation as its parity baseline.
6. **Version scope is fixed.** Evidence is for HoMM5: ToTE 3.1 and the checked-in overlay. A result from another
   Heroes version, another patch, or a generic engine assumption is not sufficient.

## Evidence status

- `inventory` — artifact or symbol is catalogued; behavior is not established.
- `static-confirmed` — the stated operation is directly visible in machine code or generated declarations.
- `cross-confirmed` — independent call sites, serialization, Lua, XDB, or asset evidence agrees.
- `runtime-unconfirmed` — static behavior is recorded, but the original executable has not been run/observed.
- `runtime-confirmed` — behavior has been observed in the original and the observation is recorded.
- `unity-parity-confirmed` — Unity behavior has been checked against the original.
- `unknown` — the evidence is insufficient; no implementation conclusion may be drawn.
- `rejected` — a former hypothesis is contradicted or unsupported and must not be reused.

## Reading order

1. [`EVIDENCE_SCHEMA.md`](EVIDENCE_SCHEMA.md) — how claims and citations are written.
2. [`SOURCE_CORPUS.md`](SOURCE_CORPUS.md) — exact input artifacts and reproducibility commands.
3. [`claims.tsv`](claims.tsv) — machine-readable claim/status index.
4. [`INDEX.md`](INDEX.md) — subsystem map and document status.
4. [`STATUS.md`](STATUS.md) — current parity blockers and next evidence tasks.
5. [`evidence/native-fog-visibility.md`](evidence/native-fog-visibility.md) — first completed evidence record.
6. [`unknowns.md`](unknowns.md) and [`quarantined-code.md`](quarantined-code.md) — explicit gaps and code that
   must not be treated as source-backed.

## Scope of the first pass

The first pass builds an evidence index before changing Unity code. It covers the paths that are required for
verifiable campaign parity:

- application state, main menu, campaign start, and scenario loading;
- world command creation, serialization, queueing, and execution;
- adventure input, path preview, movement confirmation, and movement execution;
- calendar/day/turn transitions and AI scheduling;
- map tile data, visibility queries, fog writes, fog consumers, and rendering;
- adventure UI, original spellbook descriptors and event ordering;
- Granny/creature/map animation import and playback;
- the ToTE data and `a2p1` overlay precedence.

A complete inventory of every byte and RTTI type is useful, but it is not the same as proving every runtime
behavior. Semantic documentation is therefore expanded by subsystem and keeps its unknowns explicit.

## Regeneration and review

Static evidence is reproducible from the repository root. Commands and input hashes are recorded in
`SOURCE_CORPUS.md`. When a claim changes, update its evidence and status in the same change; do not silently
rewrite a conclusion.
