using System.Collections;
using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The game-side identity of a placed map object.
    ///
    /// The scene only needs position and mesh; this is what makes the object
    /// answer a question about itself -- what it is, who owns it, how many
    /// creatures are in it -- which is what the HUD and any future combat code
    /// ask.
    /// </summary>
    public class H5MapObject : MonoBehaviour
    {
        public MapObjectDef Def { get; private set; }
        public MapSceneBuilder Builder { get; private set; }
        public GameDatabase Database { get; private set; }

        public string Kind => Def != null ? Def.kind : "";
        public bool IsMonster => Kind == "AdvMapMonster";
        public bool IsTown => Kind == "AdvMapTown";
        public bool IsHero => Kind == "AdvMapHero";
        public bool IsArtifact => Kind == "AdvMapArtifact";

        public void Attach(MapObjectDef def, MapSceneBuilder builder)
        {
            Def = def;
            Builder = builder;
        }

        public void AttachDatabase(GameDatabase db) => Database = db;

        Coroutine _moveRoutine;
        bool _halted;
        AdventureMapGameState _gameState;
        string _movementKey;
        public float moveUnitsPerSecond = 7f;
        public float MovementPointsRemaining { get; private set; } = AdventureMapGameState.DefaultDailyHeroMovement;
        public bool IsMoving => _moveRoutine != null;

        /// <summary>Stable id for save files and state dicts (the map object id).</summary>
        public string StateKey => _movementKey;

        /// <summary>Whether this hero belongs to the locally played player.</summary>
        public bool IsLocalHero => _isLocal;
        bool _isLocal;

        /// <summary>The hero's persistent stats, once bound.</summary>
        public H5HeroState HeroState { get; private set; }

        HoMM5.Anim.H5AnimationPlayer _anim;
        string _idleClip;
        string _moveClip;

        internal void BindMovement(AdventureMapGameState state, string key, float points, bool isLocal)
        {
            _gameState = state;
            _movementKey = key;
            _isLocal = isLocal;
            MovementPointsRemaining = points;
        }

        internal void BindHeroState(H5HeroState state) => HeroState = state;

        /// <summary>Wire up the idle/move clips in the native playable graph.</summary>
        public void AttachAnimation(HoMM5.Anim.H5AnimationPlayer anim, string idleClip, string moveClip)
        {
            _anim = anim;
            _idleClip = idleClip;
            _moveClip = moveClip;
        }

        void SetMoveState(bool moving)
        {
            if (_anim == null) return;
            var key = moving ? _moveClip : _idleClip;
            if (string.IsNullOrEmpty(key)) return;
            _anim.Play(key, 0.25f);
        }

        internal void SetMovementPoints(float points)
        {
            MovementPointsRemaining = Mathf.Max(0f, points);
            if (_gameState != null) _gameState.StoreMovement(_movementKey, MovementPointsRemaining);
        }

        /// <summary>Grant extra movement (fountains, stables, rally flags).</summary>
        public void AddMovement(float bonus)
        {
            if (bonus > 0f) SetMovementPoints(MovementPointsRemaining + bonus);
        }

        /// <summary>
        /// Stop the walk where it stands (a battle started mid-path). The
        /// FollowPath loop notices the flag and unwinds itself.
        /// </summary>
        public void HaltMovement()
        {
            _halted = true;
            _moveRoutine = null;
            SetMoveState(false);
        }

        public H5MovementProfile GetMovementProfile()
        {
            return MovementProfile();
        }

        H5MovementProfile MovementProfile()
        {
            var profile = H5MovementProfile.Default;
            if (HeroState != null)
            {
                profile.terrainPenaltyMultiplier = HeroState.Effects.TerrainPenaltyMultiplier;
                profile.snatch = HeroState.Effects.Snatch;
            }
            return profile;
        }

        /// <summary>Move along the native-cost passability path for this day.</summary>
        public bool MoveHeroTo(Vector2Int destination)
        {
            // Only heroes of the locally played player take orders.
            return MoveHeroTo(destination, false);
        }

        /// <summary>
        /// The computer's heroes walk the same passability path and budget as
        /// the player's, but resolve each step at once: there is no camera on
        /// them and the computer's turn must finish before the player's begins.
        /// </summary>
        internal bool MoveComputerHeroTo(Vector2Int destination)
        {
            return MoveHeroTo(destination, true);
        }

        bool MoveHeroTo(Vector2Int destination, bool computer)
        {
            if (!IsHero || Def == null || Builder == null || Builder.Terrain == null ||
                _moveRoutine != null || MovementPointsRemaining <= 0.0001f ||
                !Builder.Terrain.IsPassable(destination.x, destination.y)) return false;
            if (computer == IsLocalHero) return false;
            var start = new Vector2Int(Mathf.RoundToInt(Def.x), Mathf.RoundToInt(Def.y));
            var profile = MovementProfile();
            var fullPath = H5Pathfinder.FindPath(Builder.Terrain, start, destination, profile);
            if (fullPath.Count == 0) return start == destination;
            var path = H5Pathfinder.AffordablePrefix(start, fullPath, MovementPointsRemaining,
                                                     profile, Builder.Terrain);
            if (path.Count == 0) return false;
            var spent = H5Pathfinder.PathCost(Builder.Terrain, start, path, profile);
            SetMovementPoints(MovementPointsRemaining - spent);
            _halted = false;
            if (computer)
            {
                WalkComputerPath(path);
                return true;
            }
            _moveRoutine = StartCoroutine(FollowPath(path));
            return true;
        }

        void WalkComputerPath(List<Vector2Int> path)
        {
            for (var i = 0; i < path.Count; i++)
            {
                // A fight can remove the hero (lost army) or halt the walk.
                if (Def == null || !gameObject.activeSelf) break;
                var tile = path[i];
                transform.position = Builder.TileToWorld(tile.x, tile.y);
                Def.x = tile.x;
                Def.y = tile.y;
                if (_gameState != null) _gameState.HeroEnteredTile(this, tile);
                if (_halted) break;
            }
        }

        IEnumerator FollowPath(List<Vector2Int> path)
        {
            SetMoveState(true);
            for (var i = 0; i < path.Count; i++)
            {
                var tile = path[i];
                var target = Builder.TileToWorld(tile.x, tile.y);
                while ((transform.position - target).sqrMagnitude > 0.01f)
                {
                    transform.position = Vector3.MoveTowards(transform.position, target,
                        Mathf.Max(0.1f, moveUnitsPerSecond) * Time.deltaTime);
                    yield return null;
                }
                transform.position = target;
                Def.x = tile.x;
                Def.y = tile.y;
                if (_gameState != null) _gameState.HeroEnteredTile(this, tile);
                if (_halted) yield break;   // halted (e.g. a battle started)
            }
            SetMoveState(false);
            _moveRoutine = null;
        }

        public CreatureDef Creature => Database != null ? Database.GetCreature(Def != null ? Def.creature : null) : null;

        /// <summary>Set by the adventure map so a town can report its live state.</summary>
        public H5TownState Town { get; internal set; }
        public ArtifactDef Artifact => Database != null ? Database.GetArtifact(Def != null ? Def.artifact : null) : null;

        /// <summary>One-line summary for the HUD.</summary>
        public string Summary()
        {
            if (Def == null) return "";
            switch (Kind)
            {
                case "AdvMapMonster":
                {
                    var c = Creature;
                    var name = c != null ? c.DisplayName : Def.creature;
                    var count = Def.customAmount ? Def.amount.ToString() : "?";
                    return $"{name} x{count}";
                }
                case "AdvMapTown":
                    return string.IsNullOrEmpty(Def.name)
                        ? (string.IsNullOrEmpty(Def.townType) ? "Town" : GameDatabase.PrettifyEnum(Def.townType))
                        : Def.name;
                case "AdvMapHero":
                    return Def.hero ?? "Hero";
                case "AdvMapArtifact":
                {
                    var a = Artifact;
                    return a != null ? a.DisplayName : (Def.artifact ?? "Artifact");
                }
                case "AdvMapMine":
                    return (Def.resource ?? "Mine") + (string.IsNullOrEmpty(Def.owner) ? "" : " (" + Def.owner + ")");
                case "AdvMapTreasure":
                    return "Treasure";
                default:
                    if (!string.IsNullOrEmpty(Def.name)) return Def.name;
                    return !string.IsNullOrEmpty(Def.sharedType)
                        ? GameDatabase.PrettifyEnum(Def.sharedType) : Kind;
            }
        }

        /// <summary>Multi-line detail for the inspector panel.</summary>
        public string Details()
        {
            if (Def == null) return "";
            var c = Creature;
            if (IsMonster && c != null)
            {
                return $"{c.DisplayName}\n" +
                       $"Tier {c.CreatureTier}  {GameDatabase.PrettifyEnum(c.town)}\n" +
                       $"Attack {c.AttackSkill}  Defence {c.DefenceSkill}\n" +
                       $"Damage {c.DamageText}  Health {c.Health}\n" +
                       $"Speed {c.Speed}  Initiative {c.Initiative}\n" +
                       $"Cost {c.cost}\n" +
                       (string.IsNullOrEmpty(c.Description) ? "" : "\n" + c.Description);
            }

            var a = Artifact;
            if (IsArtifact && a != null)
            {
                return $"{a.DisplayName}\n" +
                       $"{GameDatabase.PrettifyEnum(a.artifactClass)}  slot {GameDatabase.PrettifyEnum(a.slot)}\n" +
                       $"Value {a.price} gold\n" +
                       (string.IsNullOrEmpty(a.description) ? "" : "\n" + a.description);
            }

            if (IsHero)
            {
                var lines = new System.Collections.Generic.List<string> { Summary() };
                if (!string.IsNullOrEmpty(Def.owner))
                    lines.Add("Owner: " + GameDatabase.PrettifyEnum(Def.owner));
                if (HeroState != null)
                {
                    lines.Add(HeroState.Summary());
                    if (HeroState.Artifacts.Count > 0)
                        lines.Add("Artifacts: " + HeroState.Artifacts.Count);
                    if (HeroState.Spells.Count > 0)
                        lines.Add("Spells: " + HeroState.Spells.Count);
                    if (HeroState.KeyColors.Count > 0)
                        lines.Add("Keys: " + string.Join(", ", HeroState.KeyColors));
                }
                if (Def.army != null)
                    lines.Add("Army stacks: " + Def.army.Count);
                var dailyMovement = _gameState != null
                    ? _gameState.DailyHeroMovement : AdventureMapGameState.DefaultDailyHeroMovement;
                lines.Add("Movement today: " + MovementPointsRemaining.ToString("0") + " / " +
                          dailyMovement.ToString("0") + " MP (native terrain cost)");
                return string.Join("\n", lines);
            }

            if (Kind == "AdvMapDwelling")
                return GameDatabase.PrettifyEnum(Def.sharedType) + "\n" +
                       "Owner: " + GameDatabase.PrettifyEnum(Def.owner) + "\n" +
                       "Available creatures: " + (Def.creatures != null ? Def.creatures.Count : 0);

            if (Kind == "AdvMapGarrison" || Kind == "AdvMapDwarvenWarren")
                return GameDatabase.PrettifyEnum(Def.sharedType) + "\n" +
                       "Owner: " + GameDatabase.PrettifyEnum(Def.owner) + "\n" +
                       "Army stacks: " + (Def.garrison != null ? Def.garrison.Count : 0);

            if (Kind == "AdvMapMine")
                return "Mine: " + GameDatabase.PrettifyEnum(Def.resource) + "\n" +
                       "Owner: " + GameDatabase.PrettifyEnum(Def.owner);

            if (Kind == "AdvMapCartographer") return Summary() + "\nCost: " + Def.cost + " gold";
            if (Kind == "AdvMapShrine") return Summary() + "\nSpell: " + GameDatabase.PrettifyEnum(Def.spell);
            if (Kind == "AdvMapTent") return Summary() + "\nKey color: " + GameDatabase.PrettifyEnum(Def.keyColor);
            if (Kind == "AdvMapPrison")
                return Summary() + "\n" + (Def.randomHero ? "Random hero" : "Hero: " + Def.prisonedHero);
            if (Kind == "AdvMapSphinx")
                return Def.randomRiddle ? Summary() + "\nRandom riddle" : Summary() + "\n" + Def.riddle;
            if (Kind == "AdvMapSign" && !string.IsNullOrEmpty(Def.message))
                return Summary() + "\n" + Def.message;

            if (Kind == "AdvMapTown")
            {
                var lines = new System.Collections.Generic.List<string>();
                lines.Add(Summary());
                var town = Town;
                if (town != null)
                {
                    // The live town state knows the faction a random town rolled,
                    // the fort tier the player has raised and what the dwellings
                    // have bred since the last recruitment.
                    var type = Database != null ? Database.GetTownType(town.TownType) : null;
                    lines.Add("Faction: " + (type != null ? type.DisplayName
                                                : GameDatabase.PrettifyEnum(town.TownType)));
                    if (!string.IsNullOrEmpty(town.Owner))
                        lines.Add("Owner: " + GameDatabase.PrettifyEnum(town.Owner));
                    var built = 0;
                    foreach (var slot in town.Slots)
                        if (slot != null && slot.Tier > 0) built++;
                    lines.Add($"Buildings: {built} of {town.Slots.Count}   Fort tier {town.FortTier}");
                    var waiting = 0;
                    foreach (var kv in town.Pools) waiting += kv.Value;
                    if (waiting > 0) lines.Add("Creatures waiting: " + waiting);
                    lines.Add("Press T for the town screen.");
                }
                else
                {
                    if (!string.IsNullOrEmpty(Def.townType))
                        lines.Add("Faction: " + GameDatabase.PrettifyEnum(Def.townType));
                    if (!string.IsNullOrEmpty(Def.owner))
                        lines.Add("Owner: " + GameDatabase.PrettifyEnum(Def.owner));
                    if (Def.buildings != null) lines.Add("Buildings: " + Def.buildings.Count);
                }
                if (Def.garrison != null && Def.garrison.Count > 0)
                    lines.Add("Garrison stacks: " + Def.garrison.Count);
                if (!string.IsNullOrEmpty(Def.garrisonHero))
                    lines.Add("Garrison hero: " + Def.garrisonHero);
                return string.Join("\n", lines);
            }

            if (Kind == "AdvMapTreasure")
                return Def.isCustom ? $"Treasure\nAmount: {Def.amount}\n{Def.message}" : "Treasure";

            return Summary();
        }
    }
}
