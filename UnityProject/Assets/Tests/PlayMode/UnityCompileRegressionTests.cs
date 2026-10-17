using System;
using System.Collections.Generic;
using HoMM5.Anim;
using HoMM5.Combat;
using HoMM5.Data;
using HoMM5.Map;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HoMM5.Tests
{
    /// <summary>Behaviour around the API/type mismatches reported by the Unity compiler.</summary>
    public class UnityCompileRegressionTests
    {
        [TestCase(0f)]
        [TestCase(2f)]
        public void QuaternionTrackNormalizesAndHandlesZeroLength(float scale)
        {
            var curve = new H5Curve
            {
                width = 4, knots = new[] { 0f },
                controls = new[] { scale, 0f, 0f, scale }
            };
            var rotation = H5Anim.SampleQuat(curve, 0f);
            Assert.That(Quaternion.Dot(rotation, rotation), Is.EqualTo(1f).Within(0.00001f));
            if (scale == 0f) Assert.That(rotation, Is.EqualTo(Quaternion.identity));
            else Assert.That(rotation.x, Is.EqualTo(rotation.w).Within(0.00001f));
        }

        [Test]
        public void BattleReadsGarrisonAsAListOfStacks()
        {
            var go = new GameObject("GarrisonRegression");
            try
            {
                var obj = go.AddComponent<H5MapObject>();
                obj.Attach(new MapObjectDef
                {
                    kind = "AdvMapTown",
                    garrison = new List<MapArmyStackDef>
                    {
                        new MapArmyStackDef { creature = "First", count = 3 },
                        new MapArmyStackDef { creature = "Second", amount = 6 }
                    }
                }, null);
                var battle = CombatGameState.Create(null, obj, null, new System.Random(1));
                Assert.That(battle.Units.Count, Is.EqualTo(2));
                Assert.That(battle.Units[0].Count, Is.EqualTo(3));
                Assert.That(battle.Units[1].Count, Is.EqualTo(6));
                Assert.That(battle.Units.TrueForAll(u => u.Side == CombatSide.Defender), Is.True);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void SiegeDetectionHandlesNullEmptyAndPopulatedGarrisons()
        {
            Assert.That(AdventureMapGameState.IsSiegeFight(null), Is.False);
            var def = new MapObjectDef { kind = "AdvMapBuilding", garrison = new List<MapArmyStackDef>() };
            Assert.That(AdventureMapGameState.IsSiegeFight(def), Is.False);
            def.garrison.Add(new MapArmyStackDef { creature = "Guard", count = 1 });
            Assert.That(AdventureMapGameState.IsSiegeFight(def), Is.True);
            def.kind = "AdvMapDwelling";
            Assert.That(AdventureMapGameState.IsSiegeFight(def), Is.False);
        }

        [Test]
        public void RegenerationUsesTheShippedHealingRange()
        {
            var unit = new CombatUnit
            {
                Count = 1, StartCount = 1, HealthLeft = 1,
                Def = new CreatureDef { Health = 100, abilities = new List<string> { "ABILITY_REGENERATION" } }
            };
            var message = CombatAbilities.OnTurnStart(new System.Random(1), unit);
            Assert.That(message, Does.Contain("regenerates"));
            Assert.That(unit.HealthLeft, Is.InRange(31, 51));
        }

        [Test]
        public void MakingInventoryUnitySerializablePreservesPlayerSaveResources()
        {
            Assert.That(Attribute.IsDefined(typeof(PlayerResourceInventory), typeof(SerializableAttribute)), Is.True);
            var player = new H5PlayerState();
            player.Resources.gold = 1234;
            player.Resources.wood = 17;
            var restored = JsonConvert.DeserializeObject<H5PlayerState>(JsonConvert.SerializeObject(player));
            Assert.That(restored.Resources.gold, Is.EqualTo(1234));
            Assert.That(restored.Resources.wood, Is.EqualTo(17));
        }
    }
}
