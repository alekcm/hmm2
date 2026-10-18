using HoMM5.Data;
using HoMM5.World;
using UnityEngine;

namespace HoMM5.Map
{
    /// <summary>
    /// The game-side identity of a placed map object.
    ///
    /// The scene only needs position and mesh; this is what makes the object
    /// answer a question about itself -- what it is, who owns it, how many
    /// creatures are in it -- which is what the HUD and any future combat code
    /// ask.
    /// </summary>
    public class H5MapObject : MonoBehaviour
    {
        public MapObjectDef Def { get; private set; }
        public MapSceneBuilder Builder { get; private set; }
        public GameDatabase Database { get; private set; }

        public string Kind => Def != null ? Def.kind : "";
        public bool IsMonster => Kind == "AdvMapMonster";
        public bool IsTown => Kind == "AdvMapTown";
        public bool IsHero => Kind == "AdvMapHero";
        public bool IsArtifact => Kind == "AdvMapArtifact";

        public void Attach(MapObjectDef def, MapSceneBuilder builder)
        {
            Def = def;
            Builder = builder;
        }

        public void AttachDatabase(GameDatabase db) => Database = db;

        public CreatureDef Creature => Database != null ? Database.GetCreature(Def != null ? Def.creature : null) : null;
        public ArtifactDef Artifact => Database != null ? Database.GetArtifact(Def != null ? Def.artifact : null) : null;

        /// <summary>One-line summary for the HUD.</summary>
        public string Summary()
        {
            if (Def == null) return "";
            switch (Kind)
            {
                case "AdvMapMonster":
                {
                    var c = Creature;
                    var name = c != null ? c.DisplayName : Def.creature;
                    var count = Def.amount2 != 0 && Def.amount2 != Def.amount
                        ? Def.amount + "-" + Def.amount2
                        : Def.amount.ToString();
                    return $"{name} x{count}";
                }
                case "AdvMapTown":
                    return string.IsNullOrEmpty(Def.name) ? Def.townType : Def.name;
                case "AdvMapHero":
                    return Def.hero ?? "Hero";
                case "AdvMapArtifact":
                {
                    var a = Artifact;
                    return a != null ? a.DisplayName : (Def.artifact ?? "Artifact");
                }
                case "AdvMapMine":
                    return (Def.resource ?? "Mine") + (string.IsNullOrEmpty(Def.owner) ? "" : " (" + Def.owner + ")");
                case "AdvMapTreasure":
                    return "Treasure";
                default:
                    return string.IsNullOrEmpty(Def.name) ? Kind : Def.name;
            }
        }

        /// <summary>Multi-line detail for the inspector panel.</summary>
        public string Details()
        {
            if (Def == null) return "";
            var c = Creature;
            if (IsMonster && c != null)
            {
                var stackCount = Def.customAmount && Def.amount > 0 ? Def.amount : 1;
                var xpValue = NWorldCombat.CalcUnitExperienceValue(c, stackCount, false);
                var stackLines = $"Amount {Def.amount} / {Def.amount2}  Custom {Def.customAmount}\n" +
                                  $"Attack type {Def.attackType}  Mood {Def.mood}\n";
                if (Def.additionalStacks != null)
                    for (var i = 0; i < Def.additionalStacks.Count; i++)
                        stackLines += $"Additional {Def.additionalStacks[i].creature} " +
                                      $"{Def.additionalStacks[i].amount} / {Def.additionalStacks[i].amount2}\n";
                return $"{c.DisplayName}\n" + stackLines +
                       $"Tier {c.CreatureTier}  {GameDatabase.PrettifyEnum(c.town)}\n" +
                       $"Attack {c.AttackSkill}  Defence {c.DefenceSkill}\n" +
                       $"Damage {c.DamageText}  Health {c.Health}\n" +
                       $"Speed {c.Speed}  Initiative {c.Initiative}  XP {xpValue}\n" +
                       $"Cost {c.cost}\n" +
                       (string.IsNullOrEmpty(c.Description) ? "" : "\n" + c.Description);
            }

            var a = Artifact;
            if (IsArtifact && a != null)
            {
                return $"{a.DisplayName}\n" +
                       $"{GameDatabase.PrettifyEnum(a.artifactClass)}  slot {GameDatabase.PrettifyEnum(a.slot)}\n" +
                       $"Value {a.price} gold\n" +
                       (string.IsNullOrEmpty(a.description) ? "" : "\n" + a.description);
            }

            if (IsHero)
            {
                var lines = new System.Collections.Generic.List<string>
                {
                    string.IsNullOrEmpty(Def.hero) ? "Hero" : Def.hero,
                    "Owner " + (string.IsNullOrEmpty(Def.owner) ? "PLAYER_NONE" : Def.owner),
                    "Experience " + Def.experience,
                    "Attack " + Def.heroAttack + "  Defence " + Def.heroDefence,
                    "Spell Power " + Def.heroSpellPower + "  Knowledge " + Def.heroKnowledge
                };
                if (Def.army != null)
                {
                    for (var i = 0; i < Def.army.Count; i++)
                    {
                        var stack = Def.army[i];
                        var creature = CreatureName(stack.creature);
                        lines.Add("Army " + creature + " x" + stack.count);
                    }
                }
                if (Def.heroSkills != null && Def.heroSkills.Count > 0)
                    lines.Add("Skills " + string.Join(", ", Def.heroSkills));
                if (Def.heroPerks != null && Def.heroPerks.Count > 0)
                    lines.Add("Perks " + string.Join(", ", Def.heroPerks));
                if (Def.heroSpells != null && Def.heroSpells.Count > 0)
                    lines.Add("Spells " + string.Join(", ", Def.heroSpells));
                return string.Join("\n", lines);
            }

            return Summary();
        }

        string CreatureName(string id)
        {
            if (Database != null)
            {
                var fromId = Database.GetCreature(id);
                if (fromId != null) return fromId.DisplayName;
            }
            return id;
        }
    }
}
