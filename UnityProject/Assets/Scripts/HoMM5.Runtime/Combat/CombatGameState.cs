using System;
using System.Collections.Generic;
using HoMM5.Map;
using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>
    /// The battlefield: a grid of square cells (the shipped engine's Cell type
    /// is a plain {x,y} pair), two armies, and an initiative turn loop.
    ///
    /// The original runs on an ATB gauge (DefaultStats: ATBBaseTime,
    /// GoodMorale_ATBValue 0.5 ...); what is reconstructed here is the
    /// discrete equivalent: each round units act in initiative order, Wait
    /// pushes a unit to the end of the round, good morale grants an extra
    /// action and bad morale skips one. Exact ATB timing is not claimed.
    /// </summary>
    public class CombatGameState
    {
        /// <summary>Prototype battle grid. The engine's exact cell counts per
        /// arena type are not yet reconstructed from the executable.</summary>
        public const int GridWidth = 14;
        public const int GridHeight = 10;

        public readonly List<CombatUnit> Units = new List<CombatUnit>();
        public readonly List<string> Log = new List<string>();

        public int Round { get; private set; } = 1;
        public CombatUnit Active { get; private set; }
        public bool IsOver { get; private set; }
        public CombatResult Result { get; private set; }
        public System.Random Rand { get; private set; }

        /// <summary>Siege works of this battle, null for field fights.</summary>
        public SiegeState Siege { get; private set; }

        /// <summary>Hero casts once per round.</summary>
        public bool SpellCastThisRound { get; set; }

        /// <summary>Deployment phase before the first round.</summary>
        public bool TacticsActive { get; private set; }

        /// <summary>
        /// Number of columns the tactics phase allows rearranging: the two
        /// default columns plus one per Tactics perk (skill text).
        /// </summary>
        public int TacticsColumns { get; private set; } = 2;

        HoMM5.Data.GameDatabase _db;

        readonly List<CombatUnit> _order = new List<CombatUnit>();
        int _orderIndex;
        bool _extraActionPending;

        public event Action Changed;
        public event Action TurnStarted;
        public event Action BattleEnded;

        /// <summary>
        /// Every event a viewer should show or play, in the order it happened, each
        /// raised after the state has changed. Headless fights raise nothing: the
        /// controller clears <see cref="Presenting"/> while quick combat runs.
        /// </summary>
        public event Action<CombatVisual> Presented;

        /// <summary>False while a fight resolves without a viewer (quick combat).</summary>
        public bool Presenting { get; set; } = true;

        void Present(CombatVisual visual)
        {
            if (Presenting && Presented != null && visual != null) Presented.Invoke(visual);
        }

        // -- setup ---------------------------------------------------------

        public static CombatGameState Create(H5MapObject heroObj, H5MapObject monsterObj,
                                             HoMM5.Data.GameDatabase db, System.Random rng,
                                             List<ArmyStackState> guardOverride = null)
        {
            var state = new CombatGameState { Rand = rng ?? new System.Random(12345), _db = db };
            var heroState = heroObj != null ? heroObj.HeroState : null;

            var id = 0;
            var attackerArmies = heroState != null && heroState.Army != null && heroState.Army.Count > 0
                ? heroState.Army
                : null;
            if (attackerArmies != null)
            {
                foreach (var stack in attackerArmies)
                {
                    if (stack == null || stack.Count <= 0 || string.IsNullOrEmpty(stack.CreatureId)) continue;
                    state.AddUnit(CombatSide.Attacker, stack.CreatureId, stack.Count, db, heroState, id++);
                }
            }
            // The hero's war machines ride into battle beside the army.
            if (heroState != null && heroState.WarMachines != null)
                foreach (var machine in heroState.WarMachines)
                    state.AddWarMachine(CombatSide.Attacker, machine, db, heroState, id++);
            if (heroState != null)
                state.TacticsColumns = 2 + heroState.Effects.TacticsExtraColumns;

            var def = monsterObj != null ? monsterObj.Def : null;
            if (guardOverride != null && guardOverride.Count > 0)
            {
                // Guard table (banks): the whole defending force comes in as
                // explicit stacks.
                foreach (var guard in guardOverride)
                    if (guard != null && guard.Count > 0 && !string.IsNullOrEmpty(guard.CreatureId))
                        state.AddUnit(CombatSide.Defender, guard.CreatureId, guard.Count, db, null, id++);
            }
            else if (def != null && def.garrison != null &&
                     def.garrison.Count > 0)
            {
                // Garrison (town, garrison post, dwarven warren): fight the
                // posted stacks.
                foreach (var stack in def.garrison)
                    if (stack != null && !string.IsNullOrEmpty(stack.creature))
                        state.AddUnit(CombatSide.Defender, stack.creature,
                                      Mathf.Max(1, stack.count > 0 ? stack.count : stack.amount),
                                      db, null, id++);
            }
            else if (def != null)
            {
                var count = def.customAmount || def.amount2 <= def.amount
                    ? Mathf.Max(1, def.amount)
                    : state.Rand.Next(Mathf.Max(1, def.amount), def.amount2 + 1);
                state.AddUnit(CombatSide.Defender, def.creature, count, db, null, id++);
                if (def.additionalStacks != null)
                    foreach (var extra in def.additionalStacks)
                        if (extra != null && !string.IsNullOrEmpty(extra.creature))
                            state.AddUnit(CombatSide.Defender, extra.creature,
                                          Mathf.Max(1, extra.amount), db, null, id++);
            }
            state.PlaceArmies();
            return state;
        }

        public void AddUnit(CombatSide side, string creatureId, int count,
                            HoMM5.Data.GameDatabase db, H5HeroState leader, int id)
        {
            var def = db != null ? db.GetCreature(creatureId) : null;
            var unit = new CombatUnit
            {
                Id = id,
                Side = side,
                CreatureId = creatureId ?? "",
                Def = def,
                Count = Mathf.Max(1, count),
                StartCount = Mathf.Max(1, count),
                HealthLeft = def != null && def.Health > 0 ? def.Health : 1,
                ShotsLeft = def != null ? def.Shots : 0,
                Leader = leader,
            };
            Units.Add(unit);
        }

        /// <summary>Add a ballista/catapult/aid tent/ammo cart to a side.</summary>
        public void AddWarMachine(CombatSide side, string machineId,
                                  HoMM5.Data.GameDatabase db, H5HeroState leader, int id)
        {
            if (string.IsNullOrEmpty(machineId)) return;
            var def = db != null && db.WarMachines != null &&
                      db.WarMachines.TryGetValue(machineId, out var wm) ? wm : null;
            var unit = new CombatUnit
            {
                Id = id,
                Side = side,
                UnitType = CombatUnitType.WarMachine,
                WarMachineId = machineId,
                CreatureId = machineId,
                Count = 1,
                StartCount = 1,
                HealthLeft = def != null ? def.health : 100,
                Def = null,
                ShotsLeft = def != null ? def.shots : 0,
                Leader = leader,
            };
            if (def != null)
            {
                // Ballista damage runs on the hero's attack via factors; keep
                // the table's own offence as the machine's combat stat. The
                // tier column is the hero's War Machines mastery (0..3).
                unit.WarMachineTier = leader != null ? leader.Effects.WarMachineTier : 0;
                var tier = Mathf.Clamp(unit.WarMachineTier, 0, Mathf.Max(0, def.offence.Count - 1));
                if (def.offence != null && def.offence.Count > 0)
                    unit.WarMachineAttack = def.offence[tier];
                if (def.minDamageFactor != null && def.minDamageFactor.Count > 0)
                    unit.WarMachineMinFactor = def.minDamageFactor[tier];
                if (def.maxDamageFactor != null && def.maxDamageFactor.Count > 0)
                    unit.WarMachineMaxFactor = Mathf.Max(unit.WarMachineMinFactor, def.maxDamageFactor[tier]);

                // Shipped health bonuses: +HealthBonusPerSkillTrained per
                // trained rank, doubled when the hero owns the machine perk.
                var rules = db != null && db.Rules != null ? db.Rules.skillEffects : null;
                if (rules != null && leader != null)
                {
                    var hp = def.health + rules.warMachinesHealthPerSkill * unit.WarMachineTier;
                    if (MachinePerkOwned(leader, machineId))
                        hp = Mathf.RoundToInt(hp * rules.warMachinesPerkHealthMultiplier);
                    unit.HealthLeft = hp;
                }
            }
            Units.Add(unit);
        }

        /// <summary>The machine-control perk (Ballista/Catapult/First Aid) doubles machine health.</summary>
        static bool MachinePerkOwned(H5HeroState leader, string machineId)
        {
            if (leader == null) return false;
            switch (machineId)
            {
                case "WAR_MACHINE_BALLISTA": return leader.HasPerk("HERO_SKILL_BALLISTA") || leader.HasPerk("HERO_SKILL_TRIPLE_BALLISTA");
                case "WAR_MACHINE_CATAPULT": return leader.HasPerk("HERO_SKILL_CATAPULT") || leader.HasPerk("HERO_SKILL_TRIPLE_CATAPULT");
                case "WAR_MACHINE_FIRST_AID_TENT": return leader.HasPerk("HERO_SKILL_FIRST_AID") || leader.HasPerk("HERO_SKILL_LAST_AID");
                default: return false;
            }
        }

        /// <summary>First aid tent: mend the most wounded friendly stack.</summary>
        public void ExecuteAid(CombatUnit tent, CombatUnit patient)
        {
            if (tent == null || patient == null || !patient.IsAlive) return;
            // Inferred: the tent heals one to two creatures' worth of health.
            var heal = patient.MaxHealth * (patient.Count < patient.StartCount ? 2 : 1);
            var revived = CombatAbilities.HealStack(patient, heal);
            AddLog($"The First Aid Tent tends {patient.DisplayName}" +
                   (revived > 0 ? $", reviving {revived}." : $", healing {heal}."));
            Present(new CombatVisual { Kind = CombatVisualKind.Aid, Source = tent, Target = patient, Side = patient.Side });
            Changed?.Invoke();
        }

        // -- siege ---------------------------------------------------------

        /// <summary>
        /// Build the siege works for a fortified defender. Fort level comes
        /// from the town's buildings (0 none ... 3 Castle). Structure health is
        /// ArenaBuildingsStats; the wall line is a prototype layout (three
        /// segments plus gate) since per-arena wall coordinates are still
        /// locked in the executable.
        /// </summary>
        public void SetupSiege(int fortLevel)
        {
            Siege = new SiegeState { FortLevel = Mathf.Clamp(fortLevel, 0, 3) };
            var wallX = GridWidth - 3;
            var gateRows = new HashSet<int> { GridHeight / 2 - 1, GridHeight / 2 };
            var towerRows = new Dictionary<int, string>
            {
                { 0, SiegeStructure.LeftTower },
                { GridHeight / 2, SiegeStructure.BigTower },
                { GridHeight - 1, SiegeStructure.RightTower },
            };

            SiegeStructure Make(string kind, int x, int y)
            {
                var hp = StructureHealth(kind);
                var s = new SiegeStructure { Kind = kind, Health = hp, MaxHealth = hp, X = x, Y = y };
                s.Cells.Add(new Vector2Int(x, y));
                Siege.Structures.Add(s);
                return s;
            }

            // One structure per cell along the defender's wall line: walls,
            // a two-cell gate in the middle, towers at the ends and center.
            for (var y = 0; y < GridHeight; y++)
            {
                if (gateRows.Contains(y)) Make(SiegeStructure.Gate, wallX, y);
                else if (towerRows.TryGetValue(y, out var towerKind)) Make(towerKind, wallX, y);
                else Make(SiegeStructure.Wall, wallX, y);
            }

            // The moat covers the wall line on the attacker's side.
            for (var y = 0; y < GridHeight; y++)
                Siege.MoatCells.Add(new Vector2Int(wallX - 1, y));

            // The attacker brings the catapult (the defender's towers shoot).
            // It rides under the attacker's hero so War Machines mastery
            // applies to its stats.
            var attackerHero = AttackerLeader;
            AddWarMachine(CombatSide.Attacker, "WAR_MACHINE_CATAPULT", _db, attackerHero, Units.Count + 1);

            // Death Tread: the besieging army fights faster on castle walls.
            if (attackerHero != null && attackerHero.Effects.SiegeSpeedBonus > 0)
                foreach (var u in Units)
                    if (u.Side == CombatSide.Attacker) u.ContextSpeedBonus += attackerHero.Effects.SiegeSpeedBonus;
            PlaceArmies();
        }

        /// <summary>The attacking hero, if any leads the army.</summary>
        public H5HeroState AttackerLeader
        {
            get
            {
                foreach (var u in Units)
                    if (u.Side == CombatSide.Attacker && u.Leader != null) return u.Leader;
                return null;
            }
        }

        int StructureHealth(string kind)
        {
            if (_db == null || _db.ArenaBuildings == null ||
                !_db.ArenaBuildings.TryGetValue(kind, out var ab) || ab == null)
                return 200;
            return ab.HealthAtFortLevel(Siege != null ? Siege.FortLevel : 0);
        }

        /// <summary>Cells a structure blocks for walking.</summary>
        public bool BlockedBySiege(int x, int y)
        {
            if (Siege == null) return false;
            var s = Siege.StructureAt(x, y);
            // Everything on the wall line blocks until destroyed (a wrecked
            // tower or wall becomes a breach the army can pour through).
            return s != null && s.IsAlive;
        }

        /// <summary>Hero (or catapult) strikes a siege structure.</summary>
        public void AttackStructure(CombatUnit attacker, SiegeStructure structure)
        {
            if (attacker == null || structure == null || !structure.IsAlive) return;
            var damage = attacker.UnitType == CombatUnitType.WarMachine
                ? 250   // catapult shot: inferred fixed damage
                : attacker.Count * Mathf.Max(1, (attacker.MinDamage + attacker.MaxDamage) / 2);
            structure.Health -= damage;
            AddLog($"{attacker.DisplayName} hits the {PrettyStructure(structure.Kind)} for {damage} " +
                   $"({Mathf.Max(0, structure.Health)}/{structure.MaxHealth}).");
            // War Machines mastery: the catapult can demolish the section
            // outright (30/40/50% by rank -- shipped skill text).
            if (structure.IsAlive && attacker.UnitType == CombatUnitType.WarMachine)
            {
                var chance = attacker.LeaderFx.CatapultDestroyChance;
                if (chance > 0 && Rand.Next(100) < chance)
                {
                    structure.Health = 0;
                    AddLog($"The {PrettyStructure(structure.Kind)} is demolished outright!");
                }
            }
            if (!structure.IsAlive)
                AddLog($"The {PrettyStructure(structure.Kind)} is destroyed!");
            Changed?.Invoke();
        }

        public static string PrettyStructure(string kind)
        {
            switch (kind)
            {
                case SiegeStructure.Wall: return "wall";
                case SiegeStructure.Gate: return "gate";
                case SiegeStructure.LeftTower:
                case SiegeStructure.RightTower: return "arrow tower";
                case SiegeStructure.BigTower: return "main tower";
                case SiegeStructure.Moat: return "moat";
                default: return kind.ToLowerInvariant();
            }
        }

        /// <summary>Moat check on movement: deals damage to whoever ends there.</summary>
        void ApplyMoat(CombatUnit unit)
        {
            if (Siege == null || unit == null || !unit.IsAlive) return;
            if (!Siege.MoatCells.Contains(new Vector2Int(unit.X, unit.Y))) return;
            // Moat: half a stack's top health per crossing (inferred).
            var damage = Mathf.Max(1, unit.MaxHealth / 2);
            CombatRules.ApplyDamage(unit, damage);
            AddLog($"{unit.DisplayName} crosses the moat for {damage} damage.");
            if (!unit.IsAlive) NotifyDeath(unit);
        }

        // -- deaths and summons --------------------------------------------

        /// <summary>Handle a stack's death: enrage, death explosions, auras.</summary>
        public void NotifyDeath(CombatUnit dead)
        {
            if (dead == null || dead.IsAlive) return;
            if (!dead.DeathPresented)
            {
                dead.DeathPresented = true;
                Present(new CombatVisual { Kind = CombatVisualKind.Death, Target = dead, Side = dead.Side });
            }
            AddLog($"{dead.DisplayName} is destroyed.");

            // Enrage: allies gain a notch of fury.
            var allies = Units.FindAll(u => u.Side == dead.Side);
            CombatAbilities.OnFriendlyDeath(allies, dead);

            // Death explosion (Obsidian Gargoyles and friends).
            var blast = CombatAbilities.DeathExplosionDamage(dead);
            if (blast > 0)
            {
                foreach (var u in Units)
                {
                    if (u == null || !u.IsAlive || u == dead) continue;
                    if (Mathf.Abs(u.X - dead.X) > 1 || Mathf.Abs(u.Y - dead.Y) > 1) continue;
                    var dealt = CombatRules.ApplyDamage(u, blast);
                    AddLog($"{dead.DisplayName} explodes: {blast} to {u.DisplayName}, {dealt} killed.");
                    if (!u.IsAlive) NotifyDeath(u);
                }
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Place a summoned stack near the summoning side's front. Returns
        /// null when there is no room left.
        /// </summary>
        public CombatUnit SummonStack(CombatSide side, string creatureId, int totalHealth)
        {
            var def = _db != null ? _db.GetCreature(creatureId) : null;
            var perCreature = def != null && def.Health > 0 ? def.Health : 50;
            var count = Mathf.Max(1, totalHealth / perCreature);

            // Find a free cell on the side's half of the board.
            var startX = side == CombatSide.Attacker ? 1 : GridWidth - 2;
            for (var radius = 0; radius < GridWidth / 2; radius++)
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = startX + (side == CombatSide.Attacker ? dx : -dx);
                var y = GridHeight / 2 + dy;
                if (!InBounds(x, y) || UnitAt(x, y) != null || BlockedBySiege(x, y)) continue;

                var unit = new CombatUnit
                {
                    Id = Units.Count + 100,
                    Side = side,
                    CreatureId = creatureId ?? "",
                    Def = def,
                    Count = count,
                    StartCount = count,
                    HealthLeft = perCreature,
                    Summoned = true,
                };
                unit.X = x;
                unit.Y = y;
                Units.Add(unit);
                AddLog($"{unit.DisplayName} answers the summon.");
                Present(new CombatVisual { Kind = CombatVisualKind.Summon, Side = side, Target = unit });
                return unit;
            }
            return null;
        }

        /// <summary>
        /// Deploy: attackers in the left columns, defenders in the right ones,
        /// both centered vertically. Stack rows spread one apart.
        /// </summary>
        public void PlaceArmies()
        {
            PlaceSide(CombatSide.Attacker, 0);
            PlaceSide(CombatSide.Defender, GridWidth - 1);
        }

        void PlaceSide(CombatSide side, int column)
        {
            var sideUnits = Units.FindAll(u => u.Side == side && u.IsAlive);
            var secondColumn = side == CombatSide.Attacker ? column + 1 : column - 1;
            for (var i = 0; i < sideUnits.Count; i++)
            {
                var u = sideUnits[i];
                if (i < GridHeight)
                {
                    u.X = column;
                    u.Y = (GridHeight / 2 - sideUnits.Count / 2 + i + GridHeight) % GridHeight;
                }
                else
                {
                    u.X = secondColumn;
                    u.Y = (GridHeight / 2 - (sideUnits.Count - GridHeight) / 2 +
                           (i - GridHeight) + GridHeight) % GridHeight;
                }
            }
        }

        // -- turn loop -----------------------------------------------------

        public void BeginBattle()
        {
            Round = 1;
            IsOver = false;
            StartRound();
        }

        void StartRound()
        {
            _order.Clear();
            SpellCastThisRound = false;
            foreach (var u in Units)
                if (u.IsAlive)
                {
                    u.RetaliatedThisRound = false;
                    u.HasActedThisRound = false;
                    u.WaitedThisRound = false;
                    u.Defended = false;
                    _order.Add(u);
                }
            // Initiative order; ties keep list order for determinism.
            _order.Sort((a, b) => b.Initiative.CompareTo(a.Initiative));
            _orderIndex = 0;
            _extraActionPending = false;
            TowersShoot();
            Advance();
        }

        /// <summary>Every surviving arrow tower picks a target at the round's start.</summary>
        void TowersShoot()
        {
            if (Siege == null) return;
            foreach (var tower in Siege.Towers)
            {
                // Towers harry the strongest attacker stack.
                CombatUnit best = null;
                var bestPower = float.MinValue;
                foreach (var u in Units)
                {
                    if (!u.IsAlive || u.Side != CombatSide.Attacker) continue;
                    var power = u.TotalHealth * (float)u.AttackSkill;
                    if (power > bestPower)
                    {
                        bestPower = power;
                        best = u;
                    }
                }
                if (best == null) return;
                // Inferred: a tower shot eats ~8% of a stack's current health.
                var damage = Mathf.Max(1, Mathf.RoundToInt(best.TotalHealth * 0.08f));
                var killed = CombatRules.ApplyDamage(best, damage);
                AddLog($"The {PrettyStructure(tower.Kind)} shoots {best.DisplayName}: " +
                       $"{damage} damage, {killed} killed.");
                if (!best.IsAlive) NotifyDeath(best);
            }
        }

        /// <summary>Move to the next unit that acts.</summary>
        void Advance()
        {
            if (IsOver) return;
            if (!AnyAlive(CombatSide.Attacker) || !AnyAlive(CombatSide.Defender))
            {
                Finish(AnyAlive(CombatSide.Attacker) ? CombatOutcome.AttackerWon : CombatOutcome.DefenderWon);
                return;
            }
            if (_orderIndex >= _order.Count)
            {
                Round++;
                AddLog($"-- Round {Round} --");
                StartRound();
                return;
            }

            Active = _order[_orderIndex];
            if (Active == null || !Active.IsAlive)
            {
                _orderIndex++;
                Advance();
                return;
            }

            // Statuses resolve before the unit may act: poison and burns tick,
            // blind and stun steal the turn, durations wind down.
            if (Active.HasEffect(CombatEffect.Blind))
            {
                AddLog($"{Active.DisplayName} is blinded and misses the turn.");
                SkipAndAdvance(Active);
                return;
            }
            if (Active.HasEffect(CombatEffect.Stun))
            {
                AddLog($"{Active.DisplayName} is stunned and misses the turn.");
                SkipAndAdvance(Active);
                return;
            }
            var turnLog = CombatAbilities.OnTurnStart(Rand, Active);
            if (turnLog != null) AddLog(turnLog);
            if (!Active.IsAlive)
            {
                NotifyDeath(Active);
                _orderIndex++;
                Advance();
                return;
            }
            foreach (var expired in Active.TickEffects())
                if (expired != CombatEffect.Poison)
                    AddLog($"{Active.DisplayName}: {expired} wears off.");

            // Morale: bad skips the turn, good grants an extra action after it.
            var morale = CombatRules.RollMorale(Rand, Active);
            if (morale == MoraleOutcome.Bad)
            {
                AddLog($"{Active.DisplayName}: bad morale, the turn is lost.");
                Active.HasActedThisRound = true;
                _orderIndex++;
                Advance();
                return;
            }
            _extraActionPending = morale == MoraleOutcome.Good;
            if (_extraActionPending)
                AddLog($"{Active.DisplayName}: good morale, an extra action follows!");

            Active.Defended = false;
            TurnStarted?.Invoke();
            Changed?.Invoke();
        }

        bool AnyAlive(CombatSide side)
        {
            for (var i = 0; i < Units.Count; i++)
                if (Units[i].Side == side && Units[i].IsAlive) return true;
            return false;
        }

        /// <summary>The active unit loses its turn (stun/blind); countdown and move on.</summary>
        void SkipAndAdvance(CombatUnit unit)
        {
            unit.HasActedThisRound = true;
            unit.TickEffects();
            _orderIndex++;
            Active = null;
            Advance();
        }

        /// <summary>The active unit is done; go to the next one.</summary>
        public void EndTurn()
        {
            if (Active == null || IsOver) return;
            Active.HasActedThisRound = true;
            if (_extraActionPending && Active.IsAlive)
            {
                // The extra action inserts the unit right where it stands.
                _extraActionPending = false;
                Active.HasActedThisRound = false;
                AddLog($"{Active.DisplayName} acts again.");
                TurnStarted?.Invoke();
                Changed?.Invoke();
                return;
            }
            _extraActionPending = false;
            _orderIndex++;
            Active = null;
            Advance();
        }

        /// <summary>Wait: act after everyone else this round.</summary>
        public void DoWait()
        {
            if (Active == null || Active.WaitedThisRound || IsOver) return;
            Active.WaitedThisRound = true;
            _order.Remove(Active);
            _order.Add(Active);
            // The index now points at what was after the waiter.
            Active = null;
            Advance();
        }

        public void DoDefend()
        {
            if (Active == null || IsOver) return;
            Active.Defended = true;
            AddLog($"{Active.DisplayName} defends (+{Mathf.RoundToInt(CombatRules.DefenceBonusOnDefend * 100)}% defence).");
            Changed?.Invoke();
            EndTurn();
        }

        /// <summary>Retreat: the battle ends, the monster keeps its wounds.</summary>
        public CombatResult DoFlee()
        {
            if (IsOver) return Result;
            AddLog("You retreat from the battle.");
            Finish(CombatOutcome.Fled);
            return Result;
        }

        // -- movement ------------------------------------------------------

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < GridWidth && y < GridHeight;

        public CombatUnit UnitAt(int x, int y)
        {
            for (var i = 0; i < Units.Count; i++)
                if (Units[i].IsAlive && Units[i].X == x && Units[i].Y == y) return Units[i];
            return null;
        }

        public bool IsAdjacent(CombatUnit a, CombatUnit b)
        {
            return a != null && b != null &&
                   Mathf.Abs(a.X - b.X) <= 1 && Mathf.Abs(a.Y - b.Y) <= 1 &&
                   !(a.X == b.X && a.Y == b.Y);
        }

        /// <summary>Cells the unit can reach this turn (occupied cells block).</summary>
        public HashSet<Vector2Int> ReachableCells(CombatUnit unit)
        {
            var result = new HashSet<Vector2Int>();
            if (unit == null || !unit.IsAlive) return result;
            var budget = unit.Speed;

            if (unit.IsFlying)
            {
                // Flyers ignore intervening units; only the landing cell matters.
                for (var y = 0; y < GridHeight; y++)
                for (var x = 0; x < GridWidth; x++)
                {
                    if (UnitAt(x, y) != null || BlockedBySiege(x, y)) continue;
                    if (Mathf.Max(Mathf.Abs(x - unit.X), Mathf.Abs(y - unit.Y)) <= budget)
                        result.Add(new Vector2Int(x, y));
                }
                return result;
            }

            var dist = new int[GridWidth * GridHeight];
            for (var i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;
            var start = unit.Y * GridWidth + unit.X;
            dist[start] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                var cx = at % GridWidth;
                var cy = at / GridWidth;
                var d = dist[at];
                if (d >= budget) continue;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (!InBounds(nx, ny)) continue;
                    var ni = ny * GridWidth + nx;
                    if (UnitAt(nx, ny) != null || BlockedBySiege(nx, ny)) continue;
                    if (dist[ni] <= d + 1) continue;
                    dist[ni] = d + 1;
                    queue.Enqueue(ni);
                }
            }

            for (var y = 0; y < GridHeight; y++)
            for (var x = 0; x < GridWidth; x++)
                if (dist[y * GridWidth + x] <= budget && dist[y * GridWidth + x] != int.MaxValue)
                    result.Add(new Vector2Int(x, y));
            return result;
        }

        /// <summary>
        /// Cells the unit can attack the target from this turn: reachable
        /// cells adjacent to the target, or the current cell if already there.
        /// </summary>
        public List<Vector2Int> AttackCells(CombatUnit unit, CombatUnit target)
        {
            var cells = new List<Vector2Int>();
            if (unit == null || target == null) return cells;
            if (IsAdjacent(unit, target))
            {
                cells.Add(new Vector2Int(unit.X, unit.Y));
                return cells;
            }
            foreach (var c in ReachableCells(unit))
                if (Mathf.Abs(c.x - target.X) <= 1 && Mathf.Abs(c.y - target.Y) <= 1)
                    cells.Add(c);
            return cells;
        }

        // -- actions -------------------------------------------------------

        public void MoveUnit(CombatUnit unit, int x, int y)
        {
            if (unit == null || IsOver) return;
            unit.X = x;
            unit.Y = y;
            ApplyMoat(unit);
            Changed?.Invoke();
        }

        /// <summary>One melee or ranged attack with its retaliation and on-hit effects.</summary>
        public void ExecuteAttack(CombatUnit attacker, CombatUnit target, bool ranged,
                                  bool shooterInMelee, int cellsMoved = 0)
        {
            if (attacker == null || target == null || IsOver) return;
            if (ranged && attacker.ShotsLeft > 0) attacker.ShotsLeft--;

            var outcome = CombatRules.RollAttack(Rand, attacker, target, ranged, shooterInMelee, cellsMoved);
            if (outcome.Missed)
            {
                Present(new CombatVisual
                {
                    Kind = CombatVisualKind.Strike, Source = attacker, Target = target,
                    Side = attacker.Side, Ranged = ranged, Missed = true,
                });
                AddLog($"{target.DisplayName} is incorporeal -- the blow passes through.");
                Changed?.Invoke();
                return;
            }

            var luckText = outcome.GoodLuck ? " (good luck!)" : outcome.BadLuck ? " (bad luck!)" : "";
            Present(new CombatVisual
            {
                Kind = CombatVisualKind.Strike, Source = attacker, Target = target,
                Side = attacker.Side, Ranged = ranged, Damage = outcome.Damage,
            });
            AddLog($"{attacker.DisplayName} hits {target.DisplayName} for {outcome.Damage} damage, " +
                   $"{outcome.CreaturesKilled} killed{luckText}.");

            // Fire shield burns the melee attacker back.
            if (!ranged && outcome.Damage > 0)
            {
                var reflect = CombatAbilities.MeleeReflect(target, outcome.Damage);
                if (reflect > 0)
                {
                    CombatRules.ApplyDamage(attacker, reflect);
                    AddLog($"{target.DisplayName}'s shield burns back for {reflect}.");
                    if (!attacker.IsAlive) NotifyDeath(attacker);
                }
            }

            // Life drain feeds the attacker's stack.
            if (outcome.Damage > 0 && CombatAbilities.HasAbility(attacker, "ABILITY_LIFE_DRAIN"))
            {
                var revived = CombatAbilities.LifeDrainHeal(attacker, outcome.Damage);
                AddLog($"{attacker.DisplayName} drains life" +
                       (revived > 0 ? $", reviving {revived}." : "."));
            }

            // On-hit abilities: poison, blind, bash, curses, marks.
            if (target.IsAlive && outcome.Damage > 0)
            {
                var effect = CombatAbilities.ApplyOnHitEffects(Rand, attacker, target, outcome.Damage);
                if (effect != null) AddLog($"{target.DisplayName} is {effect}.");
            }

            // Sweeping strikes spill onto extra stacks.
            foreach (var extra in CombatAbilities.ExtraTargets(attacker, target, Units))
            {
                if (extra == null || !extra.IsAlive) continue;
                var side = CombatRules.RollAttack(Rand, attacker, extra, ranged, false, 0);
                Present(new CombatVisual
                {
                    Kind = CombatVisualKind.Strike, Source = attacker, Target = extra,
                    Side = attacker.Side, Ranged = ranged, Missed = side.Missed, Damage = side.Damage,
                });
                AddLog($"{attacker.DisplayName} sweeps {extra.DisplayName} for {side.Damage}, " +
                       $"{side.CreaturesKilled} killed.");
                if (!extra.IsAlive) NotifyDeath(extra);
            }

            if (!target.IsAlive)
            {
                AddLog($"{target.DisplayName} is destroyed.");
                NotifyDeath(target);
                Changed?.Invoke();
                return;
            }

            // Strike and return is handled by the move+attack order.

            // Retaliation: one per round, melee; shooters can return fire with
            // Ranged retaliate; unlimited retaliation breaks the one-per-round
            // rule; No enemy retaliation silences it entirely.
            var canRetaliate = !CombatAbilities.BlocksRetaliation(attacker) && attacker.IsAlive;
            if (canRetaliate)
            {
                var usedRetaliation = target.RetaliatedThisRound && !CombatAbilities.AlwaysRetaliates(target);
                var meleeRange = IsAdjacent(target, attacker);
                var returnChannel = (!ranged && meleeRange) ||
                                     (ranged && CombatAbilities.RangedRetaliate(target) && target.IsRanged);
                if (!usedRetaliation && returnChannel && target.IsAlive)
                {
                    target.RetaliatedThisRound = true;
                    var isReturnShot = ranged && target.IsRanged;
                    if (isReturnShot && target.ShotsLeft > 0) target.ShotsLeft--;
                    var back = CombatRules.RollAttack(Rand, target, attacker, isReturnShot, false, retaliation: true);
                    Present(new CombatVisual
                    {
                        Kind = CombatVisualKind.Strike, Source = target, Target = attacker,
                        Side = target.Side, Ranged = isReturnShot, Missed = back.Missed, Damage = back.Damage,
                    });
                    if (back.Missed)
                        AddLog($"{target.DisplayName}'s retaliation passes through.");
                    else
                    {
                        AddLog($"{target.DisplayName} retaliates for {back.Damage}, {back.CreaturesKilled} killed.");
                        if (!attacker.IsAlive) NotifyDeath(attacker);
                    }
                }
            }
            Changed?.Invoke();
        }

        /// <summary>Move to an attack cell and strike in one order.</summary>
        public void MoveAndAttack(CombatUnit attacker, CombatUnit target, bool ranged)
        {
            if (attacker == null || target == null || IsOver) return;
            if (ranged)
            {
                ExecuteAttack(attacker, target, true, HasEnemyAdjacent(attacker), 0);
                return;
            }

            var originX = attacker.X;
            var originY = attacker.Y;
            var moved = 0;
            if (!IsAdjacent(attacker, target))
            {
                var cells = AttackCells(attacker, target);
                if (cells.Count == 0) return;
                // Closest reachable attack cell.
                var best = cells[0];
                var bestDist = int.MaxValue;
                foreach (var c in cells)
                {
                    var d = Mathf.Abs(c.x - attacker.X) + Mathf.Abs(c.y - attacker.Y);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = c;
                    }
                }
                moved = bestDist;
                MoveUnit(attacker, best.x, best.y);
            }
            attacker.CellsMovedLastTurn = moved;
            ExecuteAttack(attacker, target, false, false, moved);

            // Strike and return: spring back after the blow.
            if (moved > 0 && attacker.IsAlive && CombatAbilities.StrikeAndReturn(attacker) &&
                UnitAt(originX, originY) == null)
            {
                MoveUnit(attacker, originX, originY);
                AddLog($"{attacker.DisplayName} strikes and returns.");
            }
        }

        public bool HasEnemyAdjacent(CombatUnit unit)
        {
            for (var i = 0; i < Units.Count; i++)
            {
                var other = Units[i];
                if (other == null || !other.IsAlive || other.Side == unit.Side) continue;
                if (IsAdjacent(unit, other)) return true;
            }
            return false;
        }

        // -- AI ------------------------------------------------------------

        /// <summary>
        /// Greedy battle AI: war machines serve their function, shooters pick
        /// a target when free, everyone else walks at the weakest reachable
        /// enemy and hits it; defend in place when there is nothing to do.
        /// </summary>
        public CombatOrder ChooseAiOrder(CombatUnit unit)
        {
            var order = new CombatOrder();
            if (unit == null || !unit.IsAlive) return order;

            // -- war machines ------------------------------------------
            if (unit.UnitType == CombatUnitType.WarMachine)
            {
                switch (unit.WarMachineId)
                {
                    case "WAR_MACHINE_FIRST_AID_TENT":
                    {
                        // Heal the most wounded friendly creature stack.
                        CombatUnit patient = null;
                        var worst = float.MaxValue;
                        foreach (var u in Units)
                        {
                            if (u == null || !u.IsAlive || u.Side != unit.Side ||
                                u.UnitType != CombatUnitType.Creature) continue;
                            var ratio = (float)u.TotalHealth / Mathf.Max(1, u.StartCount * u.MaxHealth);
                            if (ratio < worst)
                            {
                                worst = ratio;
                                patient = u;
                            }
                        }
                        if (patient != null && worst < 0.999f)
                        {
                            order.Target = patient;
                            order.Aid = true;
                            return order;
                        }
                        order.Defend = true;
                        return order;
                    }
                    case "WAR_MACHINE_CATAPULT":
                    {
                        if (Siege == null)
                        {
                            order.Defend = true;
                            return order;
                        }
                        // Pound the healthiest structure (gate first when up).
                        SiegeStructure structureTarget = null;
                        SiegeStructure gate = null;
                        foreach (var s in Siege.Structures)
                        {
                            if (!s.IsAlive) continue;
                            if (s.Kind == SiegeStructure.Gate) gate = s;
                            if (structureTarget == null || s.Health > structureTarget.Health) structureTarget = s;
                        }
                        structureTarget = gate ?? structureTarget;
                        if (structureTarget != null)
                        {
                            AttackStructure(unit, structureTarget);
                            order.Done = true;
                            return order;
                        }
                        order.Defend = true;
                        return order;
                    }
                    case "WAR_MACHINE_BALLISTA":
                    {
                        if (unit.ShotsLeft <= 0)
                        {
                            order.Defend = true;
                            return order;
                        }
                        CombatUnit prey = null;
                        var best = float.MinValue;
                        foreach (var u in Units)
                        {
                            if (!u.IsAlive || u.Side == unit.Side) continue;
                            var score = u.TotalHealth + u.AttackSkill * 10f;
                            if (score > best)
                            {
                                best = score;
                                prey = u;
                            }
                        }
                        if (prey != null)
                        {
                            order.Target = prey;
                            order.Ranged = true;
                        }
                        return order;
                    }
                    default:
                        order.Defend = true;
                        return order;
                }
            }

            var enemies = Units.FindAll(u => u.IsAlive && u.Side != unit.Side);
            if (enemies.Count == 0) return order;

            if (unit.IsRanged && unit.ShotsLeft > 0 && !HasEnemyAdjacent(unit))
            {
                // Prefer the stack the shot will hurt most (high count, low health).
                CombatUnit best = null;
                var bestScore = float.MinValue;
                foreach (var e in enemies)
                {
                    var score = e.Count * 10f - e.TotalHealth * 0.1f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = e;
                    }
                }
                if (best != null)
                {
                    order.Target = best;
                    order.Ranged = true;
                    return order;
                }
            }

            // Melee: reach the closest enemy; attack if possible.
            CombatUnit target = null;
            var bestDist = int.MaxValue;
            foreach (var e in enemies)
            {
                var d = Mathf.Abs(e.X - unit.X) + Mathf.Abs(e.Y - unit.Y);
                if (d < bestDist)
                {
                    bestDist = d;
                    target = e;
                }
            }
            if (target == null) return order;

            var cells = AttackCells(unit, target);
            if (cells.Count > 0)
            {
                order.Target = target;
                return order;
            }

            // Not reachable this turn: step toward the target and end.
            var reach = ReachableCells(unit);
            var bestCell = new Vector2Int(unit.X, unit.Y);
            var bestCellDist = int.MaxValue;
            foreach (var c in reach)
            {
                var d = Mathf.Abs(c.x - target.X) + Mathf.Abs(c.y - target.Y);
                if (d < bestCellDist)
                {
                    bestCellDist = d;
                    bestCell = c;
                }
            }
            if (bestCellDist < bestDist)
            {
                order.MoveX = bestCell.x;
                order.MoveY = bestCell.y;
            }
            else
            {
                order.Defend = true;
            }
            return order;
        }

        // -- the battle AI's hero ----------------------------------------------

        /// <summary>
        /// The side whose hero the battle AI commands. Set for computer fights;
        /// null when the player commands the battle, so their hero waits for
        /// input.
        /// </summary>
        public CombatSide? AiHeroSide;

        /// <summary>Buffs the AI casts on its strongest stack in the first round.</summary>
        static readonly string[] AiBuffSpells = { "Bless", "Haste", "StoneSkin", "Bloodlust" };

        /// <summary>
        /// The AI's army gives up when the enemy's worth is at least this many
        /// times its own, from the second round on.
        /// </summary>
        public const float AiRetreatRatio = 4f;

        /// <summary>
        /// A stack's worth on the battlefield: its live count times the shipped
        /// creature Power rating (tier + 1 when a record has none).
        /// </summary>
        static float UnitValue(CombatUnit u)
        {
            if (u == null || u.Def == null) return 0f;
            var power = u.Def.Power > 0 ? u.Def.Power : 1 + u.Def.CreatureTier;
            return u.Count * power;
        }

        /// <summary>
        /// Once per round, the AI's hero casts the strongest damage spell it can
        /// afford at the enemy stack worth the most. In the first round, when no
        /// damage spell is castable, it buffs its own strongest stack instead.
        /// Returns true when a spell was cast.
        /// </summary>
        public bool AiCastSpell()
        {
            var hero = AttackerLeader;
            if (hero == null || SpellCastThisRound || _db == null) return false;
            var side = AiHeroSide ?? CombatSide.Attacker;

            CombatUnit enemy = null;
            CombatUnit friend = null;
            foreach (var u in Units)
            {
                if (u == null || !u.IsAlive || u.UnitType != CombatUnitType.Creature) continue;
                if (u.Side == side)
                {
                    if (friend == null || UnitValue(u) > UnitValue(friend)) friend = u;
                }
                else if (enemy == null || UnitValue(u) > UnitValue(enemy))
                {
                    enemy = u;
                }
            }

            HoMM5.Data.SpellDef spell = null;
            CombatUnit target = null;
            if (enemy != null)
            {
                var bestCost = -1;
                foreach (var id in hero.Spells)
                {
                    var candidate = _db.GetSpell(id);
                    if (!CombatSpells.IsDamageSpell(candidate)) continue;
                    if (!CombatSpells.CanCast(hero, candidate, (int)hero.Mana, out _)) continue;
                    var cost = CombatSpells.ManaCost(hero, candidate);
                    if (cost > bestCost)
                    {
                        bestCost = cost;
                        spell = candidate;
                        target = enemy;
                    }
                }
            }
            if (spell == null && friend != null && Round <= 1)
            {
                var bestCost = -1;
                foreach (var id in hero.Spells)
                {
                    if (System.Array.IndexOf(AiBuffSpells, id) < 0) continue;
                    var candidate = _db.GetSpell(id);
                    if (candidate == null || !CombatSpells.CanCast(hero, candidate, (int)hero.Mana, out _)) continue;
                    var cost = CombatSpells.ManaCost(hero, candidate);
                    if (cost > bestCost)
                    {
                        bestCost = cost;
                        spell = candidate;
                        target = friend;
                    }
                }
            }
            if (spell == null || target == null) return false;
            return CastSpell(hero, spell, target, target.X, target.Y) != null;
        }

        /// <summary>
        /// Whether the AI's army should give up: from the second round, when the
        /// enemy outweighs it by <see cref="AiRetreatRatio"/>.
        /// </summary>
        public bool AiShouldRetreat()
        {
            if (Round < 2 || AttackerLeader == null) return false;
            var side = AiHeroSide ?? CombatSide.Attacker;
            float ours = 0f;
            float theirs = 0f;
            foreach (var u in Units)
            {
                if (u == null || !u.IsAlive || u.UnitType != CombatUnitType.Creature) continue;
                if (u.Side == side) ours += UnitValue(u);
                else theirs += UnitValue(u);
            }
            return theirs > 0f && ours * AiRetreatRatio < theirs;
        }

        /// <summary>Resolve the whole battle headless (quick combat).</summary>
        public CombatResult RunQuickCombat()
        {
            BeginBattle();
            var guard = 0;
            while (!IsOver && guard++ < 2000)
            {
                var unit = Active;
                if (unit == null) break;
                if (AiHeroSide.HasValue && unit.Side == AiHeroSide.Value)
                {
                    if (AiShouldRetreat())
                    {
                        AddLog("The computer's army retreats.");
                        Finish(CombatOutcome.Fled);
                        break;
                    }
                    AiCastSpell();
                    if (IsOver) break;
                }
                var order = ChooseAiOrder(unit);
                ExecuteOrder(order, unit);
                if (!IsOver && Active == unit) EndTurn();
            }
            return Result;
        }

        /// <summary>Carry out one AI order headless (quick combat / auto-resolve).</summary>
        public void ExecuteOrder(CombatOrder order, CombatUnit unit)
        {
            if (order == null || unit == null) return;
            if (order.Done) return;
            if (order.Aid && order.Target != null) ExecuteAid(unit, order.Target);
            else if (order.Defend) DoDefend();
            else if (order.Target != null) MoveAndAttack(unit, order.Target, order.Ranged);
            else if (order.MoveX >= 0) MoveUnit(unit, order.MoveX, order.MoveY);
            else DoDefend();
        }

        // -- spells and tactics ---------------------------------------------

        /// <summary>
        /// The player's hero casts one spell per round. Returns the log line,
        /// or null when the cast failed (state.Err explains). Mana is spent on
        /// success and the hero's spell for the round is used up.
        /// </summary>
        public string CastSpell(H5HeroState hero, HoMM5.Data.SpellDef spell,
                                CombatUnit target, int cellX, int cellY)
        {
            Err = null;
            if (hero == null || spell == null)
            {
                Err = "No spell.";
                return null;
            }
            if (SpellCastThisRound)
            {
                Err = "The hero has already cast a spell this round.";
                return null;
            }
            if (!CombatSpells.CanCast(hero, spell, (int)hero.Mana, out var reason))
            {
                Err = reason;
                return null;
            }

            var line = CombatSpells.Cast(this, hero, spell, target, cellX, cellY);
            if (line == null)
            {
                Err = $"{spell.DisplayName} has no implemented effect yet.";
                return null;
            }
            hero.Mana = Mathf.Max(0f, hero.Mana - CombatSpells.ManaCost(hero, spell));
            SpellCastThisRound = true;
            AddLog(line);
            Present(new CombatVisual
            {
                Kind = CombatVisualKind.Spell,
                Side = AttackerLeader == hero ? CombatSide.Attacker : CombatSide.Defender,
                Spell = spell,
                Target = target,
                HasCell = InBounds(cellX, cellY),
                CellX = cellX,
                CellY = cellY,
            });
            return line;
        }

        /// <summary>Why the last CastSpell (or similar) refused; null on success.</summary>
        public string Err;

        /// <summary>
        /// Enter the tactics phase: the player may rearrange their stacks in
        /// the first <see cref="TacticsColumns"/> columns before the first
        /// round. The Tactics perk widens the zone by one column.
        /// </summary>
        public void BeginTactics()
        {
            TacticsActive = true;
            AddLog("Tactics: arrange your army, then begin the battle.");
        }

        public bool CanPlaceInTactics(int x, int y)
        {
            return TacticsActive && InBounds(x, y) && x < TacticsColumns &&
                   UnitAt(x, y) == null && !BlockedBySiege(x, y);
        }

        /// <summary>Move a stack inside the tactics zone (or start from a cell).</summary>
        public bool TacticsMove(CombatUnit unit, int x, int y)
        {
            if (!TacticsActive || unit == null || unit.Side != CombatSide.Attacker) return false;
            if (!CanPlaceInTactics(x, y)) return false;
            unit.X = x;
            unit.Y = y;
            Changed?.Invoke();
            return true;
        }

        /// <summary>End deployment and start the battle.</summary>
        public void CommitTactics()
        {
            if (!TacticsActive) return;
            TacticsActive = false;
            AddLog("The battle begins!");
            Round = 1;
            StartRound();
        }

        // -- result --------------------------------------------------------

        void Finish(CombatOutcome outcome)
        {
            if (IsOver) return;
            IsOver = true;
            Active = null;

            var result = new CombatResult { Outcome = outcome };
            foreach (var u in Units)
            {
                var stack = new ArmyStackState(u.CreatureId, u.IsAlive ? u.Count : 0);
                if (u.Side == CombatSide.Attacker) result.AttackerSurvivors.Add(stack);
                else result.DefenderSurvivors.Add(stack);
            }

            // Experience: the shipped per-creature Exp values, for kills by the
            // winning hero's side. Kill counts are tracked on the units.
            var attacker = Units.Find(u => u.Side == CombatSide.Attacker);
            var leader = attacker != null ? attacker.Leader : null;
            if (outcome == CombatOutcome.AttackerWon && leader != null)
            {
                foreach (var u in Units)
                {
                    if (u.Side != CombatSide.Defender) continue;
                    var exp = u.Def != null ? u.Def.Exp : 0;
                    // Whole stacks died; wounded survivors still paid their top.
                    var killed = u.StartCount - u.Count;
                    if (killed > 0) result.Experience += exp * killed;
                }
                if (result.Experience > 0)
                    leader.AddExperience(result.Experience, _db);
            }
            result.CreaturesKilledByAttacker = CreaturesKilled(CombatSide.Attacker);
            result.Log.AddRange(Log);

            Result = result;
            AddLog(outcome == CombatOutcome.AttackerWon ? "The battle is won!"
                 : outcome == CombatOutcome.DefenderWon ? "The battle is lost."
                 : "The army retreats.");
            BattleEnded?.Invoke();
            Changed?.Invoke();
        }

        int CreaturesKilled(CombatSide bySide)
        {
            var total = 0;
            foreach (var u in Units)
                if (u.Side != bySide) total += u.StartCount - u.Count;
            return total;
        }

        public void AddLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Log.Add(message);
            if (Log.Count > 400) Log.RemoveRange(0, Log.Count - 400);
            Changed?.Invoke();
        }
    }
}
