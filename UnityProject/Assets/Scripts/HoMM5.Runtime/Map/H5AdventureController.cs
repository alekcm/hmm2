using System;
using System.Collections.Generic;
using HoMM5.Data;
using HoMM5.World;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The playable adventure-map loop currently supported by confirmed NWorld
    /// data: select the PLAYER_1 hero, click a reachable tile, run the exact
    /// decompiled path-cost calculation and spend the hero's move points while
    /// moving the imported hero object across that path.
    ///
    /// Encounters, object scripts and turn transitions are deliberately not
    /// fabricated here; they remain source-backed work for the next systems.
    /// </summary>
    public sealed class H5AdventureController : MonoBehaviour
    {
        MapSceneBuilder _map;
        AdventureMapController _camera;
        NWorldHero _hero;
        H5MapObject _heroObject;
        bool _moving;
        bool _heroSelected;

        public string Status { get; private set; } = "";
        public event Action StatusChanged;
        public H5MapObject HeroObject => _heroObject;

        public void Bind(MapSceneBuilder map, AdventureMapController camera, NWorldHero hero,
                         GameDatabase database)
        {
            Unbind();
            _map = map;
            _camera = camera;
            _hero = hero;
            if (_map == null || _camera == null || _hero == null)
            {
                SetStatus("No active hero is present on this map.");
                return;
            }

            foreach (var obj in _map.PlacedObjects)
            {
                obj.AttachDatabase(database);
                if (_heroObject == null && obj.IsHero && obj.Def != null &&
                    obj.Def.floor == _map.ActiveFloor &&
                    string.Equals(obj.Def.owner, "PLAYER_1", StringComparison.OrdinalIgnoreCase))
                    _heroObject = obj;
            }

            _camera.SelectionChanged += OnSelectionChanged;
            _camera.MapClicked += OnMapClicked;
            if (_heroObject == null)
            {
                SetStatus("No PLAYER_1 hero is present on this map.");
                return;
            }

            _hero.TileX = Mathf.RoundToInt(_heroObject.Def.x);
            _hero.TileY = Mathf.RoundToInt(_heroObject.Def.y);
            _hero.Floor = _map.ActiveFloor;
            SetStatus(HeroPositionText());
        }

        public void Unbind()
        {
            _moving = false;
            if (_camera != null)
            {
                _camera.SelectionChanged -= OnSelectionChanged;
                _camera.MapClicked -= OnMapClicked;
            }
            _map = null;
            _camera = null;
            _hero = null;
            _heroObject = null;
            _heroSelected = false;
        }

        void OnSelectionChanged(H5MapObject selected)
        {
            _heroSelected = selected != null && selected == _heroObject;
            if (_heroSelected) SetStatus(HeroPositionText());
        }

        void OnMapClicked(Vector3 point)
        {
            if (!_heroSelected || _heroObject == null || _map == null || _hero == null) return;
            if (_moving) return;

            var target = new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z));
            var start = new Vector2Int(Mathf.RoundToInt(_heroObject.Def.x), Mathf.RoundToInt(_heroObject.Def.y));
            if (target == start) return;

            List<Vector2Int> path;
            var cost = -1;
            if (_map.Pathfinder != null)
                cost = _map.Pathfinder.FindPath(start, target, _hero, out path);
            else
                path = new List<Vector2Int>();
            if (cost < 0)
            {
                SetStatus("The selected tile is not passable.");
                return;
            }
            if (cost > _hero.MovePoints)
            {
                SetStatus("The hero does not have enough movement points.");
                return;
            }

            _hero.MovePoints -= cost;
            MoveAlong(path);
        }

        void MoveAlong(List<Vector2Int> path)
        {
            _moving = true;
            for (var i = 1; i < path.Count; i++)
            {
                var to = TilePosition(path[i]);
                _heroObject.transform.localPosition = to;
                _heroObject.Def.x = path[i].x;
                _heroObject.Def.y = path[i].y;
                _hero.TileX = path[i].x;
                _hero.TileY = path[i].y;
            }

            _moving = false;
            SetStatus(HeroPositionText());
            _camera.FocusOn(_heroObject.transform.position, -1f);
        }

        Vector3 TilePosition(Vector2Int tile)
        {
            var height = _map.Terrain != null
                ? _map.Terrain.SampleWorldHeight(tile.x, tile.y)
                : 0f;
            return TerrainMeshBuilder.ToUnity(tile.x, tile.y, height);
        }

        string HeroPositionText()
        {
            if (_hero == null || _heroObject == null) return "No active hero is present on this map.";
            var name = string.IsNullOrEmpty(_heroObject.Def.hero) ? "Hero" : _heroObject.Def.hero;
            return name + "  " + _hero.TileX + "," + _hero.TileY +
                   "  Movement " + _hero.MovePoints + "/" + _hero.GetMaxMovePoints();
        }

        void SetStatus(string status)
        {
            Status = status ?? "";
            StatusChanged?.Invoke();
        }

        void OnDestroy()
        {
            Unbind();
        }
    }
}
