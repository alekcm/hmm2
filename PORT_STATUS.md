# HoMM5 ToTE 3.1 → Unity 6.6 — port status

What is here, what is verified, and what is not done.

## Native presentation / separate main-menu scene — 2026-10-18

- `Assets/Scenes/MainMenu.unity` is now enabled build scene 0, with a real
  scene transition to AdventureMap for New Game / Continue. Editor Play also
  enters it by default, with an explicit current-scene debug opt-out. A stale
  initialMapId no longer skips the menu without startDirectlyOnMap.
- New stdlib `ui` importer stage resolves ToTE expansion layout overrides,
  exports 160 original DDS-derived images and 24 XDB window layouts, crops
  padded native pixel backgrounds, and includes both roster and campaign faces.
  `all` includes the stage; existing imports need only `python import_to_unity.py ui`.
- Runtime consumes that skin: native top resources, bottom-left minimap,
  bottom-right command dial and button states, hero/town roster, menu logo and
  parchment. Diagnostic stats/log/map-list widgets are not the native play HUD.
  End Turn, menu, next hero, floor switch, scenario info and existing hero panel
  use gameplay callbacks. Queued-route Move Hero stays explicitly disabled.
- Removed artificial cut-cell caps/walls and height snapping. Heights and
  colliders share triangle sampling; normals use the global height field across
  chunk seams. Original coverage planes now blend original ground textures via
  a Resources-included Built-in surface shader / 2D arrays, with older hardware
  retaining the dominant-layer fallback. GPU resources have per-map ownership.

**Not finished / not claimed:** animated 3D menu backdrop, native fonts and all
modal panels, exact effects/lighting, gameplay-consistent replacement of random
object editor cubes, and original scenario scripting. The cubes are NOT blindly
hidden because they represent playable towns/resources/monsters. The user's
Game view is zoomed 1.3×; use Fit when judging the complete UI.

Validation: original UI stage successfully executed; full Python suite passes
167 tests (one skipped), including 9 new UI/scene checks. Four heuristic C#
analyzers pass and all 49 C# syntax trees parse. CPU layout/terrain cases added to Unity PlayMode tests (not run
here). No Unity executable is present: C# heuristics / syntax parsing are not
Unity compilation, shader compilation or screenshot validation.

## Follow-up: Newtonsoft test dependency and WAV warning — 2026-10-18

With overrideReferences enabled, the PlayMode assembly must explicitly list
Newtonsoft.Json.dll in precompiledReferences alongside nunit.framework.dll.
The Unity.Nuget.Newtonsoft-Json asmdef reference does not supply that DLL; it
has been removed from the test assembly's references. A project-wiring
regression now checks overridden assemblies that import Newtonsoft.

H5Wav.ReadU32 now assembles four uint operands before implicitly widening the
result to long, avoiding the mixed signed bitwise expression behind CS0675.
Four Unity test cases cover zero, int.MaxValue, 0x80000000 and uint.MaxValue at
a nonzero buffer offset. They require execution in Unity. The 14 targeted
Python checks and four heuristic C# analyzers pass; all 44 C# syntax trees
parse. This follow-up was not compiled or run in Unity in this environment.

## Unity 6.6 compiler diagnostics — 2026-10-18

The user's first Editor compile exposed errors that the heuristic checkers did
not detect. These have been addressed in source: Quaternion normalization,
double-to-float conversion for regeneration, List.Add arity, duplicate/shadowed
locals, missing creature-name API, a static IsBank call, garrison list access,
minimap hit testing, and GetInstanceID (replaced by GetEntityId for Unity 6.6).
Runtime-only view fields are NonSerialized; PlayerResourceInventory is now
Serializable without excluding it from JSON saves.

The PlayMode assembly now uses an explicit UnityEngine.TestRunner reference and
an explicit NUnit DLL, with overrideReferences and UNITY_INCLUDE_TESTS, and no
legacy optionalUnityReferences/TestAssemblies injection. Seven narrow Python
source-regression tests cover the reported bad expressions; Unity behaviour
tests cover quaternion sampling, garrisons/siege detection, regeneration and
resource serialization. They still need execution in the real Editor; this
sandbox has not compiled the fixes against Unity DLLs.

## Previous audit — 2026-10-17

See [the current single-mission audit (Russian)](SINGLE_MISSION_AUDIT_RU.md)
for the capability matrix, fixes, verification commands and remaining blockers.
The sections below are an incremental history, not a consistent current feature
inventory: old statements such as "no audio" or "no AI" are superseded by later
continuations. **No Unity Editor/player run or compilation has been performed.**

This continuation repairs the hidden/uninitialised main menu, separates the
adventure UI layer from its shared canvas, adds pause/resume and safe failed-load
navigation, registers the bootstrap scene for builds and references uGUI
explicitly. The chosen faction now drives the first human town's gameplay
state (not its exterior model), neutral towns stay neutral, initial dwelling
stock is populated, and new scenarios/legacy saves no longer inherit old
rosters or garrison caches. Ten Unity PlayMode regressions have been added
but require execution in Unity; four project-wiring tests run in Python.
Generated import output is now ignored rather than committed.

## Scope, stated plainly

<<<<<<< HEAD
A full port of the game is not something one pass produces. The engine is
`bin/H5_Game_PEST.exe` (13 MB of compiled code): original turn order, movement
rules, combat resolution, spells, abilities, hero levelling, town growth, AI,
the Lua VM, network play. The data files do not contain those implementations;
only a clearly labelled prototype day loop and simplified path movement are
implemented here.

What **is** here is the other half — the content and the formats. Every number
the game plays with, every mesh it draws, every map it ships, decoded from the
real files and loading in a Unity 6.6 project. That is the foundation a port is
built on, and it is the part that cannot be shortcut.
=======
Both the shipped data formats **and** the compiled engine binary (`bin/H5_Game_PEST.exe`
and `bin/granny2.dll`) have been reverse-engineered and decompiled so that core
gameplay formulas, RTTI class hierarchies, virtual function tables, Lua C-API
bindings, and Oodle1-compressed Granny GR2 skeletons/animations are extracted
directly from the shipped binaries rather than guessed. See
[`docs/EXE_DECOMPILATION.md`](docs/EXE_DECOMPILATION.md) for the complete binary
reference.
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad

## Delivered

### 1. Executable & DLL Decompiler — `tools/homm5_decompile/`

Static PE32 analyzer and x86 decompiler (`homm5-decompile`) targeting
`bin/H5_Game_PEST.exe` (ImageBase `0x00400000`, MSVC 7.1) and exporting
`UnityProject/Assets/StreamingAssets/homm5_extracted/decompiled_exe_db.json`:

- **3,347** MSVC 7.1 RTTI `TypeDescriptor` classes (`0x00e98370` `type_info` vftable)
- **5,577** resolved virtual function tables across **19,618** unique virtual methods
- **314** unique Lua C-API function registrations (`0x00f2def0 .. 0x00f7bd48`)
- **499** call cross-references to `NWorld::GetDefaultStats()` (`0x00bcb3f0` -> `0x01043a00`)
- **16** decompiled `NWorld` gameplay formulas and tables:
  - 40-level Hero XP progression (`0x00b4c6b0`, seed table at `0x00f755e4` -> `0x01026d90`)
  - Hero primary/secondary stat clamping (`0x00b5ad70`, `0x00b4ca10..0x00b4cde0`)
  - 30-bit packed tile coordinates (`0x00419690`, `0x00bcc310`) & passability (`0x005e11a0`)
  - Terrain step movement cost (`0x00cab090 -> 0x00ca9a70`, jump table `0x00ca9be8`: Road `75`, Off-road `100`, non-native penalty `{0, 25, 50}`, Pathfinding `* 0.5f`, Snatch, positive reduction at `SHeroMoveModifiers+0x14`, diagonal `* 1.41421354f` at `0x00e557b8`)
  - Combat Attack/Defense damage multiplier (`0x00a58d30`, `0x00a58de0`), base stack damage roll (`0x00a597a0`), range/melee/Jousting/Luck modifiers (`0x00a5a900`, `0x00a5c870`), and `NWorld::SFLB0_GetUnitExperienceValue` (`0x004d8ba0`)
  - Active Time Battle (`NWorld::CArenaATB` at `0x00a2d6c0`, `0x00a2d700`, `0x00a5e4f0..0x00a5e688`: unit period `100000.0f / Initiative`, hero period `100000.0f / 10.0f`, Good Morale `0.5f`, Wait `0.5f`, Defend `1.0f` + `+30%` Defense)

### 2. Offline importer — `tools/homm5_import/`

Reads the shipped formats (with automatic Tribes of the East Patch 3.1 `a2p1-data/`
and `a2p1-texts/` overlay):

| format | what it is | status |
| --- | --- | --- |
| `.xdb` (62144 + `a2p1-data`) | XML descriptors, 3 href kinds, Patch 3.1 overlay | **decoded** |
| `DefaultStats.xdb` | Global `NDb::RPGStats` (`NWorld::GetDefaultStats`) | **decoded** |
| `GroundTerrain.bin` (293) | height / flags / river / passability planes | **decoded, 293/293** |
| `bin/Geometries` (3567) | Nival container meshes | **decoded, 3561 exported** (6 are empty) |
| `.dds` (9258) | DXT1/3/4/5, BGRA, RGB565 | **decoded, 9258/9258** |
<<<<<<< HEAD
| `texts/` (15860) | UTF-16 LE localization | **decoded** |
| `GameMechanics` | creatures, spells, artifacts, buildings | **decoded** |
| `AdvMapDesc` (142) | map objects | **decoded, 198001 objects across 21 kinds** |
| `bin/Skeletons` (2242) | Granny GR2, Oodle-compressed | **not decoded** |
| `bin/animations` (3393) | Granny GR2, 2816 stored / 577 Oodle1 | **2816 decoded, 577 skipped** |
=======
| `texts/` + `a2p1-texts/` | UTF-16 LE localization with Patch 3.1 overlay | **decoded** |
| `GameMechanics` | creatures, spells, artifacts, buildings, defaultStats | **decoded** |
| `AdvMapDesc` (142) | map objects (`Floor`: `0`=Ground, `1`=Underground) | **decoded, 197936 objects** |
| `bin/Skeletons` (2242) | Granny GR2 v6, Oodle1 (`comp=2`) decompressed | **decoded (`gr2.py`, decompiled from `granny2.dll`)** |
| `bin/animations` (3393) | Granny GR2 v6 (`comp=0` & Oodle1 `comp=2`) | **decoded (`gr2.py`, decompiled from `granny2.dll`)** |
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
| `bin/effects` (1919) | particle systems | **not decoded** |
| `sound/`, `music/` | FMOD-packed audio | **not touched** |
| `data/scripts/*.lua` | campaign and map scripting | **Lua C-API bindings extracted (314 functions)** |
| `data/types.xml` | 775 type declarations | **present** |

### 3. Unity 6.6 project — `UnityProject/`

`6000.6.6f1`, built-in render pipeline, two assemblies, no dependency on any
<<<<<<< HEAD
glTF package. Adventure-map viewer: chunked terrain with cliff walls, water,
198001 placed objects across all 21 shipped map-object kinds, click-to-select with the shipped stats, a map list for all
142 maps, surface/underground switch.
=======
glTF package:
- **Adventure Map vertical slice (`HoMM5.Map`, `HoMM5.UI`)**: campaign/mission menu from exported Campaign XDB records, explicit map loading (no first-map heuristic), chunked terrain with cliff walls, water, 198,014 placed objects (with radians-to-degrees `<Rot>` conversion, `BoxCollider` raycast selection, and `GROUND=0` / `UNDERGROUND=1` floor handling), HUD/object inspector, and source-linked XDB material slices/textures.  A `PLAYER_1` hero can be selected and moved with the confirmed `NWorldPathfinder` cost/passability code.
- **Decompiled `NWorld` Gameplay Systems (`HoMM5.World`)**:
  - `NWorldHero.cs` (`NWorld::CHero` `0x00e7e484`: 40-level XP curve, stat clamping, movement points, mana, 7 army slots, artifacts)
  - `NWorldPathfinder.cs` (`NWorld::CHeroMovementCost` `0x00e935b0`: 30-bit packed tile coords, `STileGameInfo::CalcMoveCost` jump table `0x00ca9be8`, passability plane values `1/4`, penalty-reduction modifier, A* pathfinder)
  - `NWorldCombat.cs` (`NWorld::CCombat` `0x00e79d3c`, `NWorld::CCombatCreature` `0x00e4fba4`, `NWorld::CArenaATB` `0x00e6e250`, `SFLB0_GetUnitExperienceValue` `0x004d8ba0`)
  - `NWorldTown.cs` (`NWorld::CTown` `0x00e19674` & `NWorld::CPlayer` `0x00e1a09c`: resource spending/building state only; unsupported hard-coded income and growth tables removed)
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad

## Recent continuation

- Map-floor semantics now match the shipped Lua constants (`GROUND=0`,
  `UNDERGROUND=1`); each map's `InitialFloor` is imported and used at load. This
  corrects the earlier reversed Unity mapping, which put surface objects on the
  wrong floor and made the default map appear empty.
- The importer now recognizes all **21** `AdvMap` object kinds found in the
  shipped maps (including Hill Forts, Seer Huts, Dwarven Warrens and Cartographers).
  It resolves object identity from shared XDBs where the instance stores only a
  reference; all exported monsters, heroes and artifacts in the corpus now have
  their defining ID, and mine types map to resource kinds.
- Relative geometry references are now resolved against the model XDB, fixing
  terrain-object models that were previously dropped. Fixed-faction towns read
  their faction and exterior model from `AdvMapTownShared.Exterior`; random
  towns remain explicitly unresolved rather than being assigned a guessed race.
  Across the 142 maps, the importer now resolves geometry for **182290 of 198001
  placed objects (92.1%)**, up from 160657 (81.2%). Remaining misses are largely
  effect-only/no-model objects and placeholder test records. `RandomTown` still
  points to the shipped flat test marker; its actual faction is chosen at runtime.
- Terrain files now use the same unique map IDs as map JSON. F1 rebuilds the scene
  for the other floor (when the map has an underground plane), map bounds respect
  both dimensions, and the camera focuses on the map center in world units.
- The Unity DTO now reads town buildings/garrisons and additional map payloads;
  the selection panel reports town faction, owner, and building/garrison counts.
- Added a prototype local day loop: a date/resource HUD and End Day control
  advance days, weeks and 28-day months. Starting resources are loaded from the
  selected difficulty row in `DefaultStats.xdb` (default normal). Owned map mines
  pay their documented daily yield; owned towns pay the daily income for their
  Town Hall upgrade. Both incomes are read from shipped description texts. Player
  heroes receive the shipped base movement (`2500` MP); remaining points persist across
  floor switches by map object ID. The current 100 MP per grid step is provisional,
  not claimed to match the original terrain/army path-cost formula.
- Left-click selects map objects; right-click issues A* movement for local-player
  heroes over imported passability, with diagonal corner-cutting prevented. A hero
  entering a custom map-authored wood/ore/rare-resource pile receives its authored
  amount and the pile disappears; collection persists across floor switches. Random
  piles, gold scaling, chests, mine capture and battles remain unimplemented. Enemy
  heroes are non-controllable; terrain/army/artifact/skill movement rules and
  obstacles beyond the passability plane are not reproduced.

## Continuation: object interactions, save/load, minimap

- The importer now exports the shared record's **`activeTiles`** (which tile a
  hero must step on to trigger a visit) and the treasure **`MinResource`/
  `MaxResource`** pickup roll range. Both were already in the shipped data; the
  runtime could not see them.
- **Adventure-map object interactions** (`H5ObjectVisits`), dispatched on the
  shared `Type` enum (`BUILDING_*`, `TREASURE_*`) and the `AdvMap*` object
  kinds: treasure pickups with data-driven rolls (resource piles, campfires,
  flotsam, random piles, map-authored custom piles), artifact pickup with
  artifact stat bonuses, mine capture, permanent stat visits (Mercenary Camp,
  Marletto Tower, Star Axis, Garden of Revelation, Arena with a modal choice,
  Library of Enlightenment gated on level 10, Learning Stone, Tree of Knowledge
  level purchase), School of Magic spell learning, shrines, keymaster tents,
  once-per-day Magic Well / Fountain of Youth / Oasis / Stables / Rally Flag /
  Fountain of Fortune (movement bonuses are the shipped `DefaultStats.xdb`
  values), once-per-week Windmill / Water Wheel / Magic Spring / Idol of
  Fortune, and temporary morale/luck visits. Combat-, screen- and
  teleport-dependent objects log a clear "not implemented yet" notice at most
  once a day. Amounts the shipped tables do not carry are marked "inferred" in
  `H5ObjectVisits` (gold pile roll unit x100, chest gold x500 / 2500 XP).
- **Per-hero state** (`H5HeroState`): primary skills, level with the triangular
  XP curve, mana, spellbook, artifact satchel, key colors, temporary
  morale/luck (reset daily).
- **Quick save/load** (F5/F9 or HUD buttons): day, resources, hero states and
  positions, collected objects, fired visit effects, mine ownership — JSON in
  `persistentDataPath`, reapplied on the next map rebuild so floor switches
  and loads go through one code path.
- **Minimap**: terrain colors from the painted layers' texture names, object
  dots by kind, camera viewport rectangle; clicking moves the camera.
- Hero models that ship a `move` clip now crossfade idle <-> walk during
  movement.
- Right-clicking an impassable tile (a mine, a monster's footprint) walks the
  hero to the nearest free tile instead of refusing the order.
- Fixed object rotation: map files store radians, the scene builder applied
  them as degrees, so every rotated prop faced within a few degrees of its
  authored direction.

## Continuation: combat system

- **Combat constants** are now imported from `DefaultStats.xdb`
  (`combat/LuckAndMorale`, `combat/Creatures`, root ATB block): defending
  bonus +30%, good/bad luck damage multipliers 2.0 / 0.5, morale ATB values,
  hero initiative. The attack/defence per-point damage coefficients (5% /
  2.5%, caps 3.0 / 0.3) and morale/luck proc chances (4% per point) are not
  in the data files and live in `CombatRules` marked "inferred".
- **Turn-based battles** (`HoMM5.Combat`): square-cell battlefield (the
  engine's `Cell` type is a plain {x,y} pair) 14x10, deployed on real combat
  arena scenery (the shipped `Maps/CombatArenas` maps, picked to match the
  adventure terrain). Initiative-ordered rounds approximate the original ATB
  gauge; Wait, Defend (+30% defence), morale extra/lost turns and luck
  (double/half damage) are all wired to the shipped numbers.
- Damage: stack size x per-creature roll, scaled by the attack/defence
  difference and luck; shooters in melee deal half; one melee retaliation per
  round. Casualties wound the top creature first, exactly by health pools.
- **Player battles**: walking onto a monster's activation tile starts the
  fight (hero halts mid-path). Click to move, click an enemy to attack
  (move-then-attack resolves as one order); Wait/Defend/Flee buttons; Quick
  Combat resolves the rest with the battle AI. The greedy AI shoots when free
  and walks at the weakest enemy otherwise.
- **Aftermath**: victory removes the monster, writes the surviving army to the
  hero state (persisting in saves) and grants experience from the shipped
  per-creature `Exp` values; fleeing leaves the monster wounded for a rematch;
  defeat removes the hero. Monster stack rolls honor `amount`/`amount2` and
  `additionalStacks`.
- Battle HUD: initiative queue, unit cards with the shipped stats, battle log,
  result panel. Unit models use the imported creature geometry (capsule
  fallback without an import).
- Quick combat also runs headless (no combat scene attached), so the rules
  path is exercised without the UI.

## Continuation: full combat scope

- **Creature abilities** (`CombatAbilities`): implemented against the shipped
  ability descriptions. Passive: Large Shield (half non-magical shots),
  Armored (no defence debuffs), magic proofs 25/50/75, elemental immunities,
  undead/demon/elemental/mechanical tags, incorporeal 50% miss. On-hit:
  poison (3 turns), blind (wakes on damage; undead/elementals/mechanicals
  immune), bash stun, cursing attack (Weakness), hexing attack, wound (no
  healing), mark of fire, life drain (heals/revives), fire/magma shield
  reflect (shipped 0.2/0.4 multipliers). Sweeps: fire/acid breath and cleave
  hit the cell behind, three/six-headed and spray hit neighbors, whirlwind
  hits all adjacent. Tactical: jousting (+5%/cell moved, shipped),
  rider charge, strike and return, double shot, enrage (+30% attack per
  friendly death), death explosions, regeneration (30-50/turn, shipped
  desc), lay hands, fright auras. Probabilities not in the data are marked
  inferred in `CombatAbilities`.
- **Spellcasting** (`CombatSpells`): mana costs (`TrainedCost`) and the
  per-mastery damage/duration rows (Base + PerPower x SpellPower) are now
  imported with the spell records and drive every cast. Implemented: the
  destructive damage line (Magic Arrow through Empowered variants, areas
  included), the Light/Dark buff-debuff line (Haste, Slow, Stone Skin,
  Bloodlust, Bless, Curse, Weakness, Disrupting Ray, Blind, Berserk,
  Forgetfulness, Plague, Anti-Magic, Deflect Arrows), resurrection
  (Resurrect/Animate Dead), summons (elementals, phoenix, phantom) and
  Dispel. Elemental immunities and magic proofs gate spell damage. The hero
  casts once per round from a spellbook panel (Spell Power scales; mastery
  tiers wait on the skills system).
- **War machines**: ballista, catapult, first aid tent and ammo cart are
  imported from `RefTables/WarMachines.xdb` (offence/defence/damage factors
  per skill tier, health, shots). Ballistas shoot with hero attack x shipped
  factors, the tent heals the most wounded stack each turn, the catapult
  pounds siege structures, machines ride into battle with the hero's army.
- **Sieges**: walls, gate, three towers and the moat with shipped
  `ArenaBuildingsStats` health (wall 200+100@Castle, gate 200+100/200,
  towers 400). Structures block movement until wrecked, towers shoot every
  round, the moat burns crossers, the attacker brings a catapult. Sieges
  fire on towns/garrison posts; fort level comes from the town's Fort
  building (Fort/Citadel/Castle). The wall-line layout is a prototype grid
  placement (per-arena wall coordinates are still in the executable).
- **Tactics phase**: before the first round the player deploys their stacks
  in the first two columns (the Tactics skill's wider zone waits on skills).
- **Battles everywhere**: monster stacks (as before), enemy towns (siege,
  capture on win), garrison posts and dwarven warrens, guarded dwellings
  (unguarded ones just change hands), and the guarded banks -- Crypt,
  Forgotten Crypt, Graveyard, Dragon Utopia, Naga Bank, Pyramid, Sunken
  Temple, Tomb of the Warrior, Cyclops Stockpile, Dwarven Treasure,
  Gargoyle Stonevault, Ruined Tower, soulstone piles and shipwrecks -- each
  with its classic guard roster and payout (both marked inferred: the
  originals script them in Lua). Payouts grant gold, gems, artifacts and
  experience.
- Death handling: enrage feed, death explosions, poison/blind/stun ticks,
  blessed/cursed damage rolls, one retaliation per round with unlimited and
  ranged retaliation and no-retaliation all wired.

## Continuation: secondary skills

The full secondary-skill system is in, sourced from `Skills.xdb` (221 records),
`HeroClass.xdb`, `Mastery.xdb` and the two `HeroSkills` coefficient blocks of
`DefaultStats.xdb` — every effect size in the runtime is a shipped number.

**Importer** (`gamedata.py`, 5 new tests):
- `load_skills` → `skills.json`: 26 ranked skills (12 common + 8 racial + shatter/voice)
  with per-rank names/descriptions, 55 standard perks, 23 class perks, 108 feats,
  8 ultimates. Perk availability carries the per-class `SkillPrerequisites` rows
  (a feat is learnable only by classes with a row, and only with its dependency
  perks owned — this is how ultimates gate behind three cross-tree feats).
- `load_hero_classes` → `heroClasses.json`: per-class `SkillsProbs` (level-up offer
  weights), `AttributeProbs` (primary-stat rolls), preferred spells.
- `rules.json`: `skillEffects` (flat runtime coefficients: Offence/Defence ranks,
  Archery/Evasion, Retribution, Hold Ground, Training retaliation, Gating portions,
  Necromancy raise, Quickness of Mind, Elven Luck, shatter magic, ...) plus the raw
  `heroSkills` trees and `necromancyTransform` (134 slain→risen rows).
- `maps.py`: hero `Editable` blocks now import `skills` (mastery ranks 1..4), `perks`,
  `spells`, `primarySkills`, `experience` — map heroes start with their authored
  level-up history (verified on A2C1M5).

**Runtime**:
- `H5HeroState`: `HeroClass` (from the hero's town), `Skills`, `Perks`,
  `PendingLevelUps`; `RollLevelUpOffers` (two (skill, perk) pairs, SkillsProbs-weighted,
  perks filtered by `SkillDef.AvailableFor`) + `ApplyLevelUp` (class AttributeProbs
  stat roll, Learning's extra stat every N levels). Level-up dialog routes through
  the existing `H5DialogRequest` pipeline (headless auto-picks option 1).
- `Map/HeroSkillEffects.cs`: one aggregate of every live modifier, computed from
  the shipped coefficients; combat and map read this.
- Combat: Offence/Defence/Archery/Evasion damage channels, Retribution (+5%/morale),
  Training retaliation, Toughness/Frenzy/Bodybuilding stat lines, Hold Ground
  (Defend ×2), Quickness of Mind (+25% initiative), Elven Luck (2.25×), war machine
  tiers from War Machines mastery (+100 hp/rank, perk doubles, ammo cart +1/2/3
  ranged attack, catapult 30/40/50% demolish), Tactics +1 column, Sorcery cast
  interval, school masteries now pick the spell's damage/duration row and gate
  circles (Wisdom opens 3), Arcane Training/Empowered Spells mana and damage,
  magic-damage-taken channel (Protection/Resistance/Magic Cushion).
- Adventure: Logistics movement (+10/20/30%), Estates (+250/day), mana regen 10%/day
  ×Mysticism, Intelligence +50% mana, Learning XP bonus, Necromancy raise after
  victory through the shipped TransformTable, Witch Hut teaches a random skill,
  H key opens the hero skill roster.
- Save/load persists class/skills/perks and recomputes the effect cache.

Still out (skill-adjacent): terrain movement penalties (Pathfinding's −50% waits
for per-tile costs), fog-of-war radius (Scouting), war machine manual control and
multi-shot perks, most active perks (Holy Charge, Prayer, Gating, Demonic Fire...),
magic school spell-learning costs, runic/warcry skill trees, two-hero battles.
Ultimate perks require their three feats but grant only their passive effects
where modeled. Level-up offer pairing (skill+perk per panel) follows the shipped
LevelUpBox layout (SkillsLabel/PerksLabel rows, two panels); the exact original
pairing rules are not in the data and are marked inferred.

## Continuation: towns and the town screen

The town layer is now data-driven: what a town contains, what it costs to
build, what it earns, and what its dwellings breed all come from the shipped
tables rather than from constants in the runtime.

**Importer** (`gamedata.py`, `maps.py`, `xdb.py`; 15 new tests):
- `load_town_buildings` → `townBuildings.json`: **328** records from
  `GameMechanics/TownBuildingSharedStats/<Faction>/<Slot>/*.xdb`, keyed
  `<Faction>/<file>` (`Fort`, `Castle` and `Citadel` repeat across all nine
  faction folders, so the bare filename is not a key). Each carries `type`
  (`TB_DWELLING_1..7`, `TB_FORT`, `TB_TOWN_HALL`, `TB_MARKETPLACE`,
  `TB_SPECIAL_*`, `TB_GRAIL`, ...), `upgrade`, `cost`, `dependencies`,
  `creature`/`creature2`, `growth`, `growthMultiplier`, `incomeGoldPerDay`,
  `devLevelNeeded`, `uiObjectName`, `uiStackPosition` and the localized
  name/description.
- Building prerequisites are the `<Dependences>` hrefs, canonicalized through
  the new `XDB.canonical_href` (the data disagrees with itself on case:
  `Stronghold/Blacksmith.xdb` points at the folder `BlackSmith.xdb`). All 328
  resolve, the graph is acyclic, and exactly **one** dependency crosses faction
  lines — `Necromancy/RuinedTower → Preserve/Fort`, via an absolute href. That
  is a shipped copy-paste bug; the row is kept and pinned by a test, and the
  runtime ignores prerequisites that belong to another faction.
- Growth is not in the building records at all (`<Growth>` appears in none of
  the 328): a dwelling breeds its creature's `WeeklyGrowth`, so
  `fill_dwelling_growth` joins dwellings to `RefTables/Creatures.xdb`. 112 of
  126 dwellings carry a growth; the 14 that do not are the `RandomTown`
  placeholders pointing at `CREATURE_UNKNOWN` (`Creature/Creatures/None.xdb`).
- Fort tiers scale that: Citadel ×1.5, Castle ×2.0 for all eight factions, and
  the Grail ×1.5 — all three **read from the building descriptions**, which
  state them in prose ("Прирост существ увеличивается на 50%", "удваивается",
  "на 50%"). Only 4 of the 8 `TB_GRAIL` records quantify their bonus, so
  `fill_grail_values` copies the stated values onto the silent rows; this is
  the one inferred number in the town tables and the tests say so.
- `load_town_types` → `townTypes.json`: the 11 `RefTables/TownTypesInfo.xdb`
  records with faction, ordered building list, silo resource, native war
  machine, moat damage and moat spells — ACADEMY 36 buildings/Gem/ammo
  cart/340; DUNGEON 37/Sulfur/ammo cart/170/1 spell; FORTRESS→Dwarves
  39/Crystal/ammo cart/170; HEAVEN 36/Crystal/ballista/204; INFERNO
  36/Sulfur/ballista/204; NECROMANCY 36/Mercury/first-aid tent/170/2 spells;
  PRESERVE 38/Gem/first-aid tent/170/1; STRONGHOLD 39/Mercury/ballista/204;
  plus `TOWN_RANDOM_TYPE` (31) and the two empty records.
- `CreatureDef.enumId` from `RefTables/Creatures.xdb` (180 of 196 records).
  This fixes a real defect: `creatures.json` is keyed by file basename while
  maps, spells and dwellings name creatures `CREATURE_ASSASSIN`, so
  `GetCreature` returned null for every map-authored stack.
- `maps.py`: `AdvMapTown.buildings[]` now carries `maxUpgrade` alongside
  `type`/`upgrade`, so a map-authored town knows how far each line can go.

**Runtime**:
- `Map/H5TownState.cs`: live town state — owner, fort level, built set,
  per-dwelling recruit pool, growth on the new-week tick, income per day
  (halls, marketplace, resource silo at `BLD_UPG_2` paying the town type's
  `siloIncome`, grail), build validity (cost, prerequisites, one building per
  day, dev level) and recruitment into a visiting hero or the garrison.
- `AdventureMapGameState`: town registry bound at load, `PayTownIncome`,
  `NewDayForTowns`/`GrowTowns`, `BuildInTown`, `RecruitFromTown`, fort level
  from live siege state, capture wiring, and towns in the save file.
- `UI/TownScreen.cs`: the town screen (T, or the bar button), built in code
  like the rest of the HUD — a Construction column (what stands, what can be
  built next, with cost, prerequisite and reason-why-not) and a Dwellings
  column (pool, growth, hire). It renders the same records the shipped 3D
  interior does; the interior scene itself is not drawn yet.

Deliberately not imported: `GameMechanics/TownSpecialization/**` (281 records)
carries only the `Bonus` enum with no magnitudes, and no shipped map sets
`Specialization` on a town, so there is nothing to bind it to. The
`AdvMapTownShared` records also point at the town interior and siege arena
scenes (`/Arenas/Town/...`, wall/tower/gate `ArenaModObject`s) — that is the
source for the siege battlefield, still unused.

## Continuation: a match you can actually play

Until now the project had no front door. `H5Bootstrap.Start` loaded the first
map in the index, there was one shared treasury, one player id, and no
opponent: `BindMovement(isLocalHero ? this : null, ...)` meant every hero that
was not the local player's simply never moved. A viewer, not a game.

**Now**: main menu → scenario → faction → hero → play, against a computer.

- `UI/MainMenu.cs` — the front door. New Game / Continue / Quit, then the four
  setup steps: a scenario list (arenas filtered out — they are battlefields,
  not scenarios), the eight playable factions with their building counts, the
  faction's named heroes (see the roster section below), and a confirmation panel.
  `H5Bootstrap` no longer loads a map on start; it opens the menu. A map named
  on the component still skips straight in, which is how the editor works.
- `Map/H5PlayerState.cs` — a player record: id, human/computer, faction, town
  type, hero class, **their own treasury**, their heroes and towns, and whether
  they are still alive. Plus the roster: turn index, `EndTurn` advancing to the
  next survivor, `Defeat`, `Winner`.
- Turn order follows the source game: the day advances only when the *last*
  surviving player has acted, not once per player. The HUD's button is relabelled
  "End Turn" for that reason, and Space is bound to it.
- `Map/H5AiController.cs` — the computer's turn: build (dwellings first, then
  fort, then income, cheapest wins), recruit the whole pool it can afford, then
  send each hero at the best target in reach and attack on arrival. Every
  number is a shipped one — cost, growth, prerequisites, `CreatureTier`. It is
  a heuristic, not the engine's AI, which lives in the exe and the Lua scripts.
- Victory and defeat: a player with no town and no hero is out; when one is
  left standing the match ends and a dialog offers the main menu.

**What the data allowed, checked rather than assumed.** The scenario list is
populated because 38 of the 40 `Multiplayer` maps author a `PlayerID` on their
towns — six of them for exactly two players (A1M3, A1S1, A2M4, A2M12, A2M13,
S4), which the menu says outright. `PLAYER_NONE` is the shipped spelling of
"neutral", not a player, and most single-player maps use it for everything;
on those, unowned towns are handed to the computer at match setup. Authored
`PLAYER_3..8` opponents get a roster record each, so a scenario written for
more than two players still has all of them acting.

Not done, and worth saying plainly: there is no hot-seat; the computer answers immediately. The
AI does not defend a besieged town differently from an open field (its hero now
casts and retreats; see the battle AI section below). Town interiors and the siege battlefield are
still not rendered.

### Computer opponent: review pass (what was broken, what changed)

The section above says "against a computer". A review found that, before this
pass, that was not true in practice, and the review fixed it:

- **The project did not compile.** `H5ObjectVisits.TryVisit` used an undefined
  `mapObj`, and two visit helpers passed a `H5HeroState` where a
  `H5MapObject` was needed. Those visits also collected the *hero*
  (`CollectObject(mapObj)` deactivates its argument), so picking up a treasury
  would hide the hero. Visit helpers now use `mapObj` for the hero and `obj`
  for the target; `AdventureMapGameState.ViewOf(def)` gives the scene object.
  `VisitMine` never captured a mine (it tested the hero's owner, then captured
  the hero), and now does.
- **Computer heroes never moved.** Only the local player's heroes were on the
  roster and only they could walk. All heroes are now on the roster; the
  computer's walk the same path and budget, one step at a time, and resolve
  each visit at once (`MoveComputerHeroTo`). Computer fights are resolved
  headlessly (`RunHeadlessBattle`); the player's still open the combat scene.
- **Computer never built or recruited.** Building was checked against `null`
  resources (always "cannot afford" for anything that costs something), and the
  computer's treasury was an empty inventory nothing ever funded. Each player
  now has a treasury (`AdventureMapGameState.TreasuryOf`), and a computer is
  funded from `startComputerResources` the first time it is used.
- **Everything paid into the human.** Mines, towns, estates, chest and pile
  visits, bank rewards and captures all went to the local player. They now go
  to the owner (`TreasuryOf(hero)`, `ActingPlayerId`).
- **The HUD showed an empty treasury.** It read the roster's empty inventory for
  the human; it now reads the human's real treasury.
- **Computer targets.** It no longer sends heroes at hero-versus-hero fights
  (not supported), and it walks to an active tile of its target, so the visit
  fires on arrival.
- **Saves.** Positions of every hero are saved, and the roster (with the
  computer's treasuries) is saved in `H5SaveGame.players`. Older saves restore
  without a roster.

Still open for the computer: no walking animation (it moves in one step), no
spells, no retreat, no defence of a besieged town, and its battles are the
headless reconstruction. Nothing above has been run in Unity; the checks listed
under "Verified" are static.

## Verified, and how

<<<<<<< HEAD
`cd tools/homm5_import && python3 -m unittest discover -s tests` — **144
tests** (1 skipped), every one against the real shipped data. Five drive the C#
static checkers and fail with an install hint when `tree-sitter` is absent.
These are cross-checks between independent sources, not golden files:

- **Town data** (10 tests) — 328 buildings with 328 unique keys and zero
  unresolved or cyclic dependencies; every faction has 7×2 dwellings, 4 halls,
  3 forts, 5 mage-guild tiers and exactly 1 grail; Citadel 1.5 / Castle 2.0 on
  all eight factions; all eight grails 1.5× and 5000 gold/day;
  `Haven/Peasant_Huts` growth 22 = `CREATURE_PEASANT`'s `WeeklyGrowth`, gold
  500; the single cross-faction dependency pinned.
- **Scenario setup** (3 tests) — the new-game flow has data behind it: 38 of
  the 40 Multiplayer scenarios author a `PlayerID` per town (six of them for
  exactly two players), eight factions have buildings to run, and every one of
  the eight maps to a hero class. A menu that lists nothing is not a menu.
- **C# call sites** (3 tests) — the arity checker runs over the real
  `UnityProject` sources and must report nothing; must resolve more than 1000
  calls (a checker that resolves nothing also reports nothing, so silence is not
  a pass); and must still fail its own self-test corpus of planted violations.
- **C# undeclared names** (2 tests) — `csharp_name_check.py` flags a lowercase
  simple name used outside any declaration that could bind it (CS0103). It
  catches the `mapObj` defect that existed on `HEAD`; it does not check types,
  so a wrong argument *type* still needs a compiler or a reading.
- **Python↔C# contract** (5 tests) — every key the importer emits for
  `TownBuildingDef`, `TownTypeDef`, `TownMoatSpellDef`, `CreatureDef`,
  `MapObjectDef`, `MapBuildingStateDef` and `MapArmyStackDef` is a field of the
  C# DTO, parsed straight out of `GameDefs.cs`. This is what stands in for a
  compiler: a renamed or dropped field fails the suite instead of silently
  deserializing to a default.
=======
`cd tools/homm5_import && python3 -m unittest discover -s tests` — **43 tests,
all passing, ~56 s**, every one against the real shipped data. These are
cross-checks between independent sources, not golden files:
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad

- **Container grammar** — every geometry file ends in the 6-byte marker
  `00 00 02 00 05 00`, and a record stream consumes itself exactly.
- **Mesh geometry** — decoded positions fit the bounding box the geometry's own
  `.xdb` declares; `<NumMeshes>` equals both the binary's declared count and the
  count decoded; all triangle indices in range.
- **Normal byte order** — authored normals agree with face normals **99.6%** of
  the time over 53224 triangles. Read as x,y,z instead of z,y,x that falls to
  ~0.27, which is how the order was pinned down.
- **Winding** — after the Z-up→Y-up swap, keeping the winding leaves **0.4%** of
  faces outward and reversing it restores **99.6%**. That measurement is what
  `GlbMeshLoader` implements, and the test guards it.
- **Terrain** — every plane is exactly `(tiles+1)²`, cross-checked against the
  map descriptor's `<TileX>`; height is `V²` f32, river is `(2V−1)²`; the flat
  arena sits at 2.0 (not sea level); `BoatArena` is 100% ground flag 0 at height
  0.0.
- **Map selection** — only `AdvMapDesc` roots are maps: 142 of the 1176 `.xdb`
  under `data/Maps`, the rest being sounds (466), statics (243), textures (150)
  and `AdvMapDescTag` files (62). Every map gets a distinct id.
- **Visit data** — shared records export their `activeTiles` offsets and
  treasure `MinResource`..`MaxResource` roll ranges (TREASURE_WOOD 4..7,
  TREASURE_GOLD 5..10), asserted against A2C0M0's placed objects.
- **Data integrity** — a creature's localized name equals the file its
  `CreatureVisual` points at; its geometry UID names a real file in
  `bin/Geometries`; `ANGEL_WINGS` comes out `ARTF_CLASS_RELIC` / `SHOULDERS` /
  40000 gold from the reference table.
- **Material links** — `Model.Materials` is exported in its source order for
  creatures, artifacts and map statics; every texture record retains its XDB,
  DDS and generated PNG path, with render flags copied from `NDb::Material`.
  Unity binds ordinal slices only when the source/count relationship is
  unambiguous (or one source material covers all slices), and otherwise falls
  back without assigning by filename or PNG order.
- **PNG output** — signature, IHDR dimensions, and an IDAT that inflates to
  exactly `h × (1 + w × 4)`.

Conversion counts actually produced in this repository:

```
gamedata   196 creatures, 321 spells, 97 artifacts, 328 town buildings, 15947 strings
terrain    293 planes, 0 failed
<<<<<<< HEAD
maps       142 maps, 198001 objects across 21 kinds
meshes     3561 .glb, 6 skipped (empty), 320 MB
textures   9258 .png, 0 failed
animations 2816 .json.gz, 577 Oodle1-packed skipped
sounds     2013 effects + 155 music tracks, 2165 files copied
=======
maps       142 maps, 198014 objects
meshes     3561 .glb, 6 skipped (empty), 320 MB
textures   9268 .png, 0 failed
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
```

## Not verified

**The C# does not compile here.** This sandbox has no Unity and no .NET SDK
(`dotnet`, `mcs` and `mono` are all absent), so the runtime and editor assemblies
were written but never built. The Python pipeline is tested; the Unity side is
not. Expect to fix compile errors on first open — the API surface used is
deliberately conservative for that reason.

Two checkers narrow that gap, and neither is a compiler:

- `tools/csharp_access_check.py` — resolves every qualified `Type.Member`
  against the declared types and checks accessibility (private used outside its
  type, internal across assemblies) and existence.
- `tools/csharp_call_check.py` — infers the type of locals, fields, properties,
  parameters, `foreach` variables and `var` initialisers, then checks that every
  call on one of our own types matches a declaration's **arity**. It resolves
  ~1285 call sites across the 35 files and currently reports none.

The second exists because two defects reached the town layer that the first
cannot see, both of them calls through a variable: `MakePanel` called with four
arguments against three parameters, and `H5TownState.X` declared `int` where
`MapObjectDef.x` is `float`. Both are now caught — the regression is pinned by
planting the original `MakePanel` signature back into a copy of the tree and
watching the checker name the line.

What the call checker still does not do, so a real compiler can reject what it
accepts: **argument types** (count only — passing an `int` where a `string` goes
is not caught), overload resolution (a call passes if any signature fits the
count), and anything on UnityEngine/System types, which are not indexed. A type
deriving from `MonoBehaviour` therefore has its inherited members skipped rather
than reported, which is why `Destroy()` and `StartCoroutine()` are not flagged.

The `.unity` scene is hand-written YAML. If Unity rejects it, **Tools ▸ HoMM5 ▸
Create Bootstrap Scene** rebuilds it; the scene is one GameObject with
`H5Bootstrap`, so nothing is lost.

Nothing was run in the Unity editor, so no visual claim is made about how the
maps look.

## Not done

<<<<<<< HEAD
- **Game logic is still a prototype.** There is a local player ID and a
  day/week/month counter. The base hero movement points are imported from the
  original stats, but tile costs remain a provisional flat 100 MP. Object
  visits, pickups, mine capture and hero levelling now run from the shipped
  tables where those exist; battles run on reconstructed rules (see below).
  Towns now build, earn and breed from the shipped tables, and there is a town
  screen over them — but the town's 3D interior, its siege battlefield and the
  `TB_SPECIAL_*` effects (shipyard, grail-free specials, tavern, prison) are
  not implemented, and a hero's grimoire is still a hardcoded 12 spells.
  There is still no original multiplayer turn order, terrain/army/artifact
  movement calculation, map-script override execution, campaigns, AI, Lua
  runtime or networking. Individual hero
  starting stats come from the map's hero records where authored (the
  per-hero shared tables themselves are still not imported; unauthored heroes
  start at 1/1/1/1 with their class skill).
- **Combat is a playable reconstruction, not the original.** The luck and
  morale multipliers, defending bonus, siege structure health, war machine
  stats and creature stats are shipped numbers; the attack/defence damage
  coefficients, proc chances, ability probabilities, the 14x10 battle grid
  and the discrete initiative rounds are documented approximations of the
  engine's ATB model. The siege wall layout and bank guard rosters are
  prototype tables (the originals script them per scenario). Secondary
  skills now drive damage channels, machine tiers, tactics width and spell
  mastery rows (see the continuation notes above); remaining gaps are the
  active perks and the per-tile movement model. Two-hero battles, hero
  spellcasting beyond the once-per-round combat cast, battlefield obstacles
  (fire wall, land mines) and creature-activated specials (battle dive,
  gating, seduce) remain stubs or inert.
- **Animation is partial.** Adventure-map idle and move clips are extracted
  from Granny GR2 and play through `H5Rig`/`H5Anim`; the bind-pose caveat in
  `H5Rig` applies to clips that start far from rest. Combat stances and
  per-bone masking are not implemented.
- **Materials are partial.** Object textures and alpha modes are bound to Unity's
  Standard shader; alpha-test cutouts and basic transparency work. Two-sided
  materials are detected but Built-in Standard has no culling switch; additive
  blending, lightmaps and engine effects still need translation.
- **Terrain blending is partial.** Painted layer descriptors and byte coverage
  planes are imported and consumed. The renderer currently chooses the dominant
  layer per cell (hard boundaries), rather than smoothly blending weights.
=======
- **Gameplay is partial.** Confirmed Hero XP/stat clamps, mana/movement base values,
  source-backed campaign mission selection, map hero movement, movement-cost/passability
  primitives, combat formulas and ATB helpers exist.  Campaign bonus choice/application,
  encounter resolution, turn orchestration, spells/abilities, AI, Lua execution,
  networking and a complete town screen are still absent.
- **No animation.** Skeletons are Granny GR2 with per-section Oodle compression;
  reading them needs an Oodle1 decompressor. Creatures sit in bind pose.
- **Materials are partial.** XDB Model/Material/Texture links and core opaque,
  alpha-test, transparent, sidedness, depth and shadow flags now reach Unity.
  `AM_OVERLAY`, effect-specific shaders, lightmaps and fog opt-out still require
  a confirmed render-path reconstruction; they are preserved as source metadata
  rather than assigned heuristically.
- **No terrain texturing.** Vertex colour by ground kind; the shipped maps carry
  no splat weights that survive import.
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
- **No audio, no UI art, no video.**

## Suggested order from here

<<<<<<< HEAD
1. Get it compiling and open a map — cheapest way to find what the missing
   compile check left behind. The two checkers under "Not verified" catch
   accessibility and call-arity errors, not argument types, so expect some.
2. Hero screen — the town screen is in, the hero side is still the placeholder
   level-up dialog and skill roster; the shipped screens carry the art. The
   hero's grimoire is a hardcoded 12 spells and should read `Spells.xdb`
   through the class's preferred-spell list.
3. Town interior and siege battlefield — `AdvMapTownShared` already points at
   the `/Arenas/Town/...` interior scenes and the siege arena's wall, tower and
   gate `ArenaModObject`s; that data is imported but unused.
4. Finish material translation: two-sided culling, additive passes, lightmaps and
   visual effects; smoothly blend the imported terrain coverage layers.
5. Active skill perks (Holy Charge, Prayer, Gating, Demonic Fire, ...) and
   per-tile terrain movement costs (Pathfinding hooks are in place).
6. Audio: `music/`, `sound/`, `soundsfx/` are FMOD-packed and untouched.

### Named hero roster and static type-scope check

- **Roster.** `RMGPresetTable.xdb`'s `<HeroPool>` lists eight named heroes per
  faction (64 in all). `load_heroes` (`tools/homm5_import`) exports them to
  `Game/heroes.json`, keyed `Faction/File` (e.g. `Haven/Axel`): name, biography,
  `TownType`, class, specialization, primary skill, starting primary stats,
  skills (mastery as ranks 1..4), perks, spells, bank/tent/cart flags and portrait
  paths. The menu's hero step lists the faction's eight heroes. `GameDatabase`
  reads them into `Heroes`, with `GetHero` and `HeroesForTown`.
- **Placement.** `SetupMatch` calls `AssignRosterHero`, which puts the chosen
  hero on the human's first hero authored in the map. `GetHeroState` then uses
  `ApplyRosterHero` (identity, stats, skills, perks, spells, specialty, level 1)
  instead of the authored level-up history (`ApplyAuthoredHistory`). If the map
  gives the human no hero, the log says so and the choice is not placed.
- **Not done.** Portraits are imported but no screen draws them yet. Only the
  first authored hero of the human is replaced; a map that gives the human
  several heroes keeps the others as authored. Skill `PrimarySkill` is stored
  but not learned on its own; the roster's `Editable` skills are what a hero
  starts with.
- **Bug found by static checking.** `AdventureMapGameState.cs` used
  `MapObjectDef` and `HeroDef` without `using HoMM5.Data;`, and
  `H5ObjectVisits.cs` used `ArmyStackState` without `using HoMM5.Combat;`. Each
  is CS0246 and would have stopped the Unity build. Neither the call, access nor
  name checker sees type names, so this went unnoticed. Both usings are added.
- **New checker.** `tools/csharp_type_scope_check.py` (tree-sitter) flags a
  project or common .NET/Unity type used where neither its namespace nor a
  `using` makes it visible. A planted file with the same mistakes is caught
  (4 of 4), and the project now reports 0. It is a heuristic; read every hit.

### Battle AI: hero spells and retreat

Computer fights (`RunHeadlessBattle`) now run the attacking hero's magic and
retreat, through `CombatGameState.AiHeroSide`. The player's own battles are
unchanged; their hero still waits for input.

- **Spells**, once per round before the AI's units move (`AiCastSpell`): the
  most expensive damage spell the hero can afford (`CombatSpells.IsDamageSpell`,
  the same rule `Cast` uses) at the enemy stack worth the most. In round 1, if no
  damage spell is castable, one of Bless / Haste / Stone Skin / Bloodlust goes on
  the AI's strongest stack. Worth is `Count x Def.Power` (the shipped rating).
- **Retreat** (`AiShouldRetreat`): from round 2, when the enemy's worth is at
  least `AiRetreatRatio` (4) times the AI's. The army keeps its survivors, the
  enemy keeps its wounds (the `Fled` outcome, which `ApplyCombatResult` already
  handled for the player).
- **Not done.** The AI does not choose spells by value against its own
  creatures (no curses or debuffs), does not defend a siege differently, and the
  4x threshold is a guess, not measured against the original engine.
- **Not compiled.** These are static checks only; see the checker notes above.

=======
1. Get it compiling and open a campaign mission — cheapest way to find what the
   missing compile check left behind.
2. Confirm the PE adventure interaction/encounter handlers before adding monster,
   resource, artifact, and town visit states; do not substitute guessed triggers.
3. Continue only with confirmed combat UI/state and turn orchestration, then bind
   the already reconstructed damage/ATB formulas.
4. Confirm the PE render handlers for `AM_OVERLAY`, effects and `AddPlaced`, and
   GR2 + Oodle1 runtime sampling so creatures animate instead of staying in bind
   pose.  Town income/growth remains data-dependent and is not hard-coded.
>>>>>>> 9e896184bc260ab75851082df162eebbb38277ad
