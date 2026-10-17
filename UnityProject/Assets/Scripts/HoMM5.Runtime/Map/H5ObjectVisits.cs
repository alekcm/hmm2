using System;
using System.Collections.Generic;
using HoMM5.Combat;
using HoMM5.Data;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>One button of a modal choice the HUD can show.</summary>
    public class H5DialogOption
    {
        public string Label;
        public Action Action;
    }

    /// <summary>
    /// A modal choice (chest: gold or experience; arena: attack or defence).
    /// Raised through <c>AdventureMapGameState.DialogRequested</c>. When no HUD
    /// is listening the first option is taken, so headless tests still run.
    /// </summary>
    public class H5DialogRequest
    {
        public string Message;
        public readonly List<H5DialogOption> Options = new List<H5DialogOption>();
    }

    /// <summary>
    /// What happens when a hero walks onto an object's active tile.
    ///
    /// The dispatch is keyed on the shared record's <c>Type</c> enum
    /// (BUILDING_*, TREASURE_*) exactly as the game's own data spells it, with
    /// the object kinds (AdvMapMine, AdvMapArtifact, ...) handling the rest.
    ///
    /// Amounts come from three sources, in decreasing authority:
    ///   1. the shared record's own MinResource..MaxResource roll,
    ///   2. GameMechanics/RPGStats/DefaultStats.xdb (movement bonuses),
    ///   3. constants the shipped tables do not carry -- marked "inferred".
    /// The treasure gold unit is inferred: TREASURE_GOLD rolls 5..10 and the
    /// classic map gold pile is 500..1000, so one roll unit is 100 gold.
    /// </summary>
    public static class H5ObjectVisits
    {
        /// <summary>Inferred: one treasure roll unit, in gold.</summary>
        public const int GoldRollUnit = 100;
        /// <summary>Inferred: one chest roll unit, in gold (chest rolls 1..3).</summary>
        public const int ChestGoldRollUnit = 500;
        /// <summary>Inferred: experience for giving a chest to the peasants.</summary>
        public const int ChestExperience = 2500;
        /// <summary>Inferred: what the Tree of Knowledge asks for a level.</summary>
        public const int TreeOfKnowledgeGold = 2000;
        /// <summary>Inferred: Water Wheel weekly payout.</summary>
        public const int WaterWheelGold = 500;

        static readonly string[] RareResources = { "MERCURY", "CRYSTAL", "SULFUR", "GEM" };
        static readonly string[] AllResources = { "WOOD", "ORE", "MERCURY", "CRYSTAL", "SULFUR", "GEM" };

        /// <summary>
        /// Visit the object if its active tiles contain the hero's tile.
        /// Returns true when the hero triggered (and completed) something.
        /// </summary>
        public static bool TryVisitAtTile(AdventureMapGameState state, H5MapObject hero, Vector2Int tile)
        {
            if (state == null || hero == null || hero.Def == null || state.MapObjects == null) return false;
            var visited = false;
            var objects = state.MapObjects;
            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj == null || obj.kind == "AdvMapStatic" || obj.floor != hero.Def.floor) continue;
                if (!IsActivationTile(obj, tile)) continue;
                if (TryVisit(state, hero, obj)) visited = true;
            }
            return visited;
        }

        /// <summary>Whether <paramref name="tile"/> is one of the object's active tiles.</summary>
        public static bool IsActivationTile(MapObjectDef obj, Vector2Int tile)
        {
            if (obj == null) return false;
            var ox = Mathf.RoundToInt(obj.x);
            var oy = Mathf.RoundToInt(obj.y);
            if (obj.activeTiles != null && obj.activeTiles.Count > 0)
            {
                for (var i = 0; i < obj.activeTiles.Count; i++)
                {
                    var at = obj.activeTiles[i];
                    if (at == null || at.Count < 2) continue;
                    if (ox + at[0] == tile.x && oy + at[1] == tile.y) return true;
                }
                return false;
            }
            // Imports predating activeTiles: the object's own tile and the ring
            // around it, which is where the shipped active tiles fall.
            return Mathf.Abs(tile.x - ox) <= 1 && Mathf.Abs(tile.y - oy) <= 1;
        }

        /// <summary>Apply one object's visit effect. False when nothing happened.</summary>
        public static bool TryVisit(AdventureMapGameState state, H5MapObject hero, MapObjectDef obj)
        {
            if (state == null || hero == null || hero.Def == null || obj == null) return false;
            if (state.IsCollected(obj)) return false;
            var heroState = state.GetHeroState(hero);
            var view = state.ViewOf(obj);

            switch (obj.kind)
            {
                case "AdvMapMonster":
                {
                    var hs = state.GetHeroState(hero);
                    if (hs == null || hs.Army == null || hs.Army.Count == 0)
                    {
                        state.LogOncePerDay(obj, "There is no army to attack the " +
                                                  DisplayName(obj) + " with.");
                        return true;
                    }
                    state.AddLog($"Battle: {hs.HeroName} attacks the {DisplayName(obj)}!");
                    state.RequestCombat(hero, view);
                    return true;
                }
                case "AdvMapTreasure": return VisitTreasure(state, heroState, hero, obj);
                case "AdvMapArtifact": return VisitArtifact(state, heroState, hero, obj);
                case "AdvMapMine": return VisitMine(state, hero, obj);
                case "AdvMapShrine": return VisitShrine(state, heroState, hero, obj);
                case "AdvMapTent": return VisitTent(state, heroState, hero, obj);
                case "AdvMapSign": return VisitSign(state, hero, obj);
                case "AdvMapBuilding": return VisitBuilding(state, heroState, hero, obj);
                case "AdvMapDwelling":
                {
                    if (OwnedByHero(obj, hero)) return false;
                    // Unguarded dwellings simply change hands; guarded ones
                    // must be taken by force.
                    if (HasGarrison(obj))
                    {
                        state.AddLog($"Battle for the {DisplayName(obj)}!");
                        state.RequestCombat(hero, view);
                    }
                    else
                    {
                        state.CaptureObject(obj);
                        state.AddLog(hero.IsLocalHero
                            ? $"The {DisplayName(obj)} now flies your flag."
                            : $"The {DisplayName(obj)} changes hands.");
                    }
                    return true;
                }
                case "AdvMapGarrison":
                case "AdvMapDwarvenWarren":
                    if (OwnedByHero(obj, hero)) return false;
                    state.AddLog($"Battle for the {DisplayName(obj)}!");
                    state.RequestCombat(hero, view);
                    return true;
                case "AdvMapTown":
                {
                    if (OwnedByHero(obj, hero))
                    {
                        if (hero.IsLocalHero) state.LogOncePerDay(obj, "This town is already yours.");
                        return hero.IsLocalHero;
                    }
                    if (HasGarrison(obj) || obj.garrison != null)
                    {
                        state.AddLog($"The siege of {DisplayName(obj)} begins!");
                        state.RequestCombat(hero, view);
                    }
                    else
                    {
                        state.CaptureObject(obj);
                        state.AddLog(hero.IsLocalHero
                            ? $"{DisplayName(obj)} surrenders to your banner."
                            : $"{DisplayName(obj)} surrenders to the enemy.");
                    }
                    return true;
                }
                case "AdvMapSphinx":
                case "AdvMapSeerHut":
                    state.LogOncePerDay(obj, "Riddles and quests are not implemented yet.");
                    return true;
                case "AdvMapPrison":
                    state.LogOncePerDay(obj, "Recruiting the imprisoned hero is not implemented yet.");
                    return true;
                case "AdvMapHillFort":
                case "AdvMapCartographer":
                case "AdvMapShipyard":
                case "AdvMapAbanMine":
                    state.LogOncePerDay(obj, "The " + DisplayName(obj) + " screen is not implemented yet.");
                    return true;
            }
            return false;
        }

        /// <summary>Whether the object posts a defending garrison.</summary>
        static bool HasGarrison(MapObjectDef obj)
        {
            return obj != null && obj.garrison != null && obj.garrison.stacks != null &&
                   obj.garrison.stacks.Count > 0;
        }

        // -- treasures -----------------------------------------------------

        static bool VisitTreasure(AdventureMapGameState state, H5HeroState hero, H5MapObject mapObj, MapObjectDef obj)
        {
            var roll = Roll(state, obj);
            var sharedType = (obj.sharedType ?? "").ToUpperInvariant();

            // Map-authored piles carry their exact amount; random piles roll
            // MinResource..MaxResource from the shared record.
            if (obj.isCustom && obj.amount > 0)
            {
                var resource = CustomTreasureResource(sharedType);
                if (resource != null)
                {
                    state.TreasuryOf(mapObj).Add(resource, obj.amount);
                    state.CollectObject(obj);
                    state.AddLog($"Picked up {obj.amount} {resource.ToLowerInvariant()}.");
                    return true;
                }
            }

            switch (sharedType)
            {
                case "TREASURE_GOLD":
                case "TREASURE_CAMPFIRE":
                {
                    var gold = roll * GoldRollUnit;
                    state.TreasuryOf(mapObj).gold += gold;
                    state.CollectObject(obj);
                    state.AddLog($"Picked up {gold} gold.");
                    return true;
                }
                case "TREASURE_CHEST":
                case "TREASURE_SEA_CHEST":
                    // The classic dialog: gold, or experience for the peasants.
                    // Deferred to the HUD when one is listening.
                    var gold = roll * ChestGoldRollUnit;
                    var request = new H5DialogRequest
                    {
                        Message = "You found a chest. Take the gold, or give it to the peasants for experience?"
                    };
                    request.Options.Add(new H5DialogOption
                    {
                        Label = $"Take {gold} gold",
                        Action = () =>
                        {
                            state.TreasuryOf(mapObj).gold += gold;
                            state.CollectObject(obj);
                            state.AddLog($"Took {gold} gold from the chest.");
                        }
                    });
                    request.Options.Add(new H5DialogOption
                    {
                        Label = $"Give it away (+{ChestExperience} XP)",
                        Action = () =>
                        {
                            hero.AddExperience(ChestExperience);
                            state.CollectObject(obj);
                            state.AddLog($"The peasants reward you: +{ChestExperience} experience.");
                            state.CheckLevelUps(hero);
                        }
                    });
                    state.RequestDialog(request);
                    return true;
                case "TREASURE_WOOD":
                case "TREASURE_ORE":
                case "TREASURE_MERCURY":
                case "TREASURE_CRYSTAL":
                case "TREASURE_SULFUR":
                case "TREASURE_GEMS":
                {
                    var resource = sharedType.Substring("TREASURE_".Length);
                    state.TreasuryOf(mapObj).Add(resource, roll);
                    state.CollectObject(obj);
                    state.AddLog($"Picked up {roll} {resource.ToLowerInvariant()}.");
                    return true;
                }
                case "TREASURE_FLOATSAM":
                {
                    var wood = Roll(state, obj);
                    var ore = Roll(state, obj);
                    state.TreasuryOf(mapObj).Add("WOOD", wood);
                    state.TreasuryOf(mapObj).Add("ORE", ore);
                    state.CollectObject(obj);
                    state.AddLog($"Flotsam: {wood} wood, {ore} ore.");
                    return true;
                }
                case "TREASURE_RANDOM":
                {
                    var resource = AllResources[state.Rand.Next(AllResources.Length)];
                    var amount = Mathf.Max(1, obj.maxResource > 0 ? Roll(state, obj) : state.Rand.Next(2, 5));
                    state.TreasuryOf(mapObj).Add(resource, amount);
                    state.CollectObject(obj);
                    state.AddLog($"Picked up {amount} {resource.ToLowerInvariant()}.");
                    return true;
                }
                case "TREASURE_SOULSTONE":
                case "TREASURE_SHIPWRECK":
                    state.AddLog($"Battle for the {DisplayName(obj)}!");
                    state.RequestCombat(mapObj, state.ViewOf(obj), BankGuards(sharedType, state.Rand));
                    return true;
                default:
                    // Unknown pile kinds with an authored amount still pay out.
                    if (obj.amount > 0)
                    {
                        state.TreasuryOf(mapObj).gold += obj.amount;
                        state.CollectObject(obj);
                        state.AddLog($"Picked up {obj.amount} gold.");
                        return true;
                    }
                    return false;
            }
        }

        static string CustomTreasureResource(string sharedType)
        {
            switch (sharedType)
            {
                case "TREASURE_WOOD": return "WOOD";
                case "TREASURE_ORE": return "ORE";
                case "TREASURE_MERCURY": return "MERCURY";
                case "TREASURE_CRYSTAL": return "CRYSTAL";
                case "TREASURE_SULFUR": return "SULFUR";
                case "TREASURE_GEMS": return "GEM";
                case "TREASURE_GOLD": return "GOLD";
                default: return null;
            }
        }

        // -- artifacts, mines, shrines, tents ------------------------------

        static bool VisitArtifact(AdventureMapGameState state, H5HeroState hero, H5MapObject mapObj, MapObjectDef obj)
        {
            var artifact = state.Database != null ? state.Database.GetArtifact(obj.artifact) : null;
            if (hero.WearArtifact(obj.artifact ?? ""))
                hero.ApplyArtifactStats(artifact);
            state.CollectObject(obj);
            state.AddLog(artifact != null
                ? $"Picked up the {artifact.DisplayName}."
                : "Picked up an artifact.");
            return true;
        }

        static bool VisitMine(AdventureMapGameState state, H5MapObject hero, MapObjectDef obj)
        {
            if (OwnedByHero(obj, hero)) return false;
            state.CaptureObject(obj);
            state.AddLog(hero.IsLocalHero
                ? $"The {GameDatabase.PrettifyEnum(obj.resource)} mine is now yours."
                : $"The {GameDatabase.PrettifyEnum(obj.resource)} mine changes hands.");
            return true;
        }

        /// <summary>Whether the object already belongs to the visiting hero's player.</summary>
        static bool OwnedByHero(MapObjectDef obj, H5MapObject hero)
        {
            return obj != null && hero != null && hero.Def != null &&
                   !string.IsNullOrEmpty(obj.owner) &&
                   string.Equals(obj.owner, hero.Def.owner, StringComparison.OrdinalIgnoreCase);
        }

        static bool VisitShrine(AdventureMapGameState state, H5HeroState hero, H5MapObject mapObj, MapObjectDef obj)
        {
            if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
            var spell = state.Database != null ? state.Database.GetSpell(obj.spell) : null;
            var name = spell != null ? spell.DisplayName : GameDatabase.PrettifyEnum(obj.spell);
            if (hero.LearnSpell(obj.spell))
                state.AddLog($"{hero.HeroName} learned {name}.");
            else
                state.AddLog($"{hero.HeroName} already knows {name}.");
            return true;
        }

        static bool VisitTent(AdventureMapGameState state, H5HeroState hero, H5MapObject mapObj, MapObjectDef obj)
        {
            if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
            hero.GiveKey(obj.keyColor ?? "");
            state.AddLog($"Picked up the {GameDatabase.PrettifyEnum(obj.keyColor)} key.");
            return true;
        }

        static bool VisitSign(AdventureMapGameState state, H5MapObject mapObj, MapObjectDef obj)
        {
            var text = obj.message;
            if (state.Database != null && !string.IsNullOrEmpty(text))
                text = state.Database.Text(text, text);
            state.AddLog(string.IsNullOrEmpty(text) ? "A sign, with nothing written on it." : text);
            return true;
        }

        // -- buildings -----------------------------------------------------

        static bool VisitBuilding(AdventureMapGameState state, H5HeroState hero, H5MapObject mapObj, MapObjectDef obj)
        {
            var sharedType = (obj.sharedType ?? "").ToUpperInvariant();
            switch (sharedType)
            {
                // -- permanent, once per hero -----------------------------
                case "BUILDING_MERCENARY_CAMP":
                    return Permanent(state, hero, obj, "+1 Attack",
                        () => hero.AddPrimarySkill(1, 1));
                case "BUILDING_MARLETTO_TOWER":
                    return Permanent(state, hero, obj, "+1 Defence",
                        () => hero.AddPrimarySkill(2, 1));
                case "BUILDING_STAR_AXIS":
                    return Permanent(state, hero, obj, "+1 Spell Power",
                        () => hero.AddPrimarySkill(3, 1));
                case "BUILDING_GARDEN_OF_REVELATION":
                    return Permanent(state, hero, obj, "+1 Knowledge",
                        () => hero.AddPrimarySkill(4, 1));
                case "BUILDING_ARENA":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    var request = new H5DialogRequest { Message = "The Arena offers a choice of training." };
                    request.Options.Add(new H5DialogOption
                    {
                        Label = "+1 Attack",
                        Action = () => { hero.AddPrimarySkill(1, 1); state.AddLog($"{hero.HeroName} trains at the Arena: +1 Attack."); }
                    });
                    request.Options.Add(new H5DialogOption
                    {
                        Label = "+1 Defence",
                        Action = () => { hero.AddPrimarySkill(2, 1); state.AddLog($"{hero.HeroName} trains at the Arena: +1 Defence."); }
                    });
                    state.RequestDialog(request);
                    return true;
                }
                case "BUILDING_LIBRARY_OF_ENLIGHTENMENT":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    if (hero.Level < 10)
                    {
                        state.AddLog("The Library of Enlightenment requires hero level 10.");
                        return true;
                    }
                    hero.AddPrimarySkill(1, 1);
                    hero.AddPrimarySkill(2, 1);
                    hero.AddPrimarySkill(3, 1);
                    hero.AddPrimarySkill(4, 1);
                    state.AddLog($"{hero.HeroName} studies at the Library: +1 to all primary skills.");
                    return true;
                }
                case "BUILDING_LEARNING_STONE":
                    return Permanent(state, hero, obj, "+1000 experience",
                        () =>
                        {
                            hero.AddExperience(1000);
                            state.CheckLevelUps(hero);
                        });
                case "BUILDING_TREE_OF_KNOWLEDGE":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    var request = new H5DialogRequest
                    {
                        Message = $"The Tree of Knowledge offers a level for {TreeOfKnowledgeGold} gold. Pay?"
                    };
                    request.Options.Add(new H5DialogOption
                    {
                        Label = "Pay",
                        Action = () =>
                        {
                            if (state.TreasuryOf(mapObj).gold < TreeOfKnowledgeGold)
                            {
                                state.AddLog("Not enough gold for the Tree of Knowledge.");
                                return;
                            }
                            state.TreasuryOf(mapObj).gold -= TreeOfKnowledgeGold;
                            hero.AddExperience(H5HeroState.ExperienceForLevel(hero.Level + 1) - hero.Experience);
                            state.AddLog($"{hero.HeroName} reaches level {hero.Level}.");
                            state.CheckLevelUps(hero);
                        }
                    });
                    request.Options.Add(new H5DialogOption { Label = "Leave", Action = () => { } });
                    state.RequestDialog(request);
                    return true;
                }
                case "BUILDING_WITCH_HUT":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    // The hut teaches one secondary skill at Basic with a perk
                    // of it (shipped Witch Hut behaviour). Candidates are the
                    // class's own pool minus what the hero already knows.
                    var db = state.Database;
                    var cls = db != null ? db.GetHeroClass(hero.HeroClass) : null;
                    var candidates = new List<string>();
                    foreach (var kv in (cls != null ? cls.skillsProbs : new Dictionary<string, int>()))
                    {
                        var skill = db != null ? db.GetSkill(kv.Key) : null;
                        if (skill == null || !skill.IsSkill || hero.SkillRank(kv.Key) > 0) continue;
                        if (kv.Key != hero.RacialSkill && hero.SecondarySkillCount >= H5HeroState.MaxSecondarySkills) continue;
                        candidates.Add(kv.Key);
                    }
                    if (candidates.Count == 0)
                    {
                        state.AddLog("The Witch Hut has nothing left to teach.");
                        return true;
                    }
                    var taught = candidates[state.Rand.Next(candidates.Count)];
                    hero.LearnSkill(taught, 1);
                    var perk = hero.PickPerkFor(taught, db, state.Rand);
                    if (perk != null) hero.LearnPerk(perk);
                    hero.RecalculateEffects(db);
                    var skillDef = db != null ? db.GetSkill(taught) : null;
                    var perkDef = perk != null && db != null ? db.GetSkill(perk) : null;
                    state.AddLog($"The Witch Hut teaches {skillDef?.DisplayName() ?? taught}" +
                                 (perkDef != null ? $" ({perkDef.DisplayName()})" : "") + ".");
                    return true;
                }
                case "BUILDING_SCHOOL_OF_MAGIC":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    var spell = PickSchoolSpell(state);
                    if (spell == null)
                    {
                        state.AddLog("The School of Magic has nothing new to teach.");
                        return true;
                    }
                    hero.LearnSpell(spell.id);
                    state.AddLog($"{hero.HeroName} learns {spell.DisplayName} at the School of Magic.");
                    return true;
                }
                case "BUILDING_WAGON":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    // Inferred: the wagon is a small mixed find.
                    if (state.Rand.Next(100) < 60)
                    {
                        var resource = RareResources[state.Rand.Next(RareResources.Length)];
                        var amount = state.Rand.Next(1, 4);
                        state.TreasuryOf(mapObj).Add(resource, amount);
                        state.AddLog($"The wagon holds {amount} {resource.ToLowerInvariant()}.");
                    }
                    else
                    {
                        var gold = Roll(state, obj) * GoldRollUnit;
                        state.TreasuryOf(mapObj).gold += gold;
                        state.AddLog($"The wagon holds {gold} gold.");
                    }
                    state.CollectObject(obj);
                    return true;
                }
                case "BUILDING_LEAN_TO":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    var resource = RareResources[state.Rand.Next(RareResources.Length)];
                    var amount = state.Rand.Next(1, 4);
                    state.TreasuryOf(mapObj).Add(resource, amount);
                    state.CollectObject(obj);
                    state.AddLog($"The lean-to holds {amount} {resource.ToLowerInvariant()}.");
                    return true;
                }
                case "BUILDING_SKELETON":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
                    var gold = Roll(state, obj) * GoldRollUnit;
                    state.TreasuryOf(mapObj).gold += gold;
                    state.CollectObject(obj);
                    state.AddLog($"The remains hold {gold} gold.");
                    return true;
                }

                // -- once per day -----------------------------------------
                case "BUILDING_MAGIC_WELL":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
                    hero.Mana = hero.MaxMana;
                    state.AddLog($"{hero.HeroName}'s mana is restored.");
                    return true;
                }
                case "BUILDING_FONTAIN_OF_YOUTH":
                    return MovementBonus(state, hero, mapObj, obj,
                        state.Rules != null ? state.Rules.visitFountainOfYouthMovementBonus : 400,
                        "Fountain of Youth");
                case "BUILDING_OASIS":
                    return MovementBonus(state, hero, mapObj, obj,
                        state.Rules != null ? state.Rules.visitOasisMovementBonus : 800,
                        "Oasis");
                case "BUILDING_STABLES":
                    return MovementBonus(state, hero, mapObj, obj,
                        state.Rules != null ? state.Rules.visitStablesMovementBonus : 600,
                        "Stables");
                case "BUILDING_RALLY_FLAG":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
                    var bonus = state.Rules != null ? state.Rules.visitRallyFlagMovementBonus : 400;
                    mapObj.AddMovement(bonus);
                    hero.TemporaryMorale += 1;
                    state.AddLog($"Rally Flag: +{bonus} movement, +1 morale for today.");
                    return true;
                }
                case "BUILDING_FONTAIN_OF_FORTUNE":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
                    hero.TemporaryLuck += 2;
                    state.AddLog("Fountain of Fortune: +2 luck for today.");
                    return true;
                }

                // -- once per week ----------------------------------------
                case "BUILDING_WATER_WHEEL":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerWeek)) return true;
                    state.TreasuryOf(mapObj).gold += WaterWheelGold;
                    state.AddLog($"The Water Wheel pays {WaterWheelGold} gold.");
                    return true;
                }
                case "BUILDING_WINDMILL":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerWeek)) return true;
                    var resource = RareResources[state.Rand.Next(RareResources.Length)];
                    var amount = state.Rand.Next(3, 6);
                    state.TreasuryOf(mapObj).Add(resource, amount);
                    state.AddLog($"The Windmill produces {amount} {resource.ToLowerInvariant()}.");
                    return true;
                }
                case "BUILDING_MAGIC_SPRING":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerWeek)) return true;
                    hero.Mana = hero.MaxMana * 2;
                    state.AddLog("The Magic Spring doubles " + hero.HeroName + "'s mana.");
                    return true;
                }
                case "BUILDING_IDOL_OF_FORTUNE":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerWeek)) return true;
                    hero.TemporaryMorale += 1;
                    hero.TemporaryLuck += 1;
                    state.AddLog("Idol of Fortune: +1 morale and +1 luck for today.");
                    return true;
                }

                // -- until the next battle (approximated to end of day) ----
                case "BUILDING_BUOY":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
                    hero.TemporaryMorale += 1;
                    state.AddLog("Buoy: +1 morale for today.");
                    return true;
                }
                case "BUILDING_MERMAIDS":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
                    hero.TemporaryLuck += 1;
                    state.AddLog("Mermaids: +1 luck for today.");
                    return true;
                }
                case "BUILDING_FAERIE_RING":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
                    hero.TemporaryLuck += 1;
                    state.AddLog("Faerie Ring: +1 luck for today.");
                    return true;
                }
                case "BUILDING_TEMPLE":
                {
                    if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
                    hero.TemporaryMorale += 1;
                    state.AddLog("Temple: +1 morale for today.");
                    return true;
                }

                // -- keys ------------------------------------------------
                case "BUILDING_BORDER_GUARD":
                {
                    var color = obj.keyColor;
                    if (string.IsNullOrEmpty(color))
                    {
                        // The color lives on the guard's shared record name
                        // (BorderGuardRed etc.); fall back to a friendly prompt.
                        state.LogOncePerDay(obj, "A border guard blocks the way.");
                        return true;
                    }
                    if (hero.HasKey(color))
                        state.AddLog($"The {GameDatabase.PrettifyEnum(color)} border guard lets you pass.");
                    else
                        state.AddLog($"The border guard demands the {GameDatabase.PrettifyEnum(color)} key.");
                    return true;
                }
                case "BUILDING_KEYMASTER_TENT":
                    return VisitTent(state, hero, mapObj, obj);

                // -- guarded banks ---------------------------------------
                case "BUILDING_CRYPT":
                case "BUILDING_FORGOTTEN_CRYPT":
                case "BUILDING_GRAVEYARD":
                case "BUILDING_DRAGON_UTOPIA":
                case "BUILDING_NAGA_BANK":
                case "BUILDING_CYCLOPS_STOCKPILE":
                case "BUILDING_DWARVEN_TREASURE":
                case "BUILDING_GARGOYLE_STONEVAULT":
                case "BUILDING_SUNKEN_TEMPLE":
                case "BUILDING_TOMB_OF_THE_WARRIOR":
                case "BUILDING_PYRAMID":
                case "BUILDING_RUINED_TOWER":
                    state.AddLog($"Battle for the {DisplayName(obj)}!");
                    state.RequestCombat(mapObj, state.ViewOf(obj), BankGuards(sharedType, state.Rand));
                    return true;
                case "BUILDING_HILL_FORT":
                case "BUILDING_BLACK_MARKET":
                case "BUILDING_TRADING_POST":
                case "BUILDING_TAVERN":
                case "BUILDING_WARMACHINE_FACTORY":
                case "BUILDING_SANCTUARY":
                case "BUILDING_WAR_ACADEMY":
                    state.LogOncePerDay(obj, "The " + DisplayName(obj) + " screen is not implemented yet.");
                    return true;
                case "BUILDING_EYE_OF_MAGI":
                case "BUILDING_HUT_OF_MAGI":
                case "BUILDING_REDWOORD_OBSERVATORY":
                case "BUILDING_CARTOGRAPHER":
                case "BUILDING_OBELISK":
                    state.LogOncePerDay(obj, "Map revealing is not implemented yet (no fog of war).");
                    return true;
                case "BUILDING_MONOLITH_ONE_WAY_ENTRANCE":
                case "BUILDING_MONOLITH_ONE_WAY_EXIT":
                case "BUILDING_MONOLITH_TWO_WAY":
                    state.LogOncePerDay(obj, "Monolith teleport is not implemented yet.");
                    return true;
                case "BUILDING_SUBTERRA_GATE":
                    state.LogOncePerDay(obj, "The subterranean gate switches floors (F1).");
                    return true;
                case "BUILDING_LIGHTHOUSE":
                    state.LogOncePerDay(obj, "Sea movement is not implemented yet.");
                    return true;
                case "BUILDING_SHIP_GALEON":
                case "BUILDING_WHIRLPOOL":
                    state.LogOncePerDay(obj, "Sea objects are not implemented yet.");
                    return true;
                case "BUILDING_SPELL":
                case "BUILDING_GARRISON":
                case "BUILDING_GARRISON_ANTIMAGIC":
                    state.LogOncePerDay(obj, "Not implemented yet.");
                    return true;

                // -- creature-dwelling style posts -------------------------
                default:
                    if (sharedType.StartsWith("BUILDING_") &&
                        (sharedType.Contains("MILITARY_POST") || sharedType.Contains("BARRACKS") ||
                         sharedType.Contains("KENNELS") || sharedType.Contains("CRUCIBLE") ||
                         sharedType.Contains("CABINS") || sharedType.Contains("HOUSE") ||
                         sharedType.Contains("HUT") || sharedType.Contains("QUARTERS") ||
                         sharedType.Contains("DEMON_GATE") || sharedType.Contains("BLOOD_TEMPLE")))
                    {
                        state.LogOncePerDay(obj, "Recruiting from the " + DisplayName(obj) + " is not implemented yet.");
                        return true;
                    }
                    return false;
            }
        }

        // -- helpers -------------------------------------------------------

        /// <summary>
        /// Standing guard of a guarded treasury. The shipped maps say a bank
        /// is guarded but the exact rosters are scenario-scripted in the
        /// original's Lua, so these are the classic HoMM5 garrisons --
        /// inferred and tweakable here. Sizes scale a little with the roll.
        /// </summary>
        public static List<ArmyStackState> BankGuards(string sharedType, System.Random rng)
        {
            var guards = new List<ArmyStackState>();
            void Add(string creature, int count)
            {
                if (count > 0) guards.Add(new ArmyStackState(creature, count));
            }
            var jitter = rng != null ? rng.Next(0, 4) : 0;
            switch ((sharedType ?? "").ToUpperInvariant())
            {
                case "BUILDING_CRYPT":
                    Add("Skeleton", 30 + jitter * 2);
                    Add("Walking_Dead", 25 + jitter);
                    Add("Ghosts", 15 + jitter);
                    Add("Vampire", 8 + jitter / 2);
                    break;
                case "BUILDING_FORGOTTEN_CRYPT":
                    Add("Lich", 12 + jitter);
                    Add("Vampire", 12 + jitter);
                    Add("Ghosts", 20 + jitter);
                    break;
                case "BUILDING_GRAVEYARD":
                    Add("Zombie", 35 + jitter * 2);
                    Add("Ghosts", 20 + jitter);
                    Add("Wight", 15 + jitter);
                    break;
                case "BUILDING_DRAGON_UTOPIA":
                    Add("Black_Dragon", 2);
                    Add("Gold_Dragon", 2);
                    Add("Fire_Dragon", 2);
                    Add("Green_Dragon", 2);
                    break;
                case "BUILDING_NAGA_BANK":
                    Add("Hydra", 10 + jitter);
                    Add("Water_Elemental", 20 + jitter);
                    break;
                case "BUILDING_PYRAMID":
                    Add("Mummy", 20 + jitter * 2);
                    break;
                case "BUILDING_SUNKEN_TEMPLE":
                    Add("Water_Elemental", 25 + jitter);
                    Add("Hydra", 6 + jitter / 2);
                    break;
                case "BUILDING_TOMB_OF_THE_WARRIOR":
                    Add("Skeleton", 40 + jitter * 2);
                    Add("Skeleton_Archer", 25 + jitter);
                    break;
                case "BUILDING_CYCLOPS_STOCKPILE":
                    Add("Orc1", 25 + jitter);
                    Add("OrcWarmonger", 15 + jitter);
                    break;
                case "BUILDING_DWARVEN_TREASURE":
                    Add("WhiteBearRider", 12 + jitter);
                    Add("Bear_Raider", 15 + jitter);
                    break;
                case "BUILDING_GARGOYLE_STONEVAULT":
                    Add("Gremlin", 30 + jitter * 2);
                    Add("Master_Gremlin", 20 + jitter);
                    break;
                case "BUILDING_RUINED_TOWER":
                    Add("Sprite", 25 + jitter);
                    Add("Manticore", 10 + jitter);
                    break;
                case "TREASURE_SOULSTONE":
                case "TREASURE_SHIPWRECK":
                    Add("Ghosts", 25 + jitter);
                    Add("Poltergeist", 15 + jitter);
                    break;
                default:
                    // Unknown bank: a modest skeleton band keeps it honest.
                    Add("Skeleton", 20 + jitter);
                    break;
            }
            return guards;
        }

        /// <summary>Once-ever stat or skill grant.</summary>
        static bool Permanent(AdventureMapGameState state, H5HeroState hero, MapObjectDef obj,
                              string what, Action apply)
        {
            if (!state.TryBeginVisit(obj, VisitFrequency.OnceEver)) return true;
            apply();
            state.AddLog($"{hero.HeroName} visits the {DisplayName(obj)}: {what}.");
            return true;
        }

        /// <summary>Once-per-day movement boost from DefaultStats.xdb.</summary>
        static bool MovementBonus(AdventureMapGameState state, H5HeroState hero, H5MapObject mapObj,
                                  MapObjectDef obj, int bonus, string place)
        {
            if (!state.TryBeginVisit(obj, VisitFrequency.OncePerDay)) return true;
            mapObj.AddMovement(bonus);
            state.AddLog($"{place}: +{bonus} movement.");
            return true;
        }

        static SpellDef PickSchoolSpell(AdventureMapGameState state)
        {
            if (state.Database == null || state.Database.Spells == null) return null;
            var candidates = new List<SpellDef>();
            foreach (var kv in state.Database.Spells)
            {
                var s = kv.Value;
                if (s != null && s.level >= 1 && s.level <= 2) candidates.Add(s);
            }
            if (candidates.Count == 0) return null;
            return candidates[state.Rand.Next(candidates.Count)];
        }

        static int Roll(AdventureMapGameState state, MapObjectDef obj)
        {
            var min = obj.minResource;
            var max = Mathf.Max(min, obj.maxResource);
            if (max <= 0) return min > 0 ? min : 0;
            return state.Rand.Next(min, max + 1);
        }

        static string DisplayName(MapObjectDef obj)
        {
            if (!string.IsNullOrEmpty(obj.name)) return obj.name;
            if (!string.IsNullOrEmpty(obj.sharedType)) return GameDatabase.PrettifyEnum(obj.sharedType);
            return GameDatabase.PrettifyEnum(obj.kind);
        }
    }
}
