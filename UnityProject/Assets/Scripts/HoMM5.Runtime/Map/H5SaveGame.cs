using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The mutable world state, as a JSON document.
    ///
    /// Everything that changes over a game -- day, resources, hero stats and
    /// positions, picked-up treasures, mine ownership, fired visit effects --
    /// lives here; the imported map data is never edited, only overridden.
    /// </summary>
    public class H5SaveGame
    {
        public int version = 1;
        public string mapId = "";
        public int floor;
        public int day = 1;
        public string localPlayerId = "PLAYER_1";
        public HoMM5.Data.PlayerResourceInventory resources;

        /// <summary>Every player in the match, with the computer's treasuries (older saves have none).</summary>
        public List<H5PlayerState> players = new List<H5PlayerState>();
        public int turnIndex;

        /// <summary>Hero movement points remaining, keyed by hero map-object id.</summary>
        public Dictionary<string, float> movement = new Dictionary<string, float>();

        public List<H5HeroState> heroes = new List<H5HeroState>();

        public class PositionEntry
        {
            public string id;
            public float x;
            public float y;
        }

        public List<PositionEntry> heroPositions = new List<PositionEntry>();

        /// <summary>Object ids already picked up (treasures, artifacts, chests).</summary>
        public List<string> collected = new List<string>();

        /// <summary>Objects whose once-ever visit effect has fired.</summary>
        public List<string> visitedOnce = new List<string>();

        /// <summary>Object id -> day / week of the last once-per-day/week visit.</summary>
        public Dictionary<string, int> visitedDay = new Dictionary<string, int>();
        public Dictionary<string, int> visitedWeek = new Dictionary<string, int>();

        /// <summary>Object id -> owning player (captured mines).</summary>
        public Dictionary<string, string> owners = new Dictionary<string, string>();

        /// <summary>
        /// Town state: what has been built, what the dwellings have bred and who
        /// owns each town. The authored slots come back from the map JSON, so only
        /// the parts that change during play are stored.
        /// </summary>
        public List<H5TownState> towns = new List<H5TownState>();

        public List<string> log = new List<string>();

        // -- files ---------------------------------------------------------

        public static string SavePath(string slot = "quicksave")
        {
            return Path.Combine(Application.persistentDataPath, "homm5_" + slot + ".json");
        }

        public void SaveToFile(string slot = "quicksave")
        {
            var path = SavePath(slot);
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            Debug.Log("[HoMM5] game saved to " + path);
        }

        public static H5SaveGame LoadFromFile(string slot = "quicksave")
        {
            var path = SavePath(slot);
            if (!File.Exists(path)) return null;
            try
            {
                var save = JsonConvert.DeserializeObject<H5SaveGame>(File.ReadAllText(path));
                if (save == null) return null;
                Debug.Log("[HoMM5] game loaded from " + path);
                return save;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[HoMM5] bad save file " + path + ": " + e.Message);
                return null;
            }
        }

        public static bool Exists(string slot = "quicksave") => File.Exists(SavePath(slot));
    }
}
