using System.Collections.Generic;

namespace HoMM5.Map
{
    /// <summary>
    /// One player in a scenario: human or computer, with their own treasury,
    /// heroes, towns and liveness.
    ///
    /// Before this existed the game had a single <c>Resources</c> inventory and
    /// a single <c>LocalPlayerId</c> string. That is enough for a viewer, not
    /// for a match: a 1v1 needs two treasuries, two turn slots, and someone to
    /// lose. The player id stays the map's own vocabulary (<c>PLAYER_1</c>,
    /// <c>PLAYER_2</c>), because that is what <c>MapObjectDef.owner</c> carries
    /// and what the save file records.
    /// </summary>
    [System.Serializable]
    public class H5PlayerState
    {
        public string Id = "PLAYER_1";
        public string Name = "Player 1";

        /// <summary>False for the computer opponent.</summary>
        public bool IsHuman = true;

        /// <summary>Whether this player acts when their turn comes.</summary>
        public bool IsAlive = true;

        /// <summary>Faction folder name: Haven, Academy, Necromancy, ...</summary>
        public string Faction = "";

        /// <summary>Town type id: TOWN_HEAVEN, TOWN_ACADEMY, ...</summary>
        public string TownType = "";

        /// <summary>Hero class this player started with.</summary>
        public string HeroClass = "";

        /// <summary>Difficulty tier, 0 easiest .. 3 hardest (DefaultStats rows).</summary>
        public int Difficulty = 2;

        /// <summary>This player's own treasury. Not shared.</summary>
        public HoMM5.Data.PlayerResourceInventory Resources =
            new HoMM5.Data.PlayerResourceInventory();

        /// <summary>Set once a computer player has been given its starting stake.</summary>
        public bool TreasuryFunded;

        /// <summary>Map-object ids of the heroes this player commands.</summary>
        public List<string> HeroIds = new List<string>();

        /// <summary>Map-object ids of the towns this player holds.</summary>
        public List<string> TownIds = new List<string>();

        /// <summary>Whether the player has lost every town and hero.</summary>
        public bool IsDefeated => !IsAlive;

        public override string ToString()
        {
            return Name + (IsHuman ? "" : " (computer)");
        }

        /// <summary>A treasury snapshot for display: gold plus the rare resources.</summary>
        public string TreasurySummary()
        {
            return Resources != null ? Resources.ToString() : "";
        }
    }

    /// <summary>
    /// The player roster of a scenario: who is in the game, whose turn it is,
    /// and when somebody has won.
    ///
    /// Turn order in Heroes 5 is a fixed sequence over the surviving players,
    /// one day each: the day counter advances once the <em>last</em> player has
    /// acted, not once per player. That is what <see cref="EndTurn"/> implements,
    /// and it is why the day does not move when the first of two players ends
    /// their turn.
    /// </summary>
    [System.Serializable]
    public class H5PlayerRoster
    {
        public List<H5PlayerState> Players = new List<H5PlayerState>();

        /// <summary>Index into <see cref="Players"/> of whoever acts now.</summary>
        public int TurnIndex;

        public H5PlayerState Current =>
            Players != null && Players.Count > 0
                ? Players[System.Math.Max(0, System.Math.Min(TurnIndex, Players.Count - 1))]
                : null;

        public H5PlayerState Find(string id)
        {
            if (string.IsNullOrEmpty(id) || Players == null) return null;
            foreach (var p in Players)
                if (p != null && string.Equals(p.Id, id, System.StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        public H5PlayerState GetOrAdd(string id, bool isHuman)
        {
            var existing = Find(id);
            if (existing != null) return existing;
            var player = new H5PlayerState { Id = id, Name = PrettyName(id), IsHuman = isHuman };
            Players.Add(player);
            return player;
        }

        /// <summary>Only the players still in the game.</summary>
        public List<H5PlayerState> Alive()
        {
            var result = new List<H5PlayerState>();
            if (Players == null) return result;
            foreach (var p in Players)
                if (p != null && p.IsAlive) result.Add(p);
            return result;
        }

        public List<H5PlayerState> Humans()
        {
            var result = new List<H5PlayerState>();
            if (Players == null) return result;
            foreach (var p in Players)
                if (p != null && p.IsHuman) result.Add(p);
            return result;
        }

        /// <summary>The computer opponents, alive or not.</summary>
        public List<H5PlayerState> Computers()
        {
            var result = new List<H5PlayerState>();
            if (Players == null) return result;
            foreach (var p in Players)
                if (p != null && !p.IsHuman) result.Add(p);
            return result;
        }

        /// <summary>
        /// Advance to the next surviving player.
        /// Returns true when the sequence wrapped, which is when the day ends.
        /// </summary>
        public bool EndTurn()
        {
            if (Players == null || Players.Count == 0) return true;
            var start = TurnIndex;
            for (var step = 0; step < Players.Count; step++)
            {
                TurnIndex = (TurnIndex + 1) % Players.Count;
                var candidate = Players[TurnIndex];
                if (candidate != null && candidate.IsAlive)
                    return TurnIndex <= start;
            }
            // Nobody is alive; do not spin.
            return true;
        }

        /// <summary>
        /// Drop a player from the game. Called when their last town and hero are
        /// gone -- in Heroes 5 a player without either is out.
        /// </summary>
        public void Defeat(string id)
        {
            var player = Find(id);
            if (player != null) player.IsAlive = false;
        }

        /// <summary>
        /// The winner, when exactly one player is left standing.
        /// Null while the match is still open, and null in a mutual destruction.
        /// </summary>
        public H5PlayerState Winner()
        {
            var alive = Alive();
            return alive.Count == 1 ? alive[0] : null;
        }

        public static string PrettyName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Player";
            var parts = id.Split('_');
            return parts.Length > 1 ? "Player " + parts[parts.Length - 1] : id;
        }
    }
}
