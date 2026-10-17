using System.Collections;
using System.Collections.Generic;
using HoMM5.Core;
using UnityEngine;

namespace HoMM5.Combat
{
    /// <summary>
    /// Drives one battle: runs the turn loop, turns clicks into orders,
    /// animates moves and blows, and hands the result back when done.
    ///
    /// The attacker is the player; the defender is the greedy battle AI (or
    /// the same AI on both sides during quick combat).
    /// </summary>
    public class CombatController : MonoBehaviour
    {
        public CombatGameState State { get; private set; }
        public CombatSceneBuilder Scene { get; private set; }
        public HoMM5.UI.CombatHud Hud;

        public event System.Action<CombatResult> BattleFinished;

        Camera _cam;
        bool _awaitingPlayerInput;
        bool _busy;
        bool _autoResolving;
        CombatUnitView _selectedView;
        HoMM5.Data.SpellDef _spellTargeting;

        /// <summary>Set while the next click picks a spell target.</summary>
        public bool IsTargetingSpell => _spellTargeting != null;

        public void StartBattle(CombatGameState state, CombatSceneBuilder scene, Camera cam)
        {
            State = state;
            Scene = scene;
            _cam = cam;
            if (State != null)
            {
                State.TurnStarted += OnTurnStarted;
                State.BattleEnded += OnBattleEnded;
                // The player's side deploys first; quick/field fights skip it.
                var playerLeads = State.Units.Exists(u =>
                    u.Side == CombatSide.Attacker && u.Leader != null &&
                    u.UnitType == CombatUnitType.Creature);
                if (playerLeads)
                {
                    State.BeginTactics();
                    _awaitingPlayerInput = true;
                    RefreshHud();
                }
                else
                {
                    State.BeginBattle();
                }
            }
        }

        void OnDestroy()
        {
            if (State != null)
            {
                State.TurnStarted -= OnTurnStarted;
                State.BattleEnded -= OnBattleEnded;
            }
        }

        void Update()
        {
            if (State == null || State.IsOver || _busy) return;
            if (_awaitingPlayerInput) HandlePlayerInput();
        }

        // -- turn driving --------------------------------------------------

        void OnTurnStarted()
        {
            var unit = State.Active;
            if (unit == null) return;

            RefreshHud();
            // While quick combat grinds through the queue there is no player
            // turn and no AI coroutine -- the resolve loop does the work.
            if (_autoResolving) return;

            if (unit.Side == CombatSide.Attacker && unit.Leader != null)
            {
                // Player-controlled: highlight and wait for orders.
                _awaitingPlayerInput = true;
                Scene.SetHighlight(State.ReachableCells(unit), new Color(0.25f, 0.75f, 0.35f, 0.35f));
            }
            else
            {
                _awaitingPlayerInput = false;
                Scene.SetHighlight(null, Color.clear);
                StartCoroutine(AiTurn(unit));
            }
        }

        void OnBattleEnded()
        {
            _awaitingPlayerInput = false;
            if (Scene != null) Scene.SetHighlight(null, Color.clear);
            RefreshHud();
            if (Hud != null && State != null && State.Result != null)
                Hud.ShowResult(State.Result, Finish);
            else
                Finish();
        }

        IEnumerator AiTurn(CombatUnit unit)
        {
            _busy = true;
            yield return new WaitForSeconds(0.45f);
            if (State == null || State.IsOver || State.Active != unit || !unit.IsAlive)
            {
                _busy = false;
                yield break;
            }

            var order = State.ChooseAiOrder(unit);
            if (order.Done)
            {
                // The AI already spent the action (catapult shot); just end.
            }
            else if (order.Aid && order.Target != null)
            {
                State.ExecuteAid(unit, order.Target);
                RefreshCounts();
            }
            else if (order.Defend)
            {
                State.DoDefend();
            }
            else if (order.Target != null)
            {
                if (!order.Ranged && !State.IsAdjacent(unit, order.Target))
                {
                    var cells = State.AttackCells(unit, order.Target);
                    if (cells.Count > 0)
                    {
                        var best = Nearest(cells, unit);
                        yield return MoveView(unit, best.x, best.y);
                        State.MoveUnit(unit, best.x, best.y);
                    }
                }
                yield return AnimateStrike(unit, order.Target);
                State.ExecuteAttack(unit, order.Target, order.Ranged, State.HasEnemyAdjacent(unit));
                RefreshCounts();
            }
            else if (order.MoveX >= 0)
            {
                yield return MoveView(unit, order.MoveX, order.MoveY);
                State.MoveUnit(unit, order.MoveX, order.MoveY);
            }
            else
            {
                State.DoDefend();
                _busy = false;
                yield break;
            }
            if (State != null && !State.IsOver && State.Active == unit) State.EndTurn();
            _busy = false;
        }

        static Vector2Int Nearest(List<Vector2Int> cells, CombatUnit unit)
        {
            var best = cells[0];
            var bestDist = int.MaxValue;
            foreach (var c in cells)
            {
                var d = Mathf.Abs(c.x - unit.X) + Mathf.Abs(c.y - unit.Y);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }
            return best;
        }

        // -- player input --------------------------------------------------

        void HandlePlayerInput()
        {
            if (State.TacticsActive)
            {
                HandleTacticsInput();
                return;
            }
            if (H5Input.PointerOverUi) return;
            var unit = State.Active;
            if (unit == null || unit.Side != CombatSide.Attacker) return;

            if (H5Input.SelectPressed() && _cam != null)
            {
                var ray = _cam.ScreenPointToRay(H5Input.PointerPosition());
                if (Physics.Raycast(ray, out var hit, 3000f))
                {
                    var view = hit.collider.GetComponentInParent<CombatUnitView>();
                    if (_spellTargeting != null)
                    {
                        // Spell targeting: any clicked unit is the target.
                        if (view != null && view.Unit != null)
                            CastAt(view.Unit, view.Unit.X, view.Unit.Y);
                        else if (Scene.TryGetCell(hit.point, out var sx, out var sy))
                            CastAt(null, sx, sy);
                        return;
                    }
                    if (view != null && view.Unit != null)
                    {
                        if (view.Unit.Side != unit.Side) PlayerAttack(unit, view.Unit);
                        else SelectView(view);
                    }
                    else if (Scene.TryGetCell(hit.point, out var cx, out var cy))
                    {
                        // A live wall/gate/tower cell is a strike, not a move.
                        var structure = State.Siege != null ? State.Siege.StructureAt(cx, cy) : null;
                        if (structure != null && structure.IsAlive)
                            PlayerAttackStructure(unit, structure);
                        else
                            PlayerMove(unit, cx, cy);
                    }
                }
            }
        }

        void HandleTacticsInput()
        {
            if (H5Input.PointerOverUi || _cam == null || !H5Input.SelectPressed()) return;
            var ray = _cam.ScreenPointToRay(H5Input.PointerPosition());
            if (!Physics.Raycast(ray, out var hit, 3000f)) return;

            var view = hit.collider.GetComponentInParent<CombatUnitView>();
            if (view != null && view.Unit != null && view.Unit.Side == CombatSide.Attacker)
            {
                SelectView(view);
                return;
            }
            if (_selectedView == null || _selectedView.Unit == null) return;
            if (Scene.TryGetCell(hit.point, out var x, out var y))
            {
                State.TacticsMove(_selectedView.Unit, x, y);
                if (Scene.Views.TryGetValue(_selectedView.Unit.Id, out var moved) && moved != null)
                    moved.SnapTo(Scene.CellToWorld(x, y), moved.transform.eulerAngles.y);
                RefreshHud();
            }
        }

        /// <summary>The HUD hands over the spell to aim.</summary>
        public void BeginSpellTargeting(HoMM5.Data.SpellDef spell)
        {
            _spellTargeting = spell;
            RefreshHud();
        }

        void CastAt(CombatUnit target, int x, int y)
        {
            var spell = _spellTargeting;
            _spellTargeting = null;
            if (spell == null || State == null) return;
            var hero = State.Active != null ? State.Active.Leader : null;
            if (hero == null)
            {
                foreach (var u in State.Units)
                    if (u.Side == CombatSide.Attacker && u.Leader != null) { hero = u.Leader; break; }
            }
            State.CastSpell(hero, spell, target, x, y);
            RefreshCounts();
        }

        void PlayerAttackStructure(CombatUnit unit, SiegeStructure structure)
        {
            if (unit.UnitType == CombatUnitType.WarMachine && unit.WarMachineId != "WAR_MACHINE_CATAPULT")
                return;   // only the catapult and living creatures break walls
            _awaitingPlayerInput = false;
            StartCoroutine(PlayerAttackStructureRoutine(unit, structure));
        }

        System.Collections.IEnumerator PlayerAttackStructureRoutine(CombatUnit unit, SiegeStructure structure)
        {
            _busy = true;
            State.AttackStructure(unit, structure);
            yield return new WaitForSeconds(0.2f);
            if (State != null && !State.IsOver && State.Active == unit) State.EndTurn();
            _busy = false;
        }

        void SelectView(CombatUnitView view)
        {
            _selectedView = view;
            RefreshHud();
        }

        void PlayerMove(CombatUnit unit, int x, int y)
        {
            if (!State.ReachableCells(unit).Contains(new Vector2Int(x, y))) return;
            _awaitingPlayerInput = false;
            StartCoroutine(PlayerMoveRoutine(unit, x, y));
        }

        IEnumerator PlayerMoveRoutine(CombatUnit unit, int x, int y)
        {
            _busy = true;
            yield return MoveView(unit, x, y);
            State.MoveUnit(unit, x, y);
            Scene.SetHighlight(null, Color.clear);
            if (State != null && !State.IsOver && State.Active == unit) State.EndTurn();
            _busy = false;
        }

        void PlayerAttack(CombatUnit unit, CombatUnit target)
        {
            var ranged = unit.IsRanged && unit.ShotsLeft > 0 && !State.HasEnemyAdjacent(unit);
            if (!ranged && State.AttackCells(unit, target).Count == 0) return;
            _awaitingPlayerInput = false;
            StartCoroutine(PlayerAttackRoutine(unit, target, ranged));
        }

        IEnumerator PlayerAttackRoutine(CombatUnit unit, CombatUnit target, bool ranged)
        {
            _busy = true;
            Scene.SetHighlight(null, Color.clear);
            if (!ranged && !State.IsAdjacent(unit, target))
            {
                var cells = State.AttackCells(unit, target);
                if (cells.Count > 0)
                {
                    var best = Nearest(cells, unit);
                    yield return MoveView(unit, best.x, best.y);
                    State.MoveUnit(unit, best.x, best.y);
                }
            }
            yield return AnimateStrike(unit, target);
            State.ExecuteAttack(unit, target, ranged, State.HasEnemyAdjacent(unit));
            RefreshCounts();
            if (State != null && !State.IsOver && State.Active == unit) State.EndTurn();
            _busy = false;
        }

        // -- HUD buttons ---------------------------------------------------

        /// <summary>Tactics phase: the player is done deploying.</summary>
        public void OnBeginBattlePressed()
        {
            if (State == null || !State.TacticsActive || _busy) return;
            State.CommitTactics();
            _awaitingPlayerInput = false;
        }

        public void OnWaitPressed()
        {
            if (State == null || State.IsOver || _busy || !_awaitingPlayerInput) return;
            _awaitingPlayerInput = false;
            Scene.SetHighlight(null, Color.clear);
            State.DoWait();
        }

        public void OnDefendPressed()
        {
            if (State == null || State.IsOver || _busy || !_awaitingPlayerInput) return;
            _awaitingPlayerInput = false;
            Scene.SetHighlight(null, Color.clear);
            State.DoDefend();
        }

        public void OnFleePressed()
        {
            if (State == null || State.IsOver || _busy) return;
            _awaitingPlayerInput = false;
            State.DoFlee();
        }

        /// <summary>Auto-resolve the rest of the battle with the battle AI.</summary>
        public void OnQuickCombatPressed()
        {
            if (State == null || State.IsOver || _busy) return;
            _awaitingPlayerInput = false;
            Scene.SetHighlight(null, Color.clear);
            _autoResolving = true;
            var guard = 0;
            while (!State.IsOver && guard++ < 2000)
            {
                var unit = State.Active;
                if (unit == null) break;
                State.ExecuteOrder(State.ChooseAiOrder(unit), unit);
                if (!State.IsOver && State.Active == unit) State.EndTurn();
            }
            _autoResolving = false;
            RefreshCounts();
        }

        // -- animation -----------------------------------------------------

        IEnumerator MoveView(CombatUnit unit, int x, int y)
        {
            if (Scene.Views.TryGetValue(unit.Id, out var view) && view != null)
            {
                var from = view.transform.position;
                var to = Scene.CellToWorld(x, y);
                var steps = Mathf.Max(1, Mathf.RoundToInt((to - from).magnitude / 2f));
                var path = new Vector3[steps];
                for (var i = 0; i < steps; i++)
                    path[i] = Vector3.Lerp(from, to, (i + 1f) / steps);
                view.MoveAlong(path, 8f);
                while (view.IsMoving) yield return null;
            }
        }

        IEnumerator AnimateStrike(CombatUnit unit, CombatUnit target)
        {
            if (!Scene.Views.TryGetValue(unit.Id, out var view) || view == null ||
                !Scene.Views.TryGetValue(target.Id, out var targetView) || targetView == null)
                yield break;

            var origin = view.transform.position;
            var toward = targetView.transform.position;
            var lunge = Vector3.Lerp(origin, toward, 0.35f);
            var forward = Quaternion.LookRotation(toward - origin);
            view.transform.rotation = forward;

            const float step = 12f;
            while ((view.transform.position - lunge).sqrMagnitude > 0.01f)
            {
                view.transform.position = Vector3.MoveTowards(view.transform.position, lunge,
                    step * Time.deltaTime);
                yield return null;
            }
            while ((view.transform.position - origin).sqrMagnitude > 0.01f)
            {
                view.transform.position = Vector3.MoveTowards(view.transform.position, origin,
                    step * Time.deltaTime);
                yield return null;
            }
            view.transform.position = origin;
        }

        // -- hud -----------------------------------------------------------

        public void RefreshHud()
        {
            if (Hud == null || State == null) return;
            Hud.UpdateState(State, _selectedView != null ? _selectedView.Unit : null);
        }

        void RefreshCounts()
        {
            if (Scene == null) return;
            foreach (var kv in Scene.Views)
            {
                var view = kv.Value;
                if (view == null) continue;
                view.RefreshCount();
                if (view.Unit != null && !view.Unit.IsAlive) view.gameObject.SetActive(false);
            }
            Scene.RefreshSiege();
            RefreshHud();
        }

        public void Finish()
        {
            BattleFinished?.Invoke(State != null ? State.Result : null);
        }
    }
}
