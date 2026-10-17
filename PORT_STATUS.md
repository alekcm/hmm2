# HoMM5 ToTE 3.1 → Unity 6.6 — port status

What is here, what is verified, and what is not done.

## Scope, stated plainly

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

## Delivered

### 1. Offline importer — `tools/homm5_import/`

Standard library only, no dependencies. Reads the shipped formats:

| format | what it is | status |
| --- | --- | --- |
| `.xdb` (62144) | XML descriptors, 3 href kinds | **decoded** |
| `GroundTerrain.bin` (293) | height / flags / river / passability planes | **decoded, 293/293** |
| `bin/Geometries` (3567) | Nival container meshes | **decoded, 3561 exported** (6 are empty) |
| `.dds` (9258) | DXT1/3/4/5, BGRA, RGB565 | **decoded, 9258/9258** |
| `texts/` (15860) | UTF-16 LE localization | **decoded** |
| `GameMechanics` | creatures, spells, artifacts, buildings | **decoded** |
| `AdvMapDesc` (142) | map objects | **decoded, 198001 objects across 21 kinds** |
| `bin/Skeletons` (2242) | Granny GR2, Oodle-compressed | **not decoded** |
| `bin/animations` (3393) | Granny GR2, Oodle-compressed | **not decoded** |
| `bin/effects` (1919) | particle systems | **not decoded** |
| `sound/`, `music/` | FMOD-packed audio | **not touched** |
| `data/scripts/*.lua` | campaign and map scripting | **not touched** |
| `data/types.xml` | 775 type declarations | **present, unused** |

### 2. Unity 6.6 project — `UnityProject/`

`6000.6.6f1`, built-in render pipeline, two assemblies, no dependency on any
glTF package. Adventure-map viewer: chunked terrain with cliff walls, water,
198001 placed objects across all 21 shipped map-object kinds, click-to-select with the shipped stats, a map list for all
142 maps, surface/underground switch.

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

## Verified, and how

`cd tools/homm5_import && python3 -m unittest discover -s tests` — **90 tests**
(89 pass, 1 skipped), every one against the real shipped data. These are
cross-checks between independent sources, not golden files:

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
- **PNG output** — signature, IHDR dimensions, and an IDAT that inflates to
  exactly `h × (1 + w × 4)`.

Conversion counts actually produced in this repository:

```
gamedata   196 creatures, 321 spells, 97 artifacts, 328 town buildings, 15860 strings
terrain    293 planes, 0 failed
maps       142 maps, 198001 objects across 21 kinds
meshes     3561 .glb, 6 skipped (empty), 320 MB
textures   9258 .png, 0 failed
```

## Not verified

**The C# does not compile here.** This sandbox has no Unity and no .NET SDK
(`dotnet`, `mcs` and `mono` are all absent), so the runtime and editor assemblies
were written but never built. The Python pipeline is tested; the Unity side is
not. Expect to fix compile errors on first open — the API surface used is
deliberately conservative for that reason.

The `.unity` scene is hand-written YAML. If Unity rejects it, **Tools ▸ HoMM5 ▸
Create Bootstrap Scene** rebuilds it; the scene is one GameObject with
`H5Bootstrap`, so nothing is lost.

Nothing was run in the Unity editor, so no visual claim is made about how the
maps look.

## Not done

- **Game logic is still a prototype.** There is a local player ID and a
  day/week/month counter. The base hero movement points are imported from the
  original stats, but tile costs remain a provisional flat 100 MP. Object
  visits, pickups, mine capture and hero levelling now run from the shipped
  tables where those exist; battles run on reconstructed rules (see below).
  There is still no original multiplayer turn order, terrain/army/artifact
  movement calculation, map-script override execution, spells, abilities,
  town screen, campaigns, AI, Lua runtime or networking. Individual hero
  starting stats are not in the imported tables (heroes start at 1/1/1/1).
- **Combat is a playable reconstruction, not the original.** The luck and
  morale multipliers, defending bonus, siege structure health, war machine
  stats and creature stats are shipped numbers; the attack/defence damage
  coefficients, proc chances, ability probabilities, the 14x10 battle grid
  and the discrete initiative rounds are documented approximations of the
  engine's ATB model. The siege wall layout and bank guard rosters are
  prototype tables (the originals script them per scenario). Secondary
  skills (magic masteries, Tactics, War Machines tiers) are not implemented:
  casts use mastery tier 0, the tactics zone is fixed, machines use tier 0.
  Two-hero battles, hero spellcasting beyond the once-per-round combat cast,
  battlefield obstacles (fire wall, land mines) and creature-activated
  specials (battle dive, gating, seduce) remain stubs or inert.
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
- **No audio, no UI art, no video.**

## Suggested order from here

1. Get it compiling and open a map — cheapest way to find what the missing
   compile check left behind.
2. Finish material translation: two-sided culling, additive passes, lightmaps and
   visual effects; smoothly blend the imported terrain coverage layers.
3. Secondary skills system (magic masteries, Tactics, War Machines, Logistics
   ...): the combat and movement systems already read the hooks it feeds.
4. Town screen and hero screen; campaigns (Witch Huts and hero levelling
   dialogs expect the skill system behind them).
5. Audio: `music/`, `sound/`, `soundsfx/` are FMOD-packed and untouched.
