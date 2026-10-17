using System;
using System.Collections.Generic;
using HoMM5.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace HoMM5.Data
{
    /// <summary>
    /// The game's data tables, loaded from the importer's JSON output.
    ///
    /// These are the shipped numbers -- a creature's attack, an artifact's price,
    /// a spell's level all come straight from <c>data/GameMechanics</c>, so the
    /// runtime reasons about the same values the original engine does.
    /// </summary>
    public class GameDatabase
    {
        public Dictionary<string, CreatureDef> Creatures { get; private set; } = new Dictionary<string, CreatureDef>();
        public Dictionary<string, SpellDef> Spells { get; private set; } = new Dictionary<string, SpellDef>();
        public Dictionary<string, ArtifactDef> Artifacts { get; private set; } = new Dictionary<string, ArtifactDef>();
        public List<TownBuildingDef> TownBuildings { get; private set; } = new List<TownBuildingDef>();
        public Dictionary<string, TownTypeDef> TownTypes { get; private set; } = new Dictionary<string, TownTypeDef>();
        public GameRulesDef Rules { get; private set; } = new GameRulesDef { baseHeroMovement = 2500 };
        public Dictionary<string, WarMachineDef> WarMachines { get; private set; } = new Dictionary<string, WarMachineDef>();
        public Dictionary<string, ArenaBuildingDef> ArenaBuildings { get; private set; } = new Dictionary<string, ArenaBuildingDef>();
        public Dictionary<string, SkillDef> Skills { get; private set; } = new Dictionary<string, SkillDef>();
        public Dictionary<string, HeroClassDef> HeroClasses { get; private set; } = new Dictionary<string, HeroClassDef>();
        public Dictionary<string, ModelAnimDef> ModelAnimations { get; private set; } = new Dictionary<string, ModelAnimDef>();
        public Dictionary<string, string> Localization { get; private set; } = new Dictionary<string, string>();
        public List<MapIndexEntry> MapIndex { get; private set; } = new List<MapIndexEntry>();

        readonly Dictionary<string, CreatureDef> _creaturesByEnum =
            new Dictionary<string, CreatureDef>(StringComparer.Ordinal);
        readonly Dictionary<string, TownBuildingDef> _townBuildingByKey =
            new Dictionary<string, TownBuildingDef>(StringComparer.Ordinal);
        readonly Dictionary<string, List<TownBuildingDef>> _townBuildingsByFaction =
            new Dictionary<string, List<TownBuildingDef>>(StringComparer.Ordinal);

        public bool IsLoaded { get; private set; }

        public event Action Loaded;

        public void Load()
        {
            if (!H5Paths.HasImport)
            {
                Debug.LogError(MissingImportMessage());
                return;
            }

            Creatures = LoadDict<CreatureDef>(H5Paths.Game("creatures.json")) ?? Creatures;
            Spells = LoadDict<SpellDef>(H5Paths.Game("spells.json")) ?? Spells;
            Artifacts = LoadDict<ArtifactDef>(H5Paths.Game("artifacts.json")) ?? Artifacts;
            TownBuildings = LoadList<TownBuildingDef>(H5Paths.Game("townBuildings.json")) ?? TownBuildings;
            TownTypes = LoadDict<TownTypeDef>(H5Paths.Game("townTypes.json")) ?? TownTypes;
            IndexCreaturesAndBuildings();
            Rules = LoadSingle<GameRulesDef>(H5Paths.Game("rules.json")) ?? Rules;
            WarMachines = LoadDict<WarMachineDef>(H5Paths.Game("warMachines.json")) ?? WarMachines;
            ArenaBuildings = LoadDict<ArenaBuildingDef>(H5Paths.Game("arenaBuildings.json")) ?? ArenaBuildings;
            Skills = LoadDict<SkillDef>(H5Paths.Game("skills.json")) ?? Skills;
            HeroClasses = LoadDict<HeroClassDef>(H5Paths.Game("heroClasses.json")) ?? HeroClasses;
            ModelAnimations = LoadDict<ModelAnimDef>(H5Paths.Game("modelAnimations.json")) ?? ModelAnimations;
            Localization = LoadDict<string>(H5Paths.Game("Localization.json")) ?? Localization;
            MapIndex = LoadList<MapIndexEntry>(H5Paths.Maps("_index.json")) ?? MapIndex;

            IsLoaded = true;
            Debug.Log($"[HoMM5] loaded {Creatures.Count} creatures, {Spells.Count} spells, " +
                      $"{Artifacts.Count} artifacts, {TownBuildings.Count} town buildings " +
                      $"({TownTypes.Count} town types), " +
                      $"{Skills.Count} skills/perks, {HeroClasses.Count} hero classes, " +
                      $"{Localization.Count} strings, {MapIndex.Count} maps, " +
                      $"{ModelAnimations.Count} animated models");
            Loaded?.Invoke();
        }

        /// <summary>
        /// Secondary indexes over the loaded tables.
        ///
        /// Creatures are keyed in the JSON by their file name (<c>Arch_Magi</c>)
        /// but referenced from everywhere else by enum (<c>CREATURE_ARCH_MAGI</c>)
        /// -- maps, spells, dwellings, the necromancy table. Without this index a
        /// monster standing on the map resolved to nothing at all, so it fought
        /// with default stats and no name.
        /// </summary>
        void IndexCreaturesAndBuildings()
        {
            _creaturesByEnum.Clear();
            foreach (var kv in Creatures)
            {
                var c = kv.Value;
                if (c == null || string.IsNullOrEmpty(c.enumId)) continue;
                _creaturesByEnum[c.enumId] = c;
            }
            _townBuildingByKey.Clear();
            _townBuildingsByFaction.Clear();
            foreach (var b in TownBuildings)
            {
                if (b == null || string.IsNullOrEmpty(b.key)) continue;
                _townBuildingByKey[b.key] = b;
                var faction = b.faction ?? "";
                if (!_townBuildingsByFaction.TryGetValue(faction, out var list))
                    _townBuildingsByFaction[faction] = list = new List<TownBuildingDef>();
                list.Add(b);
            }
        }

        // -- lookups -------------------------------------------------------

        /// <summary>
        /// A creature by its <c>CREATURE_*</c> enum, falling back to the file-name
        /// key and then to the upgrade pair some maps name instead.
        /// </summary>
        public CreatureDef GetCreature(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_creaturesByEnum.TryGetValue(id, out var byEnum)) return byEnum;
            if (Creatures.TryGetValue(id, out var c)) return c;
            foreach (var kv in Creatures)
            {
                if (string.Equals(kv.Value.pairCreature, id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv.Value.baseCreature, id, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>One building record by its faction-qualified key.</summary>
        public TownBuildingDef GetTownBuilding(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _townBuildingByKey.TryGetValue(key, out var b) ? b : null;
        }

        /// <summary>Every building record of one faction folder.</summary>
        public List<TownBuildingDef> TownBuildingsForFaction(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return new List<TownBuildingDef>();
            return _townBuildingsByFaction.TryGetValue(faction, out var list)
                ? list : new List<TownBuildingDef>();
        }

        public TownTypeDef GetTownType(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return TownTypes.TryGetValue(id, out var t) ? t : null;
        }

        public SpellDef GetSpell(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Spells.TryGetValue(id, out var s) ? s : null;
        }

        public ArtifactDef GetArtifact(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Artifacts.TryGetValue(id, out var a) ? a : null;
        }

        /// <summary>A skill or perk record by its HERO_SKILL_* id.</summary>
        public SkillDef GetSkill(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Skills.TryGetValue(id, out var s) ? s : null;
        }

        public HeroClassDef GetHeroClass(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return HeroClasses.TryGetValue(id, out var c) ? c : null;
        }

        /// <summary>Every perk hanging off one skill (any kind but "skill").</summary>
        public List<SkillDef> PerksOfSkill(string skillId)
        {
            var outList = new List<SkillDef>();
            foreach (var kv in Skills)
            {
                var s = kv.Value;
                if (s != null && !s.IsSkill && s.skill == skillId) outList.Add(s);
            }
            return outList;
        }

        public MapIndexEntry GetMapIndexEntry(string id)
        {
            foreach (var e in MapIndex)
                if (string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        /// <summary>Look up one localized string by its <c>/Text/...</c> href.</summary>
        public string Text(string href, string fallback = null)
        {
            if (string.IsNullOrEmpty(href)) return fallback;
            return Localization.TryGetValue(href, out var s) ? s : fallback;
        }

        /// <summary>Turn a <c>TOWN_ACADEMY</c>-style enum into something readable.</summary>
        public static string PrettifyEnum(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var parts = value.Split('_');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
            }
            return string.Join(" ", parts);
        }

        // -- io ------------------------------------------------------------

        /// <summary>
        /// What to run when the import is missing.
        ///
        /// The generated assets are not in version control (~1.3 GB, and every
        /// byte of it is reproducible from the game data that is), so a fresh
        /// clone opens with nothing to load.  The message resolves the actual
        /// paths on this machine rather than quoting a command that has to be
        /// run from one particular directory.
        /// </summary>
        // Public: H5Bootstrap quotes it too, when the texture manifest is what
        // is missing. C# makes a member private when no modifier is written, so
        // the bare `static` here was only ever callable from inside this class.
        public static string MissingImportMessage()
        {
            // Application.dataPath is <repo>/UnityProject/Assets.
            var projectRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, ".."));
            var repoRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(projectRoot, ".."));
            var runner = System.IO.Path.Combine(repoRoot, "import_to_unity.py");
            var data = System.IO.Path.Combine(repoRoot, "data");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[HoMM5] No import found. The generated assets are gitignored; build them once.");
            sb.AppendLine("  expected: " + H5Paths.Root);

            if (!System.IO.Directory.Exists(data))
            {
                sb.AppendLine();
                sb.AppendLine("  The game data is missing too: " + data);
                sb.AppendLine("  Unpack the game's data.pak into that 'data' folder first.");
                return sb.ToString();
            }

            sb.AppendLine();
            if (System.IO.File.Exists(runner))
            {
                sb.AppendLine("  Run this from any folder:");
                sb.AppendLine("    python \"" + runner + "\"");
                sb.AppendLine("  (on Windows try:  py -3 \"" + runner + "\")");
            }
            else
            {
                sb.AppendLine("  import_to_unity.py was not found at the repository root:");
                sb.AppendLine("    " + runner);
                sb.AppendLine("  Run the pipeline directly instead:");
                sb.AppendLine("    cd \"" + System.IO.Path.Combine(repoRoot, "tools", "homm5_import") + "\"");
                sb.AppendLine("    python -m homm5import.cli all -o \"" + H5Paths.Root + "\"");
            }
            sb.AppendLine();
            sb.AppendLine("  Or from the editor: Tools > HoMM5 > Run Importer (all).");
            sb.AppendLine("  See tools/homm5_import/README.md.");
            return sb.ToString();
        }

        static T LoadSingle<T>(string path) where T : class
        {
            var json = H5Paths.ReadAllText(path);
            if (json == null)
            {
                Debug.LogWarning("[HoMM5] missing " + path);
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                Debug.LogError("[HoMM5] failed to parse " + path + ": " + e.Message);
                return null;
            }
        }

        static Dictionary<string, T> LoadDict<T>(string path)
        {
            var json = H5Paths.ReadAllText(path);
            if (json == null)
            {
                Debug.LogWarning("[HoMM5] missing " + path);
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, T>>(json);
            }
            catch (Exception e)
            {
                Debug.LogError("[HoMM5] failed to parse " + path + ": " + e.Message);
                return null;
            }
        }

        static List<T> LoadList<T>(string path)
        {
            var json = H5Paths.ReadAllText(path);
            if (json == null)
            {
                Debug.LogWarning("[HoMM5] missing " + path);
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<List<T>>(json);
            }
            catch (Exception e)
            {
                Debug.LogError("[HoMM5] failed to parse " + path + ": " + e.Message);
                return null;
            }
        }
    }
}
