namespace HoMM5.Audio
{
    /// <summary>
    /// Which catalogue key plays where: the mapping from game state (a town type,
    /// the terrain under the hero, the outcome of a battle) to the names in
    /// sounds.json. Keeping it here means the screens never spell a key out.
    /// </summary>
    public static class H5Cues
    {
        public const string Click = "interface/click";
        public const string MainMenuTheme = "mainmenu/mainmenu";
        public const string NeutralBattle = "neutral";

        /// <summary>
        /// The music-tree folder for a town type (<c>TOWN_HEAVEN</c> is the Haven
        /// folder). Returns "" when the town type is unknown, so the caller can fall
        /// back to the faction name.
        /// </summary>
        public static string FactionFolder(string townType, string faction)
        {
            switch (townType ?? "")
            {
                case "TOWN_HEAVEN": return "haven";
                case "TOWN_PRESERVE": return "sylvan";
                case "TOWN_ACADEMY": return "academy";
                case "TOWN_DUNGEON": return "dungeon";
                case "TOWN_NECROMANCY": return "necropolis";
                case "TOWN_INFERNO": return "inferno";
                case "TOWN_FORTRESS": return "a1-fortress";
                case "TOWN_STRONGHOLD": return "a2_stronghold";
            }
            return string.IsNullOrEmpty(faction) ? "" : faction.ToLowerInvariant().Replace(' ', '-');
        }

        /// <summary>Town screen theme, <c>towns/&lt;faction&gt;</c>.</summary>
        public static string TownTheme(string folder) => "towns/" + folder;

        /// <summary>Combat theme, <c>battle-themes/&lt;faction&gt;</c>. Field fights use the neutral one.</summary>
        public static string BattleTheme(string folder) =>
            "battle-themes/" + (string.IsNullOrEmpty(folder) ? NeutralBattle : folder);

        /// <summary>Victory stinger. The catalogue spells some folders with underscores, some with dashes.</summary>
        public static string[] VictoryCandidates(string folder) => new[]
        {
            "win-battle/" + Dashed(folder),
            "win-battle/haven",
        };

        /// <summary>Defeat stinger, with the same fallbacks as victory.</summary>
        public static string[] DefeatCandidates(string folder) => new[]
        {
            "lose-battle/" + Dashed(folder),
            "lose-battle/haven",
        };

        /// <summary>Retreat fanfare, <c>retreat-battle/&lt;faction&gt;</c>.</summary>
        public static string[] RetreatCandidates(string folder) => new[]
        {
            "retreat-battle/" + folder,
            "retreat-battle/haven",
        };

        /// <summary>
        /// The adventure-map theme for the terrain under the heroes. The keyword is
        /// the one the bootstrap reads from the terrain texture names. Underground
        /// always plays the underground track.
        /// </summary>
        public static string[] TerrainCandidates(string keyword, bool underground)
        {
            if (underground) return new[] { "advmap-terrain/underground", "advmap-terrain/grass" };
            string track;
            switch (keyword ?? "")
            {
                case "dirt": track = "dirt"; break;
                case "snow": track = "taiga"; break;
                case "lava": track = "lava"; break;
                case "sand": track = "sand"; break;
                case "beach": track = "sand"; break;
                case "orc": track = "rought"; break;
                case "moss": track = "swamp"; break;
                default: track = "grass"; break;
            }
            return new[] { "advmap-terrain/" + track, "advmap-terrain/grass" };
        }

        /// <summary>Folder name with underscores as dashes: the win/lose trees spell it that way.</summary>
        public static string Dashed(string folder) => (folder ?? "").Replace('_', '-');
    }
}
