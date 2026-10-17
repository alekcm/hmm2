using System.Collections.Generic;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The computer opponent's turn.
    ///
    /// This is a heuristic player, not the shipped AI -- the original lives in
    /// <c>bin/H5_Game_PEST.exe</c> and in the Lua scenario scripts, neither of
    /// which is decoded here. What it does do is the same set of decisions a
    /// player makes, in the same order, and every number it uses comes from the
    /// imported tables rather than from constants:
    ///
    ///   1. build in each town, cheapest useful building first, respecting the
    ///      shipped prerequisites and the one-building-per-day rule;
    ///   2. recruit everything the dwellings have bred that it can afford;
    ///   3. send each hero at the best target it can reach -- an undefended
    ///      resource pile, a creature stack it should beat, or the enemy town;
    ///   4. attack when adjacent, otherwise walk.
    ///
    /// Deliberately simple and deterministic given the shared
    /// <see cref="System.Random"/>: it is meant to be an opponent you can beat
    /// and learn from, and a place to hang real evaluation later.
    /// </summary>
    public static class H5AiController
    {
        /// <summary>How far a hero will consider a target, in tiles.</summary>
        const float TargetRadius = 48f;

        /// <summary>
        /// Play one full turn for <paramref name="player"/>.
        /// Returns the lines it wants in the log, so the caller decides whether
        /// a human should see them.
        /// </summary>
        public static List<string> PlayTurn(AdventureMapGameState state, H5PlayerState player)
        {
            var log = new List<string>();
            if (state == null || player == null || !player.IsAlive) return log;

            BuildInTowns(state, player, log);
            RecruitInTowns(state, player, log);
            MoveHeroes(state, player, log);
            return log;
        }

        // -- economy --------------------------------------------------------

        static void BuildInTowns(AdventureMapGameState state, H5PlayerState player, List<string> log)
        {
            foreach (var town in state.TownsOf(player))
            {
                if (town == null) continue;
                // One building per day, same rule the player is held to.
                var options = town.Buildable(state.Database);
                if (options == null || options.Count == 0) continue;

                var treasury = state.TreasuryOf(player);
                var pick = ChooseBuild(state, town, options, treasury);
                if (pick == null) continue;

                string reason;
                if (town.Build(state.Database, pick, treasury, out reason))
                    log.Add($"{player.Name}: {town.Name} builds {pick.DisplayName}.");
            }
        }

        /// <summary>
        /// Pick what to build: dwellings first (they are the army), then the
        /// fort line (it decides a siege), then income, then the rest.
        /// Ties go to the cheaper building, so a poor AI still progresses.
        /// </summary>
        static TownBuildingDef ChooseBuild(AdventureMapGameState state, H5TownState town,
                                           List<TownBuildingDef> options,
                                           HoMM5.Data.PlayerResourceInventory treasury)
        {
            TownBuildingDef best = null;
            var bestScore = int.MinValue;
            foreach (var b in options)
            {
                if (b == null) continue;
                if (!H5TownState.CanAfford(treasury, b.cost)) continue;
                var score = BuildPriority(b) * 10000 - TotalCost(b.cost);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }
            }
            return best;
        }

        static int BuildPriority(TownBuildingDef b)
        {
            if (b == null || b.type == null) return 0;
            if (b.type.StartsWith("TB_DWELLING")) return 6;   // army first
            if (b.type == "TB_GRAIL") return 5;
            if (b.type == "TB_FORT") return 4;
            if (b.type == "TB_TOWN_HALL") return 3;           // income
            if (b.type == "TB_MARKETPLACE") return 2;         // resource silo
            return 1;
        }

        static int TotalCost(ResourceCost cost)
        {
            if (cost == null) return 0;
            return cost.Wood + cost.Ore + cost.Mercury + cost.Crystal +
                   cost.Sulfur + cost.Gem + cost.Gold / 100;
        }

        static void RecruitInTowns(AdventureMapGameState state, H5PlayerState player, List<string> log)
        {
            foreach (var town in state.TownsOf(player))
            {
                if (town == null) continue;
                var army = state.ArmyOf(player);
                if (army == null) continue;

                foreach (var dwelling in town.Dwellings(state.Database))
                {
                    if (dwelling == null) continue;
                    foreach (var creatureId in town.Recruitables(state.Database, dwelling.type))
                    {
                        // 0 means "take the whole pool"; the game state applies
                        // the cost and the army-slot limit.
                        var taken = state.RecruitFor(player, town, dwelling.type, creatureId, 0);
                        if (taken > 0)
                            log.Add($"{player.Name}: {town.Name} fields {taken} " +
                                    $"{state.Database.CreatureName(creatureId)}.");
                    }
                }
            }
        }

        // -- movement and aggression ---------------------------------------

        static void MoveHeroes(AdventureMapGameState state, H5PlayerState player, List<string> log)
        {
            foreach (var hero in state.HeroesOf(player))
            {
                if (hero == null || hero.Def == null || !hero.gameObject.activeSelf) continue;
                if (hero.MovementPointsRemaining <= 0f) continue;

                var target = ChooseTarget(state, player, hero);
                if (target == null) continue;

                var here = new Vector2Int(Mathf.RoundToInt(hero.Def.x), Mathf.RoundToInt(hero.Def.y));
                // Already standing on one of the target's active tiles: the visit
                // is what a step onto it would have done, so make it now.
                if (H5ObjectVisits.IsActivationTile(target, here))
                {
                    if (H5ObjectVisits.TryVisit(state, hero, target))
                        log.Add($"{player.Name}: {HeroName(hero)} engages the {DisplayName(target)}.");
                    continue;
                }

                // Otherwise walk to the nearest reachable active tile. Monsters and
                // towns sit on blocked tiles, so the walk ends beside them and the
                // visit fires on the step that lands on an active tile.
                var terrain = hero.Builder != null ? hero.Builder.Terrain : null;
                if (terrain == null) continue;
                List<Vector2Int> bestPath = null;
                Vector2Int best = here;
                foreach (var tile in ActivationTiles(target))
                {
                    if (!terrain.IsPassable(tile.x, tile.y)) continue;
                    var path = H5Pathfinder.FindPath(terrain, here, tile);
                    if (path.Count == 0) continue;
                    if (bestPath == null || path.Count < bestPath.Count)
                    {
                        bestPath = path;
                        best = tile;
                    }
                }
                if (bestPath == null) continue;

                if (hero.MoveComputerHeroTo(best))
                    log.Add($"{player.Name}: {HeroName(hero)} advances on the {DisplayName(target)}.");
            }
        }

        /// <summary>The tiles that trigger an object's visit, in map coordinates.</summary>
        static List<Vector2Int> ActivationTiles(MapObjectDef obj)
        {
            var tiles = new List<Vector2Int>();
            var ox = Mathf.RoundToInt(obj.x);
            var oy = Mathf.RoundToInt(obj.y);
            if (obj.activeTiles != null && obj.activeTiles.Count > 0)
            {
                foreach (var at in obj.activeTiles)
                    if (at != null && at.Count >= 2) tiles.Add(new Vector2Int(ox + at[0], oy + at[1]));
            }
            else
            {
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                        tiles.Add(new Vector2Int(ox + dx, oy + dy));
            }
            return tiles;
        }

        static string DisplayName(MapObjectDef obj)
        {
            return !string.IsNullOrEmpty(obj.name) ? obj.name : obj.kind;
        }

        static string HeroName(H5MapObject hero)
        {
            var hs = hero != null ? hero.HeroState : null;
            return hs != null && !string.IsNullOrEmpty(hs.HeroName) ? hs.HeroName : "hero";
        }

        /// <summary>
        /// The best thing this hero can go at: an enemy town if it is close,
        /// otherwise the richest pickable object within reach, weighted by how
        /// dangerous it looks.
        /// </summary>
        static MapObjectDef ChooseTarget(AdventureMapGameState state, H5PlayerState player, H5MapObject hero)
        {
            var objects = state.MapObjects;
            if (objects == null || hero.Def == null) return null;

            MapObjectDef best = null;
            var bestScore = float.MinValue;

            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj == null) continue;
                if (obj.floor != hero.Def.floor) continue;
                if (string.Equals(obj.owner, player.Id, System.StringComparison.OrdinalIgnoreCase))
                    continue;                                  // already ours
                // Hero against hero is not a battle the game can fight yet.
                if (obj.kind == "AdvMapHero") continue;
                if (state.IsCollected(obj)) continue;

                var dx = obj.x - hero.Def.x;
                var dy = obj.y - hero.Def.y;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > TargetRadius || distance < 0.001f) continue;

                var score = ScoreTarget(state, obj) - distance;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = obj;
                }
            }
            return best;
        }

        /// <summary>
        /// How much a target is worth going after. Enemy towns dominate: taking
        /// one is how the match is actually won.
        /// </summary>
        static float ScoreTarget(AdventureMapGameState state, MapObjectDef obj)
        {
            if (obj.kind == "AdvMapTown") return 500f;
            if (obj.kind == "AdvMapHero") return 300f;
            if (state.IsBank(obj)) return 160f;
            if (obj.kind == "AdvMapMonster")
            {
                // Attack only what looks beatable; the creature's own tier is a
                // decent proxy and it is a shipped number.
                var creature = state.Database != null ? state.Database.GetCreature(obj.creature) : null;
                // CreatureTier is the shipped difficulty grade (1 weakest .. 7);
                // the AI walks away from the top of the range early on.
                var tier = creature != null && creature.CreatureTier > 0 ? creature.CreatureTier : 1;
                return tier <= 4 ? 120f - tier * 15f : -50f;
            }
            // The kinds below are the ones the shipped maps actually contain.
            // AdvMapStatic is scenery and scores nothing: 57519 of them in 20
            // maps, and sending a hero at a rock is how an AI looks broken.
            if (obj.kind == "AdvMapMine") return 110f;
            if (obj.kind == "AdvMapDwelling") return 100f;
            if (obj.kind == "AdvMapTreasure" || obj.kind == "AdvMapArtifact") return 90f;
            if (obj.kind == "AdvMapAbanMine") return 85f;
            if (obj.kind == "AdvMapShrine" || obj.kind == "AdvMapHillFort") return 70f;
            if (obj.kind == "AdvMapBuilding") return 40f;
            return 5f;
        }
    }
}
