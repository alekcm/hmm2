# Current reverse-engineering status

Date of record: 2026-10-18. Native executable and Unity runtime have not been run in this environment; Unity
compiler/PlayMode and `dotnet` are unavailable. Static Python checks are available and have previously passed,
but they do not prove native runtime parity.

## Work completed in the dis-doc

- Established a source/evidence protocol and naming policy.
- Recorded the exact native artifact hashes and version boundary.
- Created the first detailed static evidence record for the fog command/visibility path.
- Quarantined the existing approximate Unity fog implementation as non-evidence.
- Preserved explicit unknowns instead of assigning semantic names to unresolved native fields.

## Required next evidence work

1. Complete the ABI reconstruction of `0x98f4e0` and `0x98f610`, including all stack/register arguments and
   the mode values used by every call site.
2. Finish `CPlayer::vfunc_108_0x004fc170` and `vfunc_109_0x004fc2f0` field mapping, including the two player
   array families and the `CWarFogArray` object layout.
3. Trace every read of the player fog arrays and every call to the `CWarFogArray` update/notification methods.
4. Trace the native consumers of `0x009d2550` and identify whether a separate explored-memory layer exists.
5. Trace Lua `OpenCircleFog` and `OpenRegionFog` through world-command creation, queueing, serialization, and
   execution; do not infer behavior from the public function names.
6. Analyze `CEnablePlayerFogOfWarCmd`, `CAllowOpenFogOfWarForAllyCmd`, and `CSetWarfogBehaviour` together with
   their consumers.
7. Build equivalent evidence records for campaign startup, adventure input/movement, calendar/end-turn,
   spellbook, and animation playback before replacing their Unity implementations.

## Explicitly not complete

- Fog of war is **not** source-backed as a Unity system yet.
- The current Unity two-layer fog mesh is not a parity baseline.
- Native render/query polarity is unresolved.
- The original menu and campaign state machine are not documented end-to-end.
- The original two-click movement event order is not documented end-to-end.
- Spellbook mode/slot initialization and standard/Orcs descriptor selection are not fully proven.
- End-turn behavior when there is no active AI turn is not fully proven.

## Acceptance rule for closing a subsystem

A subsystem is not complete when a plausible Unity result exists. It is complete only when the dis-doc contains:

- native entry points and call graph;
- data structures and argument order with evidence;
- state transitions and side effects;
- all relevant data/overlay inputs;
- native query and render/event consumers;
- explicit runtime-test status;
- Unity tests tied to the same observable behavior.
