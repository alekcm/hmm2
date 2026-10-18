# Open questions and unresolved claims

This file is intentionally short and explicit. An item remains open until a later evidence record closes it or
marks it contradicted.

## Fog / visibility

- Meaning and polarity of the byte stored by the fog update routines.
- Whether the player arrays at offsets `+0xbc` and `+0xc8` are two visibility layers, two modes, or another
  player-specific resource. The offsets are observed; the semantic names are not proven.
- Meaning of the array family selected at offsets `+0xd8` and `+0xe4` in the player-side update helper.
- Exact ABI and semantic order of the register/stack arguments to `0x98f4e0`, `0x98f610`, and the player slots.
- Meaning of helper mode values `0`, `1`, and `2` across all call sites.
- Whether `CWarFogArray` change notifications update only gameplay queries, or also terrain/object render data.
- Whether native visibility has a separate explored-memory state in addition to the bool arrays observed so far.
- Full behavior of `CEnablePlayerFogOfWarCmd`, `CAllowOpenFogOfWarForAllyCmd`, and `CSetWarfogBehaviour`.
- All native consumers of `0x009d2550` and the render-side source of fog masks/materials.
- Ally sharing and player selection rules.

## Other parity paths

- Complete native state machine for menu -> campaign -> scenario.
- Exact input/event sequence for path preview versus movement confirmation; native movement command payloads do
  not identify the two clicks.
- Meaning of the candidate predicates and local gate in `0x00998060`, including the no-active-AI case.
- End-turn queue and calendar advancement in scenarios with no active AI command, including event/UI ordering.
- Exact semantic names and data sources for the `CSetWalkPathCmd` and `CStartMoveHeroCmd` payload fields.
- Original adventure spellbook widget tree, mode selection, slot population, and button event order.
- Native animation playback lifecycle and how map/creature clips are selected and advanced.

## Policy

An open question must not be resolved by copying the current Unity behavior, by using a familiar HoMM rule, or
by choosing the interpretation that makes a test pass. It requires a new evidence anchor.
