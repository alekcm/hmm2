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
        AdventureMapGameState _gameState;
        string _movementKey;
        public float moveUnitsPerSecond = 7f;
        public float MovementPointsRemaining { get; private set; } = AdventureMapGameState.DefaultDailyHeroMovement;
        public bool IsMoving => _moveRoutine != null;

        internal void BindMovement(AdventureMapGameState state, string key, float points)
        {
            _gameState = state;
            _movementKey = key;
            MovementPointsRemaining = points;
        }

        internal void SetMovementPoints(float points)
        {
            MovementPointsRemaining = Mathf.Max(0f, points);
            if (_gameState != null) _gameState.StoreMovement(_movementKey, MovementPointsRemaining);
        }

        /// <summary>Move along the passability path, spending this day's simplified movement allowance.</summary>
        public bool MoveHeroTo(Vector2Int destination)
        {
            if (!IsHero || Def == null || Builder == null || Builder.Terrain == null || _moveRoutine != null ||
                MovementPointsRemaining <= 0.0001f ||
                !Builder.Terrain.IsPassable(destination.x, destination.y)) return false;
            var start = new Vector2Int(Mathf.RoundToInt(Def.x), Mathf.RoundToInt(Def.y));
            var fullPath = H5Pathfinder.FindPath(Builder.Terrain, start, destination);
            if (fullPath.Count == 0) return start == destination;
            var tileBudget = MovementPointsRemaining / AdventureMapGameState.PrototypeMovementPointsPerTile;
            var path = H5Pathfinder.AffordablePrefix(start, fullPath, tileBudget);
            if (path.Count == 0) return false;
            var spent = H5Pathfinder.PathCost(start, path) *
                        AdventureMapGameState.PrototypeMovementPointsPerTile;
            SetMovementPoints(MovementPointsRemaining - spent);
            _moveRoutine = StartCoroutine(FollowPath(path));
            return true;
        }

        IEnumerator FollowPath(List<Vector2Int> path)
        {
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
            }
            _moveRoutine = null;
        }

        public CreatureDef Creature => Database != null ? Database.GetCreature(Def != null ? Def.creature : null) : null;
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
                if (Def.army != null)
                    lines.Add("Army stacks: " + Def.army.Count);
                var dailyMovement = _gameState != null
                    ? _gameState.DailyHeroMovement : AdventureMapGameState.DefaultDailyHeroMovement;
                lines.Add("Movement today: " + MovementPointsRemaining.ToString("0") + " / " +
                          dailyMovement.ToString("0") + " MP (prototype path cost)");
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
                if (!string.IsNullOrEmpty(Def.townType))
                    lines.Add("Faction: " + GameDatabase.PrettifyEnum(Def.townType));
                if (!string.IsNullOrEmpty(Def.owner))
                    lines.Add("Owner: " + GameDatabase.PrettifyEnum(Def.owner));
                if (Def.buildings != null)
                {
                    lines.Add("Buildings: " + Def.buildings.Count);
                    var dailyGold = 0;
                    if (Database != null && Database.TownBuildings != null)
                    {
                        for (var i = 0; i < Def.buildings.Count; i++)
                        {
                            var state = Def.buildings[i];
                            if (state == null) continue;
                            for (var j = 0; j < Database.TownBuildings.Count; j++)
                            {
                                var building = Database.TownBuildings[j];
                                if (building != null && building.type == "TB_TOWN_HALL" &&
                                    string.Equals(building.upgrade, state.upgrade,
                                                  System.StringComparison.OrdinalIgnoreCase))
                                    dailyGold = Mathf.Max(dailyGold, building.incomeGoldPerDay);
                            }
                        }
                    }
                    if (dailyGold > 0) lines.Add("Town Hall income: " + dailyGold + " Gold/day");
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
