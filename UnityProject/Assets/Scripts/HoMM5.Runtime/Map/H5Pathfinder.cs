using System.Collections.Generic;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The modifiers passed to NWorld::STileGameInfo::CalcMoveCost.  The native
    /// executable uses integer points (75/100/125/150, multiplied by the
    /// diagonal constant and truncated), so this profile deliberately does not
    /// convert a tile into an invented unit cost.
    /// </summary>
    public struct H5MovementProfile
    {
        /// <summary>NDb AdvMapTileType. The source default is TT_GRASS (1).</summary>
        public int nativeTerrain;
        /// <summary>Pathfinding halves the non-native terrain penalty.</summary>
        public float terrainPenaltyMultiplier;
        /// <summary>Snatch removes the non-native terrain penalty.</summary>
        public bool snatch;
        /// <summary>Additional source modifier, kept for future authored effects.</summary>
        public int penaltyReductionPercent;

        public static H5MovementProfile Default => new H5MovementProfile
        {
            nativeTerrain = 1, // TT_GRASS, RPGCombatSkillRoadHomeParams default
            terrainPenaltyMultiplier = 1f,
            snatch = false,
            penaltyReductionPercent = 0,
        };
    }

    /// <summary>
    /// A* over the imported adventure-map passability plane. Coordinates are
    /// integer map tiles; diagonal movement is allowed only when both adjacent
    /// orthogonal cells are open, so paths cannot cut through blocked corners.
    /// Costs and passability follow the recovered NWorld routines.
    /// </summary>
    public static class H5Pathfinder
    {
        static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1),
            new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        public static List<Vector2Int> FindPath(H5Terrain terrain, Vector2Int start, Vector2Int goal)
        {
            return FindPath(terrain, start, goal, H5MovementProfile.Default);
        }

        public static List<Vector2Int> FindPath(H5Terrain terrain, Vector2Int start,
                                                Vector2Int goal, H5MovementProfile profile)
        {
            var result = new List<Vector2Int>();
            if (terrain == null || !terrain.IsPassable(start.x, start.y) ||
                !terrain.IsPassable(goal.x, goal.y)) return result;
            if (start == goal) return result;

            var width = terrain.TilesX + 1;
            var height = terrain.TilesY + 1;
            var total = width * height;
            var costs = new float[total];
            var parent = new int[total];
            var closed = new bool[total];
            for (var i = 0; i < total; i++)
            {
                costs[i] = float.PositiveInfinity;
                parent[i] = -1;
            }

            var startIndex = start.y * width + start.x;
            var goalIndex = goal.y * width + goal.x;
            costs[startIndex] = 0f;
            var open = new MinHeap();
            open.Push(startIndex, Heuristic(start, goal));

            while (open.Count > 0)
            {
                var current = open.Pop();
                if (closed[current]) continue;
                if (current == goalIndex) break;
                closed[current] = true;
                var p = new Vector2Int(current % width, current / width);

                for (var n = 0; n < Neighbours.Length; n++)
                {
                    var d = Neighbours[n];
                    var next = p + d;
                    if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height ||
                        !terrain.IsPassable(next.x, next.y)) continue;
                    var diagonal = d.x != 0 && d.y != 0;
                    if (diagonal && (!terrain.IsPassable(p.x + d.x, p.y) ||
                                     !terrain.IsPassable(p.x, p.y + d.y))) continue;

                    var nextIndex = next.y * width + next.x;
                    if (closed[nextIndex]) continue;
                    var candidate = costs[current] + StepCost(terrain, next, diagonal, profile);
                    if (candidate >= costs[nextIndex]) continue;
                    costs[nextIndex] = candidate;
                    parent[nextIndex] = current;
                    open.Push(nextIndex, candidate + Heuristic(next, goal));
                }
            }

            if (parent[goalIndex] < 0) return result;
            for (var at = goalIndex; at != startIndex; at = parent[at])
                result.Add(new Vector2Int(at % width, at / width));
            result.Reverse();
            return result;
        }

        /// <summary>One native hero step, including the diagonal multiplier.</summary>
        public static int StepCost(H5Terrain terrain, Vector2Int to, bool diagonal,
                                   H5MovementProfile profile)
        {
            if (terrain == null) return 0;
            var terrainType = terrain.TerrainTypeAt(to.x, to.y);
            var roadType = terrain.RoadTypeAt(to.x, to.y);
            var baseCost = 100;
            var penalty = 0f;

            // STileGameInfo::CalcMoveCost: road wins over native terrain.
            if (roadType == 6 || roadType == 7 || roadType == 8)
            {
                baseCost = 75;
            }
            else if (terrainType >= 0 && terrainType != profile.nativeTerrain)
            {
                switch (terrainType)
                {
                    case 0: case 4: case 5: case 13: case 14:
                        penalty = 25f; break;
                    case 2: case 3:
                        penalty = 50f; break;
                    default:
                        penalty = 0f; break;
                }
                if (profile.terrainPenaltyMultiplier >= 0f)
                    penalty *= profile.terrainPenaltyMultiplier;
                if (profile.snatch) penalty = 0f;
                if (profile.penaltyReductionPercent > 0)
                    penalty *= 1f - profile.penaltyReductionPercent * 0.01f;
            }

            var total = baseCost + penalty;
            if (diagonal) total *= 1.41421354f;
            return (int)total; // cvttss2si in CHeroMovementCost::GetStepCost
        }

        /// <summary>Return the longest prefix affordable with native movement points.</summary>
        public static List<Vector2Int> AffordablePrefix(Vector2Int start, List<Vector2Int> path,
                                                         float budget)
        {
            return AffordablePrefix(start, path, budget, H5MovementProfile.Default);
        }

        public static List<Vector2Int> AffordablePrefix(Vector2Int start, List<Vector2Int> path,
                                                         float budget, H5MovementProfile profile,
                                                         H5Terrain terrain = null)
        {
            var affordable = new List<Vector2Int>();
            if (path == null || budget <= 0f) return affordable;
            var cost = 0f;
            var previous = start;
            for (var i = 0; i < path.Count; i++)
            {
                var tile = path[i];
                var diagonal = previous.x != tile.x && previous.y != tile.y;
                var step = terrain != null ? StepCost(terrain, tile, diagonal, profile) :
                    (diagonal ? 1.41421356f : 1f);
                if (cost + step > budget + 0.0001f) break;
                cost += step;
                affordable.Add(tile);
                previous = tile;
            }
            return affordable;
        }

        /// <summary>Exact native cost of a path in movement points.</summary>
        public static float PathCost(Vector2Int start, List<Vector2Int> path)
        {
            var cost = 0f;
            var previous = start;
            if (path == null) return cost;
            for (var i = 0; i < path.Count; i++)
            {
                var tile = path[i];
                cost += previous.x != tile.x && previous.y != tile.y ? 1.41421356f : 1f;
                previous = tile;
            }
            return cost;
        }

        public static int PathCost(H5Terrain terrain, Vector2Int start, List<Vector2Int> path,
                                   H5MovementProfile profile)
        {
            var cost = 0;
            var previous = start;
            if (path == null) return cost;
            for (var i = 0; i < path.Count; i++)
            {
                var tile = path[i];
                cost += StepCost(terrain, tile, previous.x != tile.x && previous.y != tile.y, profile);
                previous = tile;
            }
            return cost;
        }

        static float Heuristic(Vector2Int a, Vector2Int b)
        {
            var dx = Mathf.Abs(a.x - b.x);
            var dy = Mathf.Abs(a.y - b.y);
            // 75 is the cheapest authored orthogonal step (road); this is a
            // lower bound for all profiles and keeps A* admissible.
            return (Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy)) * 75f;
        }

        sealed class MinHeap
        {
            struct Entry { public int node; public float priority; }
            readonly List<Entry> _items = new List<Entry>();
            public int Count => _items.Count;

            public void Push(int node, float priority)
            {
                var i = _items.Count;
                _items.Add(new Entry { node = node, priority = priority });
                while (i > 0)
                {
                    var p = (i - 1) / 2;
                    if (_items[p].priority <= priority) break;
                    _items[i] = _items[p]; i = p;
                }
                _items[i] = new Entry { node = node, priority = priority };
            }

            public int Pop()
            {
                var result = _items[0].node;
                var last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count == 0) return result;
                var i = 0;
                while (true)
                {
                    var left = i * 2 + 1;
                    if (left >= _items.Count) break;
                    var right = left + 1;
                    var child = right < _items.Count && _items[right].priority < _items[left].priority
                        ? right : left;
                    if (_items[child].priority >= last.priority) break;
                    _items[i] = _items[child]; i = child;
                }
                _items[i] = last;
                return result;
            }
        }
    }
}
