using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HoMM5.Core;
using HoMM5.Data;
using HoMM5.Map;
using HoMM5.UI;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HoMM5.Tests
{
    /// <summary>
    /// Synthetic, disposable import: no original game assets, network or user
    /// save files required. These tests must run in Unity's PlayMode runner.
    /// They are regressions, not a claim of compatibility with the native game.
    /// </summary>
    public class SingleMissionRegressionTests
    {
        string _import;
        string _previousImport;
        GameObject _host;
        GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _previousImport = H5Paths.Root;
            _import = Path.Combine(Path.GetTempPath(), "homm5-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_import, "Game"));
            Directory.CreateDirectory(Path.Combine(_import, "Maps"));
            H5Paths.SetOverride(_import);
            // Supply the entire database envelope, rather than relying on a
            // partial import being treated as complete by GameDatabase.Load.
            foreach (var file in new[] { "creatures", "spells", "artifacts", "townTypes", "warMachines",
                "arenaBuildings", "skills", "heroClasses", "heroes", "modelAnimations", "Localization",
                "rules", "sounds" })
                File.WriteAllText(H5Paths.Game(file + ".json"), "{}");
            File.WriteAllText(H5Paths.Game("townBuildings.json"), JsonConvert.SerializeObject(new[]
            {
                new TownBuildingDef { key = "Haven/Dwelling", faction = "Haven", type = "TB_DWELLING_1",
                    upgrade = "BLD_UPG_1", creature = "TEST_HAVEN", growth = 12 },
                new TownBuildingDef { key = "Academy/Dwelling", faction = "Academy", type = "TB_DWELLING_1",
                    upgrade = "BLD_UPG_1", creature = "TEST_ACADEMY", growth = 7 }
            }));
            File.WriteAllText(H5Paths.Maps("_index.json"), "[]");
            _db = new GameDatabase();
            _db.Load();
            AddTownType("TOWN_HEAVEN", "Haven");
            AddTownType("TOWN_ACADEMY", "Academy");
            _host = new GameObject("SingleMissionTest");
        }

        void AddTownType(string id, string faction)
        {
            _db.TownTypes[id] = new TownTypeDef
            {
                id = id, faction = faction,
                buildings = new List<string> { faction + "/TownHall" }
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            H5Paths.SetOverride(_previousImport);
            Directory.Delete(_import, true);
        }

        [UnityTest]
        public IEnumerator BootstrapOpensMenuAndMissingScenarioDoesNotDismissIt()
        {
            var bootstrap = _host.AddComponent<H5Bootstrap>();
            yield return null;
            var menu = _host.GetComponentInChildren<MainMenu>(true);
            var hud = _host.GetComponent<HoMM5Hud>();
            Assert.That(bootstrap.Database.IsLoaded, Is.True);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(hud.IsVisible, Is.False);
            Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text == "New Game"), Is.True);
            var controller = Object.FindAnyObjectByType<AdventureMapController>();
            Assert.That(controller.enabled, Is.False);

            bootstrap.StartNewGame(new NewGameSetup { MapId = "NotImported" });
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(bootstrap.CurrentMap, Is.Null);
            Assert.That(bootstrap.GameState.Day, Is.EqualTo(1));
            Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text.Contains("Cannot open scenario")), Is.True);
        }

        [UnityTest]
        public IEnumerator HidingAdventureHudDoesNotHideMainMenu()
        {
            var hud = _host.AddComponent<HoMM5Hud>();
            var menu = MainMenu.Create(hud.CanvasRoot, _db);
            hud.Hide();
            yield return null;

            Assert.That(hud.IsVisible, Is.False);
            Assert.That(hud.CanvasRoot.gameObject.activeInHierarchy, Is.True);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(menu.GetComponent<RectTransform>(), Is.Not.Null);
            Assert.That(menu.GetComponentsInChildren<Text>().Any(t => t.text == "New Game"), Is.True,
                "The database must be assigned BEFORE the first menu render.");
            Assert.That(menu.transform.Find("MenuOverlay").GetComponent<Image>().raycastTarget, Is.True);

            menu.Hide();
            hud.Show();
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(hud.IsVisible, Is.True);
        }

        [UnityTest]
        public IEnumerator MenuRowsAndStartButtonFitTheirContent()
        {
            var hud = _host.AddComponent<HoMM5Hud>();
            _db.MapIndex.Add(new MapIndexEntry { id = "Duel", tileX = 16, tileY = 16 });
            _db.Heroes["Academy/Test"] = new HeroDef
            {
                id = "Academy/Test", name = "Test Hero", townType = "TOWN_ACADEMY"
            };
            var menu = MainMenu.Create(hud.CanvasRoot, _db);
            NewGameSetup chosen = null;
            menu.StartRequested = setup => chosen = setup;
            yield return ClickRow(menu, "New Game");
            yield return ClickRow(menu, "Duel");
            yield return ClickRow(menu, "Academy");
            yield return ClickRow(menu, "Test Hero");
            Canvas.ForceUpdateCanvases();

            var content = menu.GetComponentInChildren<ScrollRect>().content;
            var start = content.Find("StartButton").GetComponent<Button>();
            Assert.That(content.rect.height, Is.EqualTo(4 * 52 + 80));
            var title = menu.GetComponentsInChildren<Text>().First(t => t.name == "Title");
            Assert.That(title.rectTransform.rect.height, Is.GreaterThan(0));
            Assert.That(start.gameObject.activeInHierarchy, Is.True);
            start.onClick.Invoke();
            Assert.That(chosen, Is.Not.Null);
            Assert.That(chosen.MapId, Is.EqualTo("Duel"));
            Assert.That(chosen.TownType, Is.EqualTo("TOWN_ACADEMY"));
            Assert.That(chosen.HeroId, Is.EqualTo("Academy/Test"));
        }

        [UnityTest]
        public IEnumerator LongMapListIsNotClippedByAFixedContentHeight()
        {
            var hud = _host.AddComponent<HoMM5Hud>();
            for (var i = 0; i < 60; i++) _db.MapIndex.Add(new MapIndexEntry { id = "Map" + i });
            var menu = MainMenu.Create(hud.CanvasRoot, _db);
            yield return ClickRow(menu, "New Game");
            var content = menu.GetComponentInChildren<ScrollRect>().content;
            Assert.That(content.sizeDelta.y, Is.EqualTo(61 * 52 + 80)); // includes tip
        }

        static IEnumerator ClickRow(MainMenu menu, string label)
        {
            var text = menu.GetComponentsInChildren<Text>().First(t => t.text == label && t.name == "Label");
            text.GetComponentInParent<Button>().onClick.Invoke();
            // Clear() uses Destroy: do not search rows queued for deletion.
            yield return null;
        }

        static MapObjectDef TownDef(string owner = "PLAYER_1")
        {
            return new MapObjectDef
            {
                objectId = "town-1", kind = "AdvMapTown", owner = owner,
                townType = "TOWN_HEAVEN",
                buildings = new List<MapBuildingStateDef>
                {
                    new MapBuildingStateDef
                    { type = "TB_TOWN_HALL", upgrade = "BLD_UPG_1", maxUpgrade = "BLD_UPG_5" },
                    new MapBuildingStateDef
                    { type = "TB_DWELLING_1", upgrade = "BLD_UPG_1", maxUpgrade = "BLD_UPG_3" }
                }
            };
        }

        [Test]
        public void ChosenTownFactionSurvivesFreshMapAndSaveRoundTrip()
        {
            var town = H5TownState.FromMapObject(TownDef(), _db);
            Assert.That(town.SetStartingFaction(_db, "TOWN_ACADEMY"), Is.True);
            var restored = JsonConvert.DeserializeObject<H5TownState>(JsonConvert.SerializeObject(town));
            restored.SyncFromMap(_db, TownDef());
            Assert.That(restored.TownType, Is.EqualTo("TOWN_ACADEMY"));
            Assert.That(restored.Faction, Is.EqualTo("Academy"));
        }

        [Test]
        public void BuildingSlotsDoNotAliasAuthoredMapSlots()
        {
            var def = TownDef();
            var town = H5TownState.FromMapObject(def, _db);
            town.Slots[0].upgrade = "BLD_UPG_2";
            Assert.That(def.buildings[0].upgrade, Is.EqualTo("BLD_UPG_1"));
            town.SyncFromMap(_db, def);
            town.Slots[0].upgrade = "BLD_UPG_3";
            Assert.That(def.buildings[0].upgrade, Is.EqualTo("BLD_UPG_1"));
        }

        [Test]
        public void MatchSetupHonoursFactionAndDoesNotGiveNeutralTownsToComputer()
        {
            var humanTown = H5TownState.FromMapObject(TownDef(), _db);
            var neutralDef = TownDef("PLAYER_NONE");
            neutralDef.objectId = "neutral";
            var neutral = H5TownState.FromMapObject(neutralDef, _db);
            var state = new AdventureMapGameState { Database = _db };
            state.RestoreState(new H5SaveGame { towns = new List<H5TownState> { humanTown, neutral } });
            state.SetupMatch(new NewGameSetup { Faction = "Academy", TownType = "TOWN_ACADEMY", Difficulty = 3 });
            Assert.That(humanTown.TownType, Is.EqualTo("TOWN_ACADEMY"));
            Assert.That(state.LocalPlayer.Faction, Is.EqualTo("Academy"));
            Assert.That(neutral.Owner, Is.EqualTo("PLAYER_NONE"));
            Assert.That(state.Players.Find("PLAYER_2").Difficulty, Is.EqualTo(3));
            Assert.That(humanTown.PoolOf("TB_DWELLING_1"), Is.EqualTo(7),
                "New-game stock must come from the chosen faction, before day 8.");
        }

        [Test]
        public void ChosenHeroIsCapturedBeforeOpeningAnyHeroScreen()
        {
            var state = new AdventureMapGameState { Database = _db };
            var hero = _host.AddComponent<H5MapObject>();
            hero.Attach(new MapObjectDef { kind = "AdvMapHero", objectId = "hero", owner = "PLAYER_1" }, null);
            // Only seed scene binding here: the assertion exercises the public
            // roster-selection/save API, without requiring imported meshes.
            typeof(H5MapObject).GetMethod("BindMovement", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hero, new object[] { state, "hero", 2500f, true });
            var heroes = (List<H5MapObject>)typeof(AdventureMapGameState)
                .GetField("_heroes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
            heroes.Add(hero);
            _db.Heroes["Academy/Selected"] = new HeroDef
            {
                id = "Academy/Selected", name = "Selected Hero", heroClass = "HERO_CLASS_WIZARD",
                stats = new MapHeroPrimarySkills { offence = 2, defence = 3, spellpower = 4, knowledge = 5 }
            };
            state.SetupMatch(new NewGameSetup { HeroId = "Academy/Selected" });
            var saved = JsonConvert.DeserializeObject<H5SaveGame>(JsonConvert.SerializeObject(state.CaptureState()));
            Assert.That(saved.heroes.Count, Is.EqualTo(1));
            Assert.That(saved.heroes[0].HeroName, Is.EqualTo("Selected Hero"));
            Assert.That(saved.heroes[0].Knowledge, Is.EqualTo(5));
            Assert.That(hero.HeroState, Is.Not.Null);
            Assert.That(hero.HeroState.Mana, Is.EqualTo(hero.HeroState.MaxMana));
        }

        [Test]
        public void OlderSaveWithoutPlayersDoesNotInheritPreviousMatchRoster()
        {
            var state = new AdventureMapGameState { Database = _db };
            state.SetupMatch(new NewGameSetup());
            Assert.That(state.Players.Players.Count, Is.GreaterThan(0));
            state.RestoreState(new H5SaveGame { players = null });
            Assert.That(state.Players.Players, Is.Empty);
        }

        [Test]
        public void NewScenarioClearsPreviousMatchTownsAndPlayers()
        {
            var state = new AdventureMapGameState { Database = _db };
            state.RestoreState(new H5SaveGame
            {
                towns = new List<H5TownState> { H5TownState.FromMapObject(TownDef(), _db) }
            });
            state.SetupMatch(new NewGameSetup());
            state.BindMap(null, true, "PLAYER_1", _db.Rules, _db.TownBuildings, 2);
            Assert.That(state.AllTowns(), Is.Empty);
            Assert.That(state.Players.Players, Is.Empty);
        }
    }
}
