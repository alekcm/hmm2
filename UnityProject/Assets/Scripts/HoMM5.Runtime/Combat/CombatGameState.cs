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

        readonly List<CombatUnit> _order = new List<CombatUnit>();
        int _orderIndex;
        bool _extraActionPending;

        public event Action Changed;
        public event Action TurnStarted;
        public event Action BattleEnded;

        // -- setup ---------------------------------------------------------

        public static CombatGameState Create(H5MapObject heroObj, H5MapObject monsterObj,
                                             HoMM5.Data.GameDatabase db, System.Random rng)
        {
            var state = new CombatGameState { Rand = rng ?? new System.Random(12345) };
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

            var def = monsterObj != null ? monsterObj.Def : null;
            if (def != null)
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
            Advance();
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
                    if (UnitAt(x, y) != null) continue;
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
                    if (UnitAt(nx, ny) != null) continue;
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
            Changed?.Invoke();
        }

        /// <summary>One melee or ranged attack with its retaliation.</summary>
        public void ExecuteAttack(CombatUnit attacker, CombatUnit target, bool ranged, bool shooterInMelee)
        {
            if (attacker == null || target == null || IsOver) return;
            if (ranged && attacker.ShotsLeft > 0) attacker.ShotsLeft--;

            var outcome = CombatRules.RollAttack(Rand, attacker, target, ranged, shooterInMelee);
            var luckText = outcome.GoodLuck ? " (good luck!)" : outcome.BadLuck ? " (bad luck!)" : "";
            AddLog($"{attacker.DisplayName} hits {target.DisplayName} for {outcome.Damage} damage, " +
                   $"{outcome.CreaturesKilled} killed{luckText}.");

            if (!target.IsAlive)
            {
                AddLog($"{target.DisplayName} is destroyed.");
                Changed?.Invoke();
                return;
            }

            // Retaliation: once per round, melee only, and never if the
            // defender is a shooter with a ranged preference.
            if (!ranged && !target.RetaliatedThisRound && target.IsAlive && attacker.IsAlive)
            {
                target.RetaliatedThisRound = true;
                var back = CombatRules.RollAttack(Rand, target, attacker, false, false);
                AddLog($"{target.DisplayName} retaliates for {back.Damage}, {back.CreaturesKilled} killed.");
                if (!attacker.IsAlive)
                    AddLog($"{attacker.DisplayName} is destroyed.");
            }
            Changed?.Invoke();
        }

        /// <summary>Move to an attack cell and strike in one order.</summary>
        public void MoveAndAttack(CombatUnit attacker, CombatUnit target, bool ranged)
        {
            if (attacker == null || target == null || IsOver) return;
            if (ranged)
            {
                ExecuteAttack(attacker, target, true, HasEnemyAdjacent(attacker));
                return;
            }

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
                MoveUnit(attacker, best.x, best.y);
            }
            ExecuteAttack(attacker, target, false, false);
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
        /// Greedy battle AI: shoot the juiciest target when shooting is free,
        /// otherwise walk at the weakest reachable enemy and hit it; defend in
        /// place when there is nothing useful to do.
        /// </summary>
        public CombatOrder ChooseAiOrder(CombatUnit unit)
        {
            var order = new CombatOrder();
            if (unit == null || !unit.IsAlive) return order;

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

        /// <summary>Resolve the whole battle headless (quick combat).</summary>
        public CombatResult RunQuickCombat()
        {
            BeginBattle();
            var guard = 0;
            while (!IsOver && guard++ < 2000)
            {
                var unit = Active;
                if (unit == null) break;
                var order = ChooseAiOrder(unit);
                if (order.Defend) DoDefend();
                else if (order.Target != null) MoveAndAttack(unit, order.Target, order.Ranged);
                else if (order.MoveX >= 0) MoveUnit(unit, order.MoveX, order.MoveY);
                if (!IsOver) EndTurn();
            }
            return Result;
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
                    leader.AddExperience(result.Experience);
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
