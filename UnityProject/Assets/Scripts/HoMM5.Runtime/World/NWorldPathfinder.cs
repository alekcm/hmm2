using System;
using System.Collections.Generic;
using HoMM5.Data;
using HoMM5.Map;
using UnityEngine;

namespace HoMM5.World
{
    /// <summary>
    /// Decompiled implementation of <c>NWorld::CHeroMovementCost</c> (vftable <c>0x00e935b0</c>),
    /// <c>NWorld::CTetraMovementCost</c> (vftable <c>0x00e4ce2c</c>),
    /// <c>NWorld::STileGameInfo::CalcMoveCost</c> (<c>0x00ca9a70</c>), and
    /// <c>NWorld::IsTilePassable</c> (<c>0x005e11a0</c>) from <c>H5_Game_PEST.exe</c>.
    /// </summary>
    public class NWorldPathfinder
    {
        public const int RoadBaseCost = 75;
        public const int OffRoadBaseCost = 100;
        public const int UnknownTerrainType = 9; // NDb::AdvMapTileType::TT_NONE
        public const float DiagonalMultiplier = 1.41421354f; // .rdata 0x00e557b8

        private readonly int _width;
        private readonly int _height;
        private readonly bool[,] _passable;
        private readonly int[,] _terrainType;
        private readonly int[,] _roadType;

        public NWorldPathfinder(int width, int height, H5Terrain terrain = null, MapDef map = null, int floor = 0)
        {
            _width = Mathf.Max(1, width);
            _height = Mathf.Max(1, height);
            _passable = new bool[_width, _height];
            _terrainType = new int[_width, _height];
            _roadType = new int[_width, _height];

            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    // GroundTerrain.flags describes visual ground kind; it is
                    // not STileGameInfo.passabilityFlag.  Use the exported
                    // tag-0x0f plane instead of treating a visual water flag as
                    // proof that a tile is blocked.
                    var pass = terrain == null;
                    if (terrain != null && terrain.HasPassability)
                    {
                        pass = terrain.IsTilePassable(x, y);
                    }
                    _passable[x, y] = pass;
                    // The terrain import does not contain STileGameInfo's
                    // terrain/road enum planes.  TT_NONE is explicit unknown,
                    // rather than silently interpreting ground flags as types.
                    _terrainType[x, y] = UnknownTerrainType;
                    _roadType[x, y] = 0;
                }
            }

            if (map?.objects != null)
            {
                foreach (var obj in map.objects)
                {
                    if (obj.floor != floor || obj.blocked == null) continue;
                    var ox = Mathf.RoundToInt(obj.x);
                    var oy = Mathf.RoundToInt(obj.y);
                    foreach (var cell in obj.blocked)
                    {
                        if (cell == null || cell.Count < 2) continue;
                        var bx = ox + cell[0];
                        var by = oy + cell[1];
                        if (bx >= 0 && bx < _width && by >= 0 && by < _height)
                            _passable[bx, by] = false;
                    }
                }
            }
        }

        /// <summary>
        /// Packs <c>(x, y, floor)</c> into a 30-bit coordinate word exactly as <c>0x00419690</c>
        /// and <c>0x00bcc310</c> in <c>H5_Game_PEST.exe</c>.
        /// </summary>
        public static int PackCoord(int x, int y, int floor)
        {
            return (x & 0x3FF) | ((y & 0x3FF) << 10) | ((floor & 0x3FF) << 20);
        }

        public static void UnpackCoord(int packed, out int x, out int y, out int floor)
        {
            x = packed & 0x3FF;
            y = (packed >> 10) & 0x3FF;
            floor = (packed >> 20) & 0x3FF;
        }

        public bool IsTilePassable(int x, int y)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return false;
            return _passable[x, y];
        }

        public void SetTilePassable(int x, int y, bool passable)
        {
            if (x >= 0 && x < _width && y >= 0 && y < _height)
                _passable[x, y] = passable;
        }

        public void SetTileTerrain(int x, int y, int terrainType, int roadType = 0)
        {
            if (x >= 0 && x < _width && y >= 0 && y < _height)
            {
                _terrainType[x, y] = terrainType;
                _roadType[x, y] = roadType;
            }
        }

        /// <summary>
        /// Compatibility overload for callers that only have a road boolean.
        /// The authoritative tile value is still the NDb road enum 6..8.
        /// </summary>
        public void SetTileTerrain(int x, int y, int terrainType, bool hasRoad)
        {
            SetTileTerrain(x, y, terrainType, hasRoad ? 6 : 0);
        }

        /// <summary>
        /// Computes the non-native terrain penalty from the jump table at <c>0x00ca9be8</c>
        /// inside <c>NWorld::STileGameInfo::CalcMoveCost</c> (<c>0x00ca9a70</c>).
        /// </summary>
        public static int GetTerrainPenalty(int terrainType, int heroNativeTerrain)
        {
            if (terrainType == heroNativeTerrain) return 0;
            switch (terrainType)
            {
                case 0:
                case 4:
                case 5:
                case 13:
                case 14:
                    return 25; // 0x00ca9aed: mov $0x19, %eax
                case 2:
                case 3:
                    return 50; // 0x00ca9af7: mov $0x32, %eax
                default:
                    return 0;  // 0x00ca9be5: xor %eax, %eax
            }
        }

        /// <summary>
        /// Exact decompiled step movement cost from <c>0x00cab090</c> (<c>NWorld::CHeroMovementCost</c>).
        /// Pathfinding halves the penalty, Snatch clears it, and the positive
        /// modifier at <c>SHeroMoveModifiers+0x14</c> applies a further percent
        /// reduction after those operations.
        /// </summary>
        public static int CalcStepCost(
            int terrainType,
            int heroNativeTerrain,
            int roadType,
            bool isDiagonal,
            bool hasPathfinding,
            bool hasSnatch,
            int penaltyReductionPercent = 0)
        {
            int baseCost;
            float penalty;

            if (roadType == 6 || roadType == 7 || roadType == 8)
            {
                baseCost = RoadBaseCost;
                penalty = 0f;
            }
            else
            {
                baseCost = OffRoadBaseCost;
                penalty = GetTerrainPenalty(terrainType, heroNativeTerrain);
            }

            if (hasPathfinding)
                penalty *= 0.5f;
            if (hasSnatch)
                penalty = 0f;
            if (penaltyReductionPercent > 0)
                penalty *= 1f - penaltyReductionPercent * 0.01f;

            var total = baseCost + penalty;
            if (isDiagonal)
                total *= DiagonalMultiplier;

            return (int)total; // cvttss2si truncation at 0x00cab196
        }

        /// <summary>
        /// Compatibility overload; the decompiled tile field is roadType 6..8,
        /// not a second visual-ground flag.
        /// </summary>
        public static int CalcStepCost(
            int terrainType,
            int heroNativeTerrain,
            bool isRoad,
            bool isDiagonal,
            bool hasPathfinding,
            bool hasSnatch)
        {
            return CalcStepCost(terrainType, heroNativeTerrain, isRoad ? 6 : 0,
                                isDiagonal, hasPathfinding, hasSnatch);
        }

        /// <summary>
        /// A* shortest path matching <c>NWorld::CalcHeroMoveCost</c> (<c>0x005ceaf0</c>).
        /// Returns total movement point cost, or <c>-1</c> if unreachable.
        /// </summary>
        public int FindPath(
            Vector2Int start,
            Vector2Int goal,
            NWorldHero hero,
            out List<Vector2Int> path)
        {
            path = new List<Vector2Int>();
            if (!IsTilePassable(goal.x, goal.y)) return -1;
            if (start == goal)
            {
                path.Add(start);
                return 0;
            }

            var gScore = new Dictionary<Vector2Int, int> { [start] = 0 };
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var open = new List<Vector2Int> { start };
            var closed = new HashSet<Vector2Int>();

            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

            while (open.Count > 0)
            {
                var bestIdx = 0;
                var bestF = int.MaxValue;
                for (var i = 0; i < open.Count; i++)
                {
                    var node = open[i];
                    var f = gScore[node] + Heuristic(node, goal);
                    if (f < bestF)
                    {
                        bestF = f;
                        bestIdx = i;
                    }
                }

                var current = open[bestIdx];
                open.RemoveAt(bestIdx);

                if (current == goal)
                {
                    var cur = goal;
                    while (cur != start)
                    {
                        path.Add(cur);
                        cur = cameFrom[cur];
                    }
                    path.Add(start);
                    path.Reverse();
                    return gScore[goal];
                }

                closed.Add(current);

                for (var d = 0; d < 8; d++)
                {
                    var nx = current.x + dx[d];
                    var ny = current.y + dy[d];
                    if (!IsTilePassable(nx, ny)) continue;

                    var next = new Vector2Int(nx, ny);
                    if (closed.Contains(next)) continue;

                    var isDiag = dx[d] != 0 && dy[d] != 0;
                    var stepCost = CalcStepCost(
                        _terrainType[nx, ny],
                        hero != null ? hero.NativeTerrainType : UnknownTerrainType,
                        _roadType[nx, ny],
                        isDiag,
                        hero != null && hero.HasPathfinding,
                        hero != null && hero.HasSnatch,
                        hero != null ? hero.MovementPenaltyReductionPercent : 0);

                    var tentativeG = gScore[current] + stepCost;
                    if (!gScore.TryGetValue(next, out var prevG) || tentativeG < prevG)
                    {
                        cameFrom[next] = current;
                        gScore[next] = tentativeG;
                        if (!open.Contains(next)) open.Add(next);
                    }
                }
            }

            return -1;
        }

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            var dx = Mathf.Abs(a.x - b.x);
            var dy = Mathf.Abs(a.y - b.y);
            var diag = Mathf.Min(dx, dy);
            var straight = Mathf.Max(dx, dy) - diag;
            return diag * 106 + straight * 75;
        }
    }
}
