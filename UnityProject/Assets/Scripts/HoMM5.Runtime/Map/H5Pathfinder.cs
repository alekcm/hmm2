using System.Collections.Generic;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// A* over the imported adventure-map passability plane. Coordinates are
    /// integer map tiles; diagonal movement is allowed only when both adjacent
    /// orthogonal cells are open, so paths cannot cut through blocked corners.
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
            for (var i = 0; i < total; i++) { costs[i] = float.PositiveInfinity; parent[i] = -1; }

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
                    var candidate = costs[current] + (diagonal ? 1.41421356f : 1f);
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

        /// <summary>Return the longest prefix affordable with the supplied movement points.</summary>
        public static List<Vector2Int> AffordablePrefix(Vector2Int start, List<Vector2Int> path, float budget)
        {
            var affordable = new List<Vector2Int>();
            if (path == null || budget <= 0f) return affordable;
            var cost = 0f;
            var previous = start;
            for (var i = 0; i < path.Count; i++)
            {
                var tile = path[i];
                var diagonal = previous.x != tile.x && previous.y != tile.y;
                var step = diagonal ? 1.41421356f : 1f;
                if (cost + step > budget + 0.0001f) break;
                cost += step;
                affordable.Add(tile);
                previous = tile;
            }
            return affordable;
        }

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

        static float Heuristic(Vector2Int a, Vector2Int b)
        {
            var dx = Mathf.Abs(a.x - b.x);
            var dy = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dy) + 0.41421356f * Mathf.Min(dx, dy);
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
