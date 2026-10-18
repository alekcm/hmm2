// ============================================================================
// Decompiled Lua C-API Registration Table (bin/H5_Game_PEST.exe)
// Total unique Lua C-API functions: 314
// Registration tables reside in .data (0x00f2def0 .. 0x00f7bd48).
// ============================================================================

#include <cstdint>

struct lua_State;
typedef int (*lua_CFunction)(lua_State* L);

struct SLuaRegistrationEntry {
    const char*   name;
    lua_CFunction func;
    uint32_t      table_va;
};

// VA: 0x00568d90 | RegTable: 0x00f32f18 // calls: 0x00401b20, 0x00401b20
int Lua_AddCreature(lua_State* L); // 0x00568d90
// VA: 0x005c6140 | RegTable: 0x00f34ce0
int Lua_AddHeroCreatures(lua_State* L); // 0x005c6140
// VA: 0x005ccec0 | RegTable: 0x00f34dc8
int Lua_AddObjectCreatures(lua_State* L); // 0x005ccec0
// VA: 0x005e3d40 | RegTable: 0x00f35210 // calls: 0x00401b20, 0x00401b20
int Lua_AllowHeroHiringByRaceForAI(lua_State* L); // 0x005e3d40
// VA: 0x005e4270 | RegTable: 0x00f35220 // calls: 0x00401b20, 0x00401b20
int Lua_AllowHeroHiringByRaceInTown(lua_State* L); // 0x005e4270
// VA: 0x005e4000 | RegTable: 0x00f35218 // calls: 0x00401b20, 0x00401b20
int Lua_AllowHiringOfHeroForAI(lua_State* L); // 0x005e4000
// VA: 0x005e4530 | RegTable: 0x00f35228 // calls: 0x00401b20, 0x00401b20
int Lua_AllowHiringOfHeroInTown(lua_State* L); // 0x005e4530
// VA: 0x005e51f0 | RegTable: 0x00f35248 // calls: 0x00401b20, 0x00401b20
int Lua_AllowOpenFogOfWarForAlly(lua_State* L); // 0x005e51f0
// VA: 0x005e4ac0 | RegTable: 0x00f35238 // calls: 0x00401b20, 0x00401b20
int Lua_AllowPlayerTavernHero(lua_State* L); // 0x005e4ac0
// VA: 0x005e47f0 | RegTable: 0x00f35230 // calls: 0x00401b20, 0x00401b20
int Lua_AllowPlayerTavernRace(lua_State* L); // 0x005e47f0
// VA: 0x005deeb0 | RegTable: 0x00f35120 // calls: 0x00401b20, 0x00401b20
int Lua_BlockGame(lua_State* L); // 0x005deeb0
// VA: 0x005e3aa0 | RegTable: 0x00f35208 // calls: 0x00401b20, 0x00401b20
int Lua_BlockTownGarrisonForAI(lua_State* L); // 0x005e3aa0
// VA: 0x00567660 | RegTable: 0x00f32ec0 // calls: 0x00401b20, 0x00401b20
int Lua_Break(lua_State* L); // 0x00567660
// VA: 0x005cfdc0 | RegTable: 0x00f34e48
int Lua_CalcAverageMonstersTier(lua_State* L); // 0x005cfdc0
// VA: 0x005ceaf0 | RegTable: 0x00f34e10 // calls: 0x00401b20, 0x00401b20
int Lua_CalcHeroMoveCost(lua_State* L); // 0x005ceaf0
// VA: 0x00cad310 | RegTable: 0x00f7bd40 // calls: 0x00401b20, 0x00401b20
int Lua_Callback(lua_State* L); // 0x00cad310
// VA: 0x005ce790 | RegTable: 0x00f34e08
int Lua_CanMoveHero(lua_State* L); // 0x005ce790
// VA: 0x005e8060 | RegTable: 0x00f35350
int Lua_CanShowToPlayer(lua_State* L); // 0x005e8060
// VA: 0x005c3620 | RegTable: 0x00f34c88 // calls: 0x00401b20, 0x00401b20
int Lua_ChangeHeroStat(lua_State* L); // 0x005c3620
// VA: 0x005d2560 | RegTable: 0x00f34ef8 // calls: 0x00401b20, 0x00401b20
int Lua_ControlHeroCustomAbility(lua_State* L); // 0x005d2560
// VA: 0x005c8730 | RegTable: 0x00f34d58 // calls: 0x00401b20
int Lua_CreateArtifact(lua_State* L); // 0x005c8730
// VA: 0x005e32c0 | RegTable: 0x00f351f0 // calls: 0x00401b20, 0x00401b20
int Lua_CreateCaravan(lua_State* L); // 0x005e32c0
// VA: 0x005c9530 | RegTable: 0x00f34ea8 // calls: 0x00401b20
int Lua_CreateDwelling(lua_State* L); // 0x005c9530
// VA: 0x005c8aa0 | RegTable: 0x00f34d60 // calls: 0x00401b20
int Lua_CreateMonster(lua_State* L); // 0x005c8aa0
// VA: 0x005d1810 | RegTable: 0x00f34ea0 // calls: 0x00401b20
int Lua_CreateStatic(lua_State* L); // 0x005d1810
// VA: 0x005d1410 | RegTable: 0x00f34e98 // calls: 0x00401b20
int Lua_CreateTreasure(lua_State* L); // 0x005d1410
// VA: 0x005e22b0 | RegTable: 0x00f351b8 // calls: 0x00401b20, 0x00401b20
int Lua_DenyAIHeroFlee(lua_State* L); // 0x005e22b0
// VA: 0x005e2760 | RegTable: 0x00f351c0 // calls: 0x00401b20, 0x00401b20
int Lua_DenyAIHeroesFlee(lua_State* L); // 0x005e2760
// VA: 0x005d1fc0 | RegTable: 0x00f34ee8 // calls: 0x00401b20, 0x00401b20
int Lua_DenyGarrisonCreaturesTakeAway(lua_State* L); // 0x005d1fc0
// VA: 0x005c27f0 | RegTable: 0x00f34c60 // calls: 0x00401b20
int Lua_DeployReserveHero(lua_State* L); // 0x005c27f0
// VA: 0x005c8140 | RegTable: 0x00f34d48 // calls: 0x00401b20
int Lua_DestroyTownBuildingToLevel(lua_State* L); // 0x005c8140
// VA: 0x005e6a00 | RegTable: 0x00f352a0 // calls: 0x00401b20, 0x00401b20
int Lua_DisableAutoEnterTown(lua_State* L); // 0x005e6a00
// VA: 0x005df710 | RegTable: 0x00f35138 // calls: 0x00401b20, 0x00401b20
int Lua_DisableCameraFollowHeroes(lua_State* L); // 0x005df710
// VA: 0x005df870 | RegTable: 0x00f35318 // calls: 0x00401b20, 0x00401b20
int Lua_DisableCameraFollowHeroesForPlayers(lua_State* L); // 0x005df870
// VA: 0x005e7750 | RegTable: 0x00f352c8 // calls: 0x00401b20, 0x00401b20
int Lua_DoNotGiveTurnToPlayerAIIfNoTownsAndActiveHeroes(lua_State* L); // 0x005e7750
// VA: 0x005da7a0 | RegTable: 0x00f35168 // calls: 0x00401b20, 0x00401b20
int Lua_EnableAIHeroHiring(lua_State* L); // 0x005da7a0
// VA: 0x00567210 | RegTable: 0x00f32ea8 // calls: 0x00401b20, 0x00401b20
int Lua_EnableAutoFinish(lua_State* L); // 0x00567210
// VA: 0x0056e370 | RegTable: 0x00f33080 // calls: 0x00401b20, 0x00401b20
int Lua_EnableCinematicCamera(lua_State* L); // 0x0056e370
// VA: 0x00566b60 | RegTable: 0x00f32e90 // calls: 0x00401b20, 0x00401b20
int Lua_EnableDynamicBattleMode(lua_State* L); // 0x00566b60
// VA: 0x005da500 | RegTable: 0x00f35150 // calls: 0x00401b20, 0x00401b20
int Lua_EnableHeroAI(lua_State* L); // 0x005da500
// VA: 0x00cad4c0 | RegTable: 0x00f7bd48 // calls: 0x00401b20, 0x00401b20
int Lua_Error(lua_State* L); // 0x00cad4c0
// VA: 0x005e1480 | RegTable: 0x00f35008 // calls: 0x00401b20, 0x00401b20
int Lua_ExitGame(lua_State* L); // 0x005e1480
// VA: 0x005673f0 | RegTable: 0x00f32eb8 // calls: 0x00401b20, 0x00401b20
int Lua_Finish(lua_State* L); // 0x005673f0
// VA: 0x005cf8c0 | RegTable: 0x00f34e38 // calls: 0x00401b20, 0x00401b20
int Lua_GenerateMonsters(lua_State* L); // 0x005cf8c0
// VA: 0x005d7f20 | RegTable: 0x00f35030 // calls: 0x00401b20, 0x00401b20
int Lua_GetAllNames(lua_State* L); // 0x005d7f20
// VA: 0x005c57c0 | RegTable: 0x00f34eb8 // calls: 0x00401b20
int Lua_GetArtifactSetItemsCount(lua_State* L); // 0x005c57c0
// VA: 0x00568b60 | RegTable: 0x00f32f10 // calls: 0x00401b20, 0x00401b20
int Lua_GetBuildingType(lua_State* L); // 0x00568b60
// VA: 0x005686e0 | RegTable: 0x00f32f00 // calls: 0x00401b20, 0x00401b20
int Lua_GetCreatureNumber(lua_State* L); // 0x005686e0
// VA: 0x005684b0 | RegTable: 0x00f32ef8 // calls: 0x00401b20, 0x00401b20
int Lua_GetCreatureType(lua_State* L); // 0x005684b0
// VA: 0x005d2cc0 | RegTable: 0x00f34f10 // calls: 0x00401b20, 0x00401b20
int Lua_GetCurrentMoonWeek(lua_State* L); // 0x005d2cc0
// VA: 0x005d73d0 | RegTable: 0x00f35028 // calls: 0x00401b20, 0x00401b20
int Lua_GetCurrentPlayer(lua_State* L); // 0x005d73d0
// VA: 0x005d7170 | RegTable: 0x00f35020 // calls: 0x00401b20, 0x00401b20
int Lua_GetDate(lua_State* L); // 0x005d7170
// VA: 0x006b5730 | RegTable: 0x00f39a0c // calls: 0x00401b20
int Lua_GetDifficulty(lua_State* L); // 0x006b5730
// VA: 0x0056a3f0 | RegTable: 0x00f32f60 // calls: 0x00401b20, 0x00401b20
int Lua_GetGameVar(lua_State* L); // 0x0056a3f0
// VA: 0x005dea90 | RegTable: 0x00f35110 // calls: 0x00401b20, 0x00401b20
int Lua_GetGameVar(lua_State* L); // 0x005dea90
// VA: 0x00663f40 | RegTable: 0x00f38144 // calls: 0x00401b20, 0x00401b20
int Lua_GetGameVar(lua_State* L); // 0x00663f40
// VA: 0x004c1b00 | RegTable: 0x00f2df40 // calls: 0x00401ab0, 0x00401ab0
int Lua_GetGlobalVar(lua_State* L); // 0x004c1b00
// VA: 0x005e0c30 | RegTable: 0x00f35188
int Lua_GetGuardsTier(lua_State* L); // 0x005e0c30
// VA: 0x005c4f70 | RegTable: 0x00f34cc0 // calls: 0x00401b20, 0x00401b20
int Lua_GetHeroArtifactsCount(lua_State* L); // 0x005c4f70
// VA: 0x005c5ed0 | RegTable: 0x00f34cd8 // calls: 0x00401b20, 0x00401b20
int Lua_GetHeroCreatures(lua_State* L); // 0x005c5ed0
// VA: 0x005ccb70 | RegTable: 0x00f34ee0
int Lua_GetHeroCreaturesTypes(lua_State* L); // 0x005ccb70
// VA: 0x005c3190 | RegTable: 0x00f34c78 // calls: 0x00401b20, 0x00401b20
int Lua_GetHeroLevel(lua_State* L); // 0x005c3190
// VA: 0x00568290 | RegTable: 0x00f32ef0 // calls: 0x00401b20, 0x00401b20
int Lua_GetHeroName(lua_State* L); // 0x00568290
// VA: 0x005c4140 | RegTable: 0x00f34e88 // calls: 0x00401b20, 0x00401b20
int Lua_GetHeroSkillMastery(lua_State* L); // 0x005c4140
// VA: 0x005c33a0 | RegTable: 0x00f34c80 // calls: 0x00401b20, 0x00401b20
int Lua_GetHeroStat(lua_State* L); // 0x005c33a0
// VA: 0x005d0a90 | RegTable: 0x00f34e70 // calls: 0x00401b20, 0x00401b20
int Lua_GetHeroTown(lua_State* L); // 0x005d0a90
// VA: 0x00567000 | RegTable: 0x00f32ea0 // calls: 0x00401b20, 0x00401b20
int Lua_GetHost(lua_State* L); // 0x00567000
// VA: 0x005e5c50 | RegTable: 0x00f35270 // calls: 0x00401b20, 0x00401b20
int Lua_GetLastSavedCombatIndex(lua_State* L); // 0x005e5c50
// VA: 0x006b5c70 | RegTable: 0x00f39a24 // calls: 0x00401b20, 0x00401b20
int Lua_GetMapDataPath(lua_State* L); // 0x006b5c70
// VA: 0x005e1570 | RegTable: 0x00f35000 // calls: 0x00401b20, 0x00401b20
int Lua_GetMaxFloor(lua_State* L); // 0x005e1570
// VA: 0x005d33f0 | RegTable: 0x00f34f28 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectArmySlotCreature(lua_State* L); // 0x005d33f0
// VA: 0x005cc590 | RegTable: 0x00f34dc0 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectCreatures(lua_State* L); // 0x005cc590
// VA: 0x005cc820 | RegTable: 0x00f34ed8
int Lua_GetObjectCreaturesTypes(lua_State* L); // 0x005cc820
// VA: 0x005cd530 | RegTable: 0x00f34dd8 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectDwellingCreatures(lua_State* L); // 0x005cd530
// VA: 0x005e0660 | RegTable: 0x00f35190
int Lua_GetObjectNamesByType(lua_State* L); // 0x005e0660
// VA: 0x005cb6d0 | RegTable: 0x00f34d98 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectOwner(lua_State* L); // 0x005cb6d0
// VA: 0x005caf40 | RegTable: 0x00f34d88 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectPosition(lua_State* L); // 0x005caf40
// VA: 0x005d8ea0 | RegTable: 0x00f35058 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectiveProgress(lua_State* L); // 0x005d8ea0
// VA: 0x005d8710 | RegTable: 0x00f35048 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectiveState(lua_State* L); // 0x005d8710
// VA: 0x005d0cf0 | RegTable: 0x00f34e78 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectsFromPath(lua_State* L); // 0x005d0cf0
// VA: 0x005cbf60 | RegTable: 0x00f34db0 // calls: 0x00401b20, 0x00401b20
int Lua_GetObjectsInRegion(lua_State* L); // 0x005cbf60
// VA: 0x005c1540 | RegTable: 0x00f34c30 // calls: 0x00401b20, 0x00401b20
int Lua_GetPlayerHeroes(lua_State* L); // 0x005c1540
// VA: 0x005d3900 | RegTable: 0x00f34f38 // calls: 0x00401b20, 0x00401b20
int Lua_GetPlayerNecroEnergy(lua_State* L); // 0x005d3900
// VA: 0x005d2eb0 | RegTable: 0x00f34f18 // calls: 0x00401b20, 0x00401b20
int Lua_GetPlayerRace(lua_State* L); // 0x005d2eb0
// VA: 0x005c19a0 | RegTable: 0x00f34c38 // calls: 0x00401b20, 0x00401b20
int Lua_GetPlayerResource(lua_State* L); // 0x005c19a0
// VA: 0x005d2830 | RegTable: 0x00f34f00 // calls: 0x00401b20, 0x00401b20
int Lua_GetPlayerSelectedCampaignBonusIndex(lua_State* L); // 0x005d2830
// VA: 0x005c1390 | RegTable: 0x00f34c28 // calls: 0x00401b20, 0x00401b20
int Lua_GetPlayerState(lua_State* L); // 0x005c1390
// VA: 0x005d3670 | RegTable: 0x00f34f30 // calls: 0x00401b20, 0x00401b20
int Lua_GetPlayerTeam(lua_State* L); // 0x005d3670
// VA: 0x0056e710 | RegTable: 0x00f33010 // calls: 0x00401b20, 0x00401b20
int Lua_GetRageLevel(lua_State* L); // 0x0056e710
// VA: 0x0056e4a0 | RegTable: 0x00f33008 // calls: 0x00401b20, 0x00401b20
int Lua_GetRagePoints(lua_State* L); // 0x0056e4a0
// VA: 0x005e6720 | RegTable: 0x00f35298 // calls: 0x00401b20, 0x00401b20
int Lua_GetSavedCombatArmyCreatureInfo(lua_State* L); // 0x005e6720
// VA: 0x005e64d0 | RegTable: 0x00f35290 // calls: 0x00401b20, 0x00401b20
int Lua_GetSavedCombatArmyCreaturesCount(lua_State* L); // 0x005e64d0
// VA: 0x005e6260 | RegTable: 0x00f35288 // calls: 0x00401b20, 0x00401b20
int Lua_GetSavedCombatArmyHero(lua_State* L); // 0x005e6260
// VA: 0x005e6030 | RegTable: 0x00f35280 // calls: 0x00401b20, 0x00401b20
int Lua_GetSavedCombatArmyPlayer(lua_State* L); // 0x005e6030
// VA: 0x005e5e40 | RegTable: 0x00f35278 // calls: 0x00401b20, 0x00401b20
int Lua_GetSavedCombatResult(lua_State* L); // 0x005e5e40
// VA: 0x006b5e60 | RegTable: 0x00f39a2c // calls: 0x00401b20, 0x00401b20
int Lua_GetSoundTimeInSleeps(lua_State* L); // 0x006b5e60
// VA: 0x005d0360 | RegTable: 0x00f34e58 // calls: 0x00401b20, 0x00401b20
int Lua_GetStandState(lua_State* L); // 0x005d0360
// VA: 0x005d0150 | RegTable: 0x00f34e50 // calls: 0x00401b20, 0x00401b20
int Lua_GetStandStatesCount(lua_State* L); // 0x005d0150
// VA: 0x005e0f90 | RegTable: 0x00f35178 // calls: 0x00401b20, 0x00401b20
int Lua_GetTerrainSize(lua_State* L); // 0x005e0f90
// VA: 0x005c7590 | RegTable: 0x00f34d28 // calls: 0x00401b20, 0x00401b20
int Lua_GetTownBuildingLevel(lua_State* L); // 0x005c7590
// VA: 0x005c7b10 | RegTable: 0x00f34d38 // calls: 0x00401b20, 0x00401b20
int Lua_GetTownBuildingLimitLevel(lua_State* L); // 0x005c7b10
// VA: 0x005c7850 | RegTable: 0x00f34d30 // calls: 0x00401b20, 0x00401b20
int Lua_GetTownBuildingMaxLevel(lua_State* L); // 0x005c7850
// VA: 0x005d0840 | RegTable: 0x00f34e68 // calls: 0x00401b20, 0x00401b20
int Lua_GetTownHero(lua_State* L); // 0x005d0840
// VA: 0x005e8c30 | RegTable: 0x00f35378 // calls: 0x00401b20, 0x00401b20
int Lua_GetTownRace(lua_State* L); // 0x005e8c30
// VA: 0x005e8300 | RegTable: 0x00f35358 // calls: 0x00401b20, 0x00401b20
int Lua_GetTurnTimeLeft(lua_State* L); // 0x005e8300
// VA: 0x00569570 | RegTable: 0x00f32f28 // calls: 0x00401b20, 0x00401b20
int Lua_GetUnitManaPoints(lua_State* L); // 0x00569570
// VA: 0x00569790 | RegTable: 0x00f32f30 // calls: 0x00401b20, 0x00401b20
int Lua_GetUnitMaxManaPoints(lua_State* L); // 0x00569790
// VA: 0x00568060 | RegTable: 0x00f32ee0 // calls: 0x00401b20, 0x00401b20
int Lua_GetUnitPosition(lua_State* L); // 0x00568060
// VA: 0x005677a0 | RegTable: 0x00f32ec8 // calls: 0x00401b20, 0x00401b20
int Lua_GetUnitSide(lua_State* L); // 0x005677a0
// VA: 0x005679f0 | RegTable: 0x00f32ed0 // calls: 0x00401b20, 0x00401b20
int Lua_GetUnitType(lua_State* L); // 0x005679f0
// VA: 0x00567c70 | RegTable: 0x00f32ed8 // calls: 0x00401b20, 0x00401b20
int Lua_GetUnits(lua_State* L); // 0x00567c70
// VA: 0x00568930 | RegTable: 0x00f32f08 // calls: 0x00401b20, 0x00401b20
int Lua_GetWarMachineType(lua_State* L); // 0x00568930
// VA: 0x005c5200 | RegTable: 0x00f34cc8 // calls: 0x00401b20, 0x00401b20
int Lua_GiveArtefact(lua_State* L); // 0x005c5200
// VA: 0x005c2540 | RegTable: 0x00f34c58 // calls: 0x00401b20, 0x00401b20
int Lua_GiveBorderguardKey(lua_State* L); // 0x005c2540
// VA: 0x005e89b0 | RegTable: 0x00f35370 // calls: 0x00401b20, 0x00401b20
int Lua_GiveHeroBattleBonus(lua_State* L); // 0x005e89b0
// VA: 0x005c43a0 | RegTable: 0x00f34cb0
int Lua_GiveHeroSkill(lua_State* L); // 0x005c43a0
// VA: 0x005c6a50 | RegTable: 0x00f34d08 // calls: 0x00401b20, 0x00401b20
int Lua_GiveHeroWarMachine(lua_State* L); // 0x005c6a50
// VA: 0x005c4cd0 | RegTable: 0x00f34cb8 // calls: 0x00401b20, 0x00401b20
int Lua_HasArtefact(lua_State* L); // 0x005c4cd0
// VA: 0x005c22a0 | RegTable: 0x00f34c50 // calls: 0x00401b20, 0x00401b20
int Lua_HasBorderguardKey(lua_State* L); // 0x005c22a0
// VA: 0x005c3ee0 | RegTable: 0x00f34c98 // calls: 0x00401b20, 0x00401b20
int Lua_HasHeroSkill(lua_State* L); // 0x005c3ee0
// VA: 0x005c67f0 | RegTable: 0x00f34d00 // calls: 0x00401b20, 0x00401b20
int Lua_HasHeroWarMachine(lua_State* L); // 0x005c67f0
// VA: 0x005d3140 | RegTable: 0x00f34f20 // calls: 0x00401b20, 0x00401b20
int Lua_IsAIPlayer(lua_State* L); // 0x005d3140
// VA: 0x004c19e0 | RegTable: 0x00f2df30 // calls: 0x00401ab0, 0x00401ab0
int Lua_IsEqual(lua_State* L); // 0x004c19e0
// VA: 0x005c2f80 | RegTable: 0x00f34c70 // calls: 0x00401b20, 0x00401b20
int Lua_IsHeroAlive(lua_State* L); // 0x005c2f80
// VA: 0x005e3580 | RegTable: 0x00f351f8 // calls: 0x00401b20, 0x00401b20
int Lua_IsHeroInBoat(lua_State* L); // 0x005e3580
// VA: 0x005c9c50 | RegTable: 0x00f34ec8 // calls: 0x00401b20, 0x00401b20
int Lua_IsHeroInTown(lua_State* L); // 0x005c9c50
// VA: 0x005c5a70 | RegTable: 0x00f34cf0 // calls: 0x00401b20, 0x00401b20
int Lua_IsHeroLootable(lua_State* L); // 0x005c5a70
// VA: 0x005cdbd0 | RegTable: 0x00f34de8 // calls: 0x00401b20, 0x00401b20
int Lua_IsObjectEnabled(lua_State* L); // 0x005cdbd0
// VA: 0x005c8520 | RegTable: 0x00f34d50 // calls: 0x00401b20, 0x00401b20
int Lua_IsObjectExists(lua_State* L); // 0x005c8520
// VA: 0x005cbcd0 | RegTable: 0x00f34da8 // calls: 0x00401b20, 0x00401b20
int Lua_IsObjectInRegion(lua_State* L); // 0x005cbcd0
// VA: 0x005cc2a0 | RegTable: 0x00f34db8 // calls: 0x00401b20, 0x00401b20
int Lua_IsObjectVisible(lua_State* L); // 0x005cc2a0
// VA: 0x005d8120 | RegTable: 0x00f35038 // calls: 0x00401b20, 0x00401b20
int Lua_IsObjectiveVisible(lua_State* L); // 0x005d8120
// VA: 0x005d75c0 | RegTable: 0x00f35328 // calls: 0x00401b20, 0x00401b20
int Lua_IsPlayerCurrent(lua_State* L); // 0x005d75c0
// VA: 0x005d7810 | RegTable: 0x00f35330 // calls: 0x00401b20, 0x00401b20
int Lua_IsPlayerInGhostMode(lua_State* L); // 0x005d7810
// VA: 0x005e7db0 | RegTable: 0x00f35348 // calls: 0x00401b20, 0x00401b20
int Lua_IsPlayerLost(lua_State* L); // 0x005e7db0
// VA: 0x005d7a60 | RegTable: 0x00f35338 // calls: 0x00401b20, 0x00401b20
int Lua_IsPlayerWaitingForTurn(lua_State* L); // 0x005d7a60
// VA: 0x005d97a0 | RegTable: 0x00f35070 // calls: 0x00401b20, 0x00401b20
int Lua_IsRegionBlocked(lua_State* L); // 0x005d97a0
// VA: 0x005d7cb0 | RegTable: 0x00f35340 // calls: 0x00401b20, 0x00401b20
int Lua_IsTeamCurrent(lua_State* L); // 0x005d7cb0
// VA: 0x005e11a0 | RegTable: 0x00f35180
int Lua_IsTilePassable(lua_State* L); // 0x005e11a0
// VA: 0x007742f0 | RegTable: 0x00f3e02c // calls: 0x00401b20, 0x00401b20
int Lua_IsTutorialEnabled(lua_State* L); // 0x007742f0
// VA: 0x00773c00 | RegTable: 0x00f3e004 // calls: 0x00401b20, 0x00401b20
int Lua_IsTutorialItemEnabled(lua_State* L); // 0x00773c00
// VA: 0x00774160 | RegTable: 0x00f3e024 // calls: 0x00401b20, 0x00401b20
int Lua_IsTutorialMessageBoxOpen(lua_State* L); // 0x00774160
// VA: 0x00b2d470 | RegTable: 0x00f2df28 // calls: 0x004bf580
int Lua_IsValid(lua_State* L); // 0x00b2d470
// VA: 0x005c4770 | RegTable: 0x00f34ca0 // calls: 0x00401b20, 0x00401b20
int Lua_KnowHeroSpell(lua_State* L); // 0x005c4770
// VA: 0x005c3c00 | RegTable: 0x00f34c90 // calls: 0x00401b20, 0x00401b20
int Lua_LevelUpHero(lua_State* L); // 0x005c3c00
// VA: 0x006b5aa0 | RegTable: 0x00f39a1c // calls: 0x00401b20, 0x00401b20
int Lua_Load(lua_State* L); // 0x006b5aa0
// VA: 0x005e7010 | RegTable: 0x00f352b0 // calls: 0x00401b20, 0x00401b20
int Lua_LockMinHeroSkillsAndAttributes(lua_State* L); // 0x005e7010
// VA: 0x005d6ed0 | RegTable: 0x00f35018 // calls: 0x00401b20, 0x00401b20
int Lua_Loose(lua_State* L); // 0x005d6ed0
// VA: 0x004c2100 | RegTable: 0x00f2df48 // calls: 0x00401ab0, 0x00401ab0
int Lua_LuaTest(lua_State* L); // 0x004c2100
// VA: 0x005d2220 | RegTable: 0x00f34ef0 // calls: 0x00401b20, 0x00401b20
int Lua_MakeHeroInteractWithObject(lua_State* L); // 0x005d2220
// VA: 0x005e4d50 | RegTable: 0x00f35250 // calls: 0x00401b20, 0x00401b20
int Lua_MakeHeroNecromancer(lua_State* L); // 0x005e4d50
// VA: 0x005e4fc0 | RegTable: 0x00f35240 // calls: 0x00401b20, 0x00401b20
int Lua_MakeHeroReturnToTavernAfterDeath(lua_State* L); // 0x005e4fc0
// VA: 0x005e5770 | RegTable: 0x00f35260 // calls: 0x00401b20
int Lua_MakeTownMovable(lua_State* L); // 0x005e5770
// VA: 0x005ce0f0 | RegTable: 0x00f34df8 // calls: 0x00401b20
int Lua_MarkObjectAsVisited(lua_State* L); // 0x005ce0f0
// VA: 0x0056e980 | RegTable: 0x00f33018 // calls: 0x00401b20, 0x00401b20
int Lua_MessageBox(lua_State* L); // 0x0056e980
// VA: 0x005db320 | RegTable: 0x00f350b0 // calls: 0x00401b20, 0x00401b20
int Lua_MessageBox(lua_State* L); // 0x005db320
// VA: 0x005db910 | RegTable: 0x00f352e0 // calls: 0x00401b20, 0x00401b20
int Lua_MessageBoxForPlayers(lua_State* L); // 0x005db910
// VA: 0x005db320 | RegTable: 0x00f350a0 // calls: 0x00401b20, 0x00401b20
int Lua_MessageBoxInt(lua_State* L); // 0x005db320
// VA: 0x005df150 | RegTable: 0x00f35130 // calls: 0x00401b20, 0x00401b20
int Lua_MoveCamera(lua_State* L); // 0x005df150
// VA: 0x005df430 | RegTable: 0x00f35310 // calls: 0x00401b20, 0x00401b20
int Lua_MoveCameraForPlayers(lua_State* L); // 0x005df430
// VA: 0x005cef50 | RegTable: 0x00f34e18 // calls: 0x00401b20
int Lua_MoveHero(lua_State* L); // 0x005cef50
// VA: 0x005cf310 | RegTable: 0x00f34e20 // calls: 0x00401b20
int Lua_MoveHeroRealTime(lua_State* L); // 0x005cf310
// VA: 0x00b2d320 | RegTable: 0x00f2df20 // calls: 0x004bf270, 0x004bf6e0, 0x004bf6e0
int Lua_ObjPtr(lua_State* L); // 0x00b2d320
// VA: 0x005da1e0 | RegTable: 0x00f35088 // calls: 0x00401b20
int Lua_OpenCircleFog(lua_State* L); // 0x005da1e0
// VA: 0x005e29f0 | RegTable: 0x00f351d0 // calls: 0x00401b20, 0x00401b20
int Lua_OpenPuzzleMap(lua_State* L); // 0x005e29f0
// VA: 0x005d9df0 | RegTable: 0x00f35080 // calls: 0x00401b20
int Lua_OpenRegionFog(lua_State* L); // 0x005d9df0
// VA: 0x005e3090 | RegTable: 0x00f351e8 // calls: 0x00401b20, 0x00401b20
int Lua_OverrideAdvMapDialogPos(lua_State* L); // 0x005e3090
// VA: 0x005e74e0 | RegTable: 0x00f352c0 // calls: 0x00401b20, 0x00401b20
int Lua_OverrideObjectTooltipNameAndDescription(lua_State* L); // 0x005e74e0
// VA: 0x0056eff0 | RegTable: 0x00f33028 // calls: 0x00401b20, 0x00401b20
int Lua_Play2DSound(lua_State* L); // 0x0056eff0
// VA: 0x005dc970 | RegTable: 0x00f350c8 // calls: 0x00401b20, 0x00401b20
int Lua_Play2DSound(lua_State* L); // 0x005dc970
// VA: 0x005dcf40 | RegTable: 0x00f352f0 // calls: 0x00401b20, 0x00401b20
int Lua_Play2DSoundForPlayers(lua_State* L); // 0x005dcf40
// VA: 0x0056f1e0 | RegTable: 0x00f33030 // calls: 0x00401b20, 0x00401b20
int Lua_Play3DSound(lua_State* L); // 0x0056f1e0
// VA: 0x005dcb90 | RegTable: 0x00f350d0 // calls: 0x00401b20, 0x00401b20
int Lua_Play3DSound(lua_State* L); // 0x005dcb90
// VA: 0x005dd170 | RegTable: 0x00f352f8 // calls: 0x00401b20, 0x00401b20
int Lua_Play3DSoundForPlayers(lua_State* L); // 0x005dd170
// VA: 0x005e1d90 | RegTable: 0x00f351a8 // calls: 0x00401b20
int Lua_PlayObjectAnimation(lua_State* L); // 0x005e1d90
// VA: 0x005e1710 | RegTable: 0x00f35198 // calls: 0x00401b20
int Lua_PlayVisualEffect(lua_State* L); // 0x005e1710
// VA: 0x00b2d200 | RegTable: 0x00f2df18 // calls: 0x004bf270, 0x004bf6e0, 0x004bf6e0
int Lua_Ptr(lua_State* L); // 0x00b2d200
// VA: 0x005db5f0 | RegTable: 0x00f350b8 // calls: 0x00401b20, 0x00401b20
int Lua_QuestionBox(lua_State* L); // 0x005db5f0
// VA: 0x005dbc30 | RegTable: 0x00f352e8 // calls: 0x00401b20, 0x00401b20
int Lua_QuestionBoxForPlayers(lua_State* L); // 0x005dbc30
// VA: 0x005db5f0 | RegTable: 0x00f350a8 // calls: 0x00401b20, 0x00401b20
int Lua_QuestionBoxInt(lua_State* L); // 0x005db5f0
// VA: 0x005ca830 | RegTable: 0x00f34d78 // calls: 0x00401b20
int Lua_RazeBuilding(lua_State* L); // 0x005ca830
// VA: 0x005ca3f0 | RegTable: 0x00f34d70 // calls: 0x00401b20
int Lua_RazeTown(lua_State* L); // 0x005ca3f0
// VA: 0x005d9540 | RegTable: 0x00f35068 // calls: 0x00401b20, 0x00401b20
int Lua_RegionToPoint(lua_State* L); // 0x005d9540
// VA: 0x005cfba0 | RegTable: 0x00f34e40 // calls: 0x00401b20, 0x00401b20
int Lua_RemoveAllMonsters(lua_State* L); // 0x005cfba0
// VA: 0x0056b400 | RegTable: 0x00f32f98 // calls: 0x00401b20, 0x00401b20
int Lua_RemoveAllUnits(lua_State* L); // 0x0056b400
// VA: 0x005c54d0 | RegTable: 0x00f34cd0 // calls: 0x00401b20, 0x00401b20
int Lua_RemoveArtefact(lua_State* L); // 0x005c54d0
// VA: 0x005c64a0 | RegTable: 0x00f34ce8
int Lua_RemoveHeroCreatures(lua_State* L); // 0x005c64a0
// VA: 0x005c6d10 | RegTable: 0x00f34d10 // calls: 0x00401b20, 0x00401b20
int Lua_RemoveHeroWarMachine(lua_State* L); // 0x005c6d10
// VA: 0x005cacb0 | RegTable: 0x00f34d80 // calls: 0x00401b20, 0x00401b20
int Lua_RemoveObject(lua_State* L); // 0x005cacb0
// VA: 0x005cd7b0 | RegTable: 0x00f34de0
int Lua_RemoveObjectCreatures(lua_State* L); // 0x005cd7b0
// VA: 0x005d1c50 | RegTable: 0x00f34eb0 // calls: 0x00401b20, 0x00401b20
int Lua_ReplaceDwelling(lua_State* L); // 0x005d1c50
// VA: 0x005c72e0 | RegTable: 0x00f34d20 // calls: 0x00401b20, 0x00401b20
int Lua_ResetHeroCombatScript(lua_State* L); // 0x005c72e0
// VA: 0x006b58d0 | RegTable: 0x00f39a14 // calls: 0x00401b20, 0x00401b20
int Lua_Save(lua_State* L); // 0x006b58d0
// VA: 0x005dfe10 | RegTable: 0x00f35158 // calls: 0x00401b20, 0x00401b20
int Lua_SetAIHeroAttractor(lua_State* L); // 0x005dfe10
// VA: 0x005e2510 | RegTable: 0x00f351c8 // calls: 0x00401b20, 0x00401b20
int Lua_SetAIHeroFleeControl(lua_State* L); // 0x005e2510
// VA: 0x005e00e0 | RegTable: 0x00f35160 // calls: 0x00401b20, 0x00401b20
int Lua_SetAIPlayerAttractor(lua_State* L); // 0x005e00e0
// VA: 0x005dab00 | RegTable: 0x00f35090 // calls: 0x00401b20, 0x00401b20
int Lua_SetAmbientLight(lua_State* L); // 0x005dab00
// VA: 0x005dad90 | RegTable: 0x00f352d8 // calls: 0x00401b20, 0x00401b20
int Lua_SetAmbientLightForPlayers(lua_State* L); // 0x005dad90
// VA: 0x005e03d0 | RegTable: 0x00f35170 // calls: 0x00401b20, 0x00401b20
int Lua_SetCombatAmbientLight(lua_State* L); // 0x005e03d0
// VA: 0x00566d70 | RegTable: 0x00f32e98 // calls: 0x00401b20, 0x00401b20
int Lua_SetControlMode(lua_State* L); // 0x00566d70
// VA: 0x005e54b0 | RegTable: 0x00f35258 // calls: 0x00401b20, 0x00401b20
int Lua_SetDisabledObjectMode(lua_State* L); // 0x005e54b0
// VA: 0x0056a660 | RegTable: 0x00f32f68 // calls: 0x00401b20, 0x00401b20
int Lua_SetGameVar(lua_State* L); // 0x0056a660
// VA: 0x005decb0 | RegTable: 0x00f35118 // calls: 0x00401b20, 0x00401b20
int Lua_SetGameVar(lua_State* L); // 0x005decb0
// VA: 0x00664160 | RegTable: 0x00f3814c // calls: 0x00401b20, 0x00401b20
int Lua_SetGameVar(lua_State* L); // 0x00664160
// VA: 0x004c1cc0 | RegTable: 0x00f2df38 // calls: 0x00401ab0, 0x00401ab0
int Lua_SetGlobalVar(lua_State* L); // 0x004c1cc0
// VA: 0x005d2a50 | RegTable: 0x00f34f08 // calls: 0x00401b20, 0x00401b20
int Lua_SetHeroBiography(lua_State* L); // 0x005d2a50
// VA: 0x005c6fd0 | RegTable: 0x00f34d18 // calls: 0x00401b20, 0x00401b20
int Lua_SetHeroCombatScript(lua_State* L); // 0x005c6fd0
// VA: 0x005c5c80 | RegTable: 0x00f34cf8 // calls: 0x00401b20, 0x00401b20
int Lua_SetHeroLootable(lua_State* L); // 0x005c5c80
// VA: 0x005e7260 | RegTable: 0x00f352b8 // calls: 0x00401b20, 0x00401b20
int Lua_SetHeroRoleMode(lua_State* L); // 0x005e7260
// VA: 0x005e2c70 | RegTable: 0x00f351d8 // calls: 0x00401b20, 0x00401b20
int Lua_SetHeroesExpCoef(lua_State* L); // 0x005e2c70
// VA: 0x005c9240 | RegTable: 0x00f34ed0 // calls: 0x00401b20, 0x00401b20
int Lua_SetMonsterCourageAndMood(lua_State* L); // 0x005c9240
// VA: 0x005c8fd0 | RegTable: 0x00f34ec0 // calls: 0x00401b20, 0x00401b20
int Lua_SetMonsterNames(lua_State* L); // 0x005c8fd0
// VA: 0x005ce490 | RegTable: 0x00f34e00 // calls: 0x00401b20, 0x00401b20
int Lua_SetMonsterSelectionType(lua_State* L); // 0x005ce490
// VA: 0x005cd270 | RegTable: 0x00f34dd0 // calls: 0x00401b20, 0x00401b20
int Lua_SetObjectDwellingCreatures(lua_State* L); // 0x005cd270
// VA: 0x005cde30 | RegTable: 0x00f34df0 // calls: 0x00401b20
int Lua_SetObjectEnabled(lua_State* L); // 0x005cde30
// VA: 0x005db020 | RegTable: 0x00f35098 // calls: 0x00401b20
int Lua_SetObjectFlashlight(lua_State* L); // 0x005db020
// VA: 0x005cb910 | RegTable: 0x00f34da0
int Lua_SetObjectOwner(lua_State* L); // 0x005cb910
// VA: 0x005cb1b0 | RegTable: 0x00f34d90 // calls: 0x00401b20, 0x00401b20
int Lua_SetObjectPosition(lua_State* L); // 0x005cb1b0
// VA: 0x005cb440 | RegTable: 0x00f34e90 // calls: 0x00401b20, 0x00401b20
int Lua_SetObjectRotation(lua_State* L); // 0x005cb440
// VA: 0x005d9170 | RegTable: 0x00f35060 // calls: 0x00401b20
int Lua_SetObjectiveProgress(lua_State* L); // 0x005d9170
// VA: 0x005d89e0 | RegTable: 0x00f35050 // calls: 0x00401b20
int Lua_SetObjectiveState(lua_State* L); // 0x005d89e0
// VA: 0x005d83f0 | RegTable: 0x00f35040 // calls: 0x00401b20, 0x00401b20
int Lua_SetObjectiveVisible(lua_State* L); // 0x005d83f0
// VA: 0x005d11c0 | RegTable: 0x00f34e80 // calls: 0x00401b20, 0x00401b20
int Lua_SetPlayerHeroesCountNotForHire(lua_State* L); // 0x005d11c0
// VA: 0x005c1c30 | RegTable: 0x00f34c40 // calls: 0x00401b20, 0x00401b20
int Lua_SetPlayerResource(lua_State* L); // 0x005c1c30
// VA: 0x005c1fb0 | RegTable: 0x00f34c48 // calls: 0x00401b20, 0x00401b20
int Lua_SetPlayerStartResources(lua_State* L); // 0x005c1fb0
// VA: 0x005e2030 | RegTable: 0x00f351b0 // calls: 0x00401b20, 0x00401b20
int Lua_SetPlayerTeam(lua_State* L); // 0x005e2030
// VA: 0x005e6ca0 | RegTable: 0x00f352a8 // calls: 0x00401b20
int Lua_SetRegionAutoObjectEnable(lua_State* L); // 0x005e6ca0
// VA: 0x005d9a50 | RegTable: 0x00f35078 // calls: 0x00401b20, 0x00401b20
int Lua_SetRegionBlocked(lua_State* L); // 0x005d9a50
// VA: 0x005d0570 | RegTable: 0x00f34e60 // calls: 0x00401b20, 0x00401b20
int Lua_SetStandState(lua_State* L); // 0x005d0570
// VA: 0x005c7dd0 | RegTable: 0x00f34d40 // calls: 0x00401b20
int Lua_SetTownBuildingLimitLevel(lua_State* L); // 0x005c7dd0
// VA: 0x005dd530 | RegTable: 0x00f350d8 // calls: 0x00401b20
int Lua_SetTrigger(lua_State* L); // 0x005dd530
// VA: 0x005699b0 | RegTable: 0x00f32f38 // calls: 0x00401b20, 0x00401b20
int Lua_SetUnitManaPoints(lua_State* L); // 0x005699b0
// VA: 0x005df9d0 | RegTable: 0x00f35148 // calls: 0x00401b20, 0x00401b20
int Lua_SetWarfogBehaviour(lua_State* L); // 0x005df9d0
// VA: 0x0056ebe0 | RegTable: 0x00f33020 // calls: 0x00401b20, 0x00401b20
int Lua_ShowFlyingSign(lua_State* L); // 0x0056ebe0
// VA: 0x005dc450 | RegTable: 0x00f350c0 // calls: 0x00401b20, 0x00401b20
int Lua_ShowFlyingSign(lua_State* L); // 0x005dc450
// VA: 0x005d4460 | RegTable: 0x00f34e30 // calls: 0x00401b20, 0x00401b20
int Lua_SiegeTown(lua_State* L); // 0x005d4460
// VA: 0x005e37f0 | RegTable: 0x00f35200 // calls: 0x00401b20, 0x00401b20
int Lua_SinkHero(lua_State* L); // 0x005e37f0
// VA: 0x004bfe80 | RegTable: 0x00f2df00
int Lua_Sleep(lua_State* L); // 0x004bfe80
// VA: 0x005e2e80 | RegTable: 0x00f351e0 // calls: 0x00401b20, 0x00401b20
int Lua_StartAdvMapDialog(lua_State* L); // 0x005e2e80
// VA: 0x005d3b90 | RegTable: 0x00f34e28 // calls: 0x00401b20
int Lua_StartCombat(lua_State* L); // 0x005d3b90
// VA: 0x005de340 | RegTable: 0x00f35108 // calls: 0x00401b20, 0x00401b20
int Lua_StartCutScene(lua_State* L); // 0x005de340
// VA: 0x005de820 | RegTable: 0x00f35308 // calls: 0x00401b20, 0x00401b20
int Lua_StartCutSceneForPlayers(lua_State* L); // 0x005de820
// VA: 0x005de340 | RegTable: 0x00f350f8 // calls: 0x00401b20, 0x00401b20
int Lua_StartCutSceneInt(lua_State* L); // 0x005de340
// VA: 0x005de0d0 | RegTable: 0x00f35100 // calls: 0x00401b20, 0x00401b20
int Lua_StartDialogScene(lua_State* L); // 0x005de0d0
// VA: 0x005de5a0 | RegTable: 0x00f35300 // calls: 0x00401b20, 0x00401b20
int Lua_StartDialogSceneForPlayers(lua_State* L); // 0x005de5a0
// VA: 0x005de0d0 | RegTable: 0x00f350f0 // calls: 0x00401b20, 0x00401b20
int Lua_StartDialogSceneInt(lua_State* L); // 0x005de0d0
// VA: 0x004bfe20 | RegTable: 0x00f2df08 // calls: 0x00b9c2e0
int Lua_StartThread(lua_State* L); // 0x004bfe20
// VA: 0x005ddfa0 | RegTable: 0x00f350e8 // calls: 0x00401b20, 0x00401b20
int Lua_StopTrigger(lua_State* L); // 0x005ddfa0
// VA: 0x005e1bb0 | RegTable: 0x00f351a0 // calls: 0x00401b20, 0x00401b20
int Lua_StopVisualEffects(lua_State* L); // 0x005e1bb0
// VA: 0x00569180 | RegTable: 0x00f32f20 // calls: 0x00401b20, 0x00401b20
int Lua_SummonCreature(lua_State* L); // 0x00569180
// VA: 0x005e5a00 | RegTable: 0x00f35268 // calls: 0x00401b20, 0x00401b20
int Lua_TakeAwayHeroExp(lua_State* L); // 0x005e5a00
// VA: 0x005dbfa0 | RegTable: 0x00f35380 // calls: 0x00401b20, 0x00401b20
int Lua_TalkBoxForPlayers(lua_State* L); // 0x005dbfa0
// VA: 0x005c49e0 | RegTable: 0x00f34ca8 // calls: 0x00401b20, 0x00401b20
int Lua_TeachHeroSpell(lua_State* L); // 0x005c49e0
// VA: 0x005c9ee0 | RegTable: 0x00f34d68 // calls: 0x00401b20
int Lua_TransformTown(lua_State* L); // 0x005c9ee0
// VA: 0x005dd530 | RegTable: 0x00f350e0 // calls: 0x00401b20
int Lua_Trigger(lua_State* L); // 0x005dd530
// VA: 0x00773da0 | RegTable: 0x00f3e00c // calls: 0x00401b20, 0x00401b20
int Lua_TutorialActivateHint(lua_State* L); // 0x00773da0
// VA: 0x00774020 | RegTable: 0x00f3e01c // calls: 0x00401b20, 0x00401b20
int Lua_TutorialMessageBox(lua_State* L); // 0x00774020
// VA: 0x00773ee0 | RegTable: 0x00f3e014 // calls: 0x00401b20, 0x00401b20
int Lua_TutorialSetBlink(lua_State* L); // 0x00773ee0
// VA: 0x005df000 | RegTable: 0x00f35128 // calls: 0x00401b20, 0x00401b20
int Lua_UnblockGame(lua_State* L); // 0x005df000
// VA: 0x0056a080 | RegTable: 0x00f32f50 // calls: 0x00401b20, 0x00401b20
int Lua_UnitCastAimedSpell(lua_State* L); // 0x0056a080
// VA: 0x00569e60 | RegTable: 0x00f32f48 // calls: 0x00401b20, 0x00401b20
int Lua_UnitCastAreaSpell(lua_State* L); // 0x00569e60
// VA: 0x00569c30 | RegTable: 0x00f32f40 // calls: 0x00401b20, 0x00401b20
int Lua_UnitCastGlobalSpell(lua_State* L); // 0x00569c30
// VA: 0x005c2c90 | RegTable: 0x00f34c68 // calls: 0x00401b20
int Lua_UnreserveHero(lua_State* L); // 0x005c2c90
// VA: 0x005e7a30 | RegTable: 0x00f352d0 // calls: 0x00401b20, 0x00401b20
int Lua_UpgradeTownBuilding(lua_State* L); // 0x005e7a30
// VA: 0x005e8720 | RegTable: 0x00f35368 // calls: 0x00401b20, 0x00401b20
int Lua_WarpHeroExp(lua_State* L); // 0x005e8720
// VA: 0x005e85c0 | RegTable: 0x00f35360 // calls: 0x00401b20, 0x00401b20
int Lua_WarpToMap(lua_State* L); // 0x005e85c0
// VA: 0x005d69d0 | RegTable: 0x00f35010 // calls: 0x00401b20, 0x00401b20
int Lua_Win(lua_State* L); // 0x005d69d0
// VA: 0x005d6c70 | RegTable: 0x00f35320 // calls: 0x00401b20
int Lua_WinTeam(lua_State* L); // 0x005d6c70
// VA: 0x0056a9f0 | RegTable: 0x00f32f78 // calls: 0x00401b20, 0x00401b20
int Lua_addUnit(lua_State* L); // 0x0056a9f0
// VA: 0x00cac330 | RegTable: 0x00f7bd18 // calls: 0x00401b20, 0x00401b20
int Lua_ceil(lua_State* L); // 0x00cac330
// VA: 0x0056dbc0 | RegTable: 0x00f33058 // calls: 0x00401b20, 0x00401b20
int Lua_changeSubject(lua_State* L); // 0x0056dbc0
// VA: 0x0056da90 | RegTable: 0x00f33050 // calls: 0x00401b20, 0x00401b20
int Lua_clearMessage(lua_State* L); // 0x0056da90
// VA: 0x00567210 | RegTable: 0x00f32eb0 // calls: 0x00401b20, 0x00401b20
int Lua_combatEnableFinish(lua_State* L); // 0x00567210
// VA: 0x0056d5c0 | RegTable: 0x00f32ff8 // calls: 0x00401b20, 0x00401b20
int Lua_combatPlayEmotion(lua_State* L); // 0x0056d5c0
// VA: 0x0056cd60 | RegTable: 0x00f32fe0 // calls: 0x00401b20, 0x00401b20
int Lua_combatReadyPerson(lua_State* L); // 0x0056cd60
// VA: 0x0056d3c0 | RegTable: 0x00f32ff0 // calls: 0x00401b20, 0x00401b20
int Lua_combatSetPause(lua_State* L); // 0x0056d3c0
// VA: 0x0056cbd0 | RegTable: 0x00f32fd8 // calls: 0x00401b20, 0x00401b20
int Lua_combatStarted(lua_State* L); // 0x0056cbd0
// VA: 0x0056b950 | RegTable: 0x00f32fa8 // calls: 0x00401b20, 0x00401b20
int Lua_commandDefend(lua_State* L); // 0x0056b950
// VA: 0x0056c4b0 | RegTable: 0x00f32fc8 // calls: 0x00401b20, 0x00401b20
int Lua_commandDoSpecial(lua_State* L); // 0x0056c4b0
// VA: 0x0056c830 | RegTable: 0x00f32fd0 // calls: 0x00401b20, 0x00401b20
int Lua_commandDoSpell(lua_State* L); // 0x0056c830
// VA: 0x0056bb30 | RegTable: 0x00f32fb0 // calls: 0x00401b20, 0x00401b20
int Lua_commandMove(lua_State* L); // 0x0056bb30
// VA: 0x0056bec0 | RegTable: 0x00f32fb8 // calls: 0x00401b20, 0x00401b20
int Lua_commandMoveAttack(lua_State* L); // 0x0056bec0
// VA: 0x0056c200 | RegTable: 0x00f32fc0 // calls: 0x00401b20, 0x00401b20
int Lua_commandShot(lua_State* L); // 0x0056c200
// VA: 0x00cabca0 | RegTable: 0x00f7bcf0 // calls: 0x00401b20, 0x00401b20
int Lua_consoleCmd(lua_State* L); // 0x00cabca0
// VA: 0x0056b680 | RegTable: 0x00f32fa0 // calls: 0x00401b20, 0x00401b20
int Lua_displace(lua_State* L); // 0x0056b680
// VA: 0x00cabda0 | RegTable: 0x00f7bcf8 // calls: 0x00401b20, 0x00401b20
int Lua_doFile(lua_State* L); // 0x00cabda0
// VA: 0x004bff20 | RegTable: 0x00f7bcc8 // calls: 0x00d0c4d0
int Lua_errorHook(lua_State* L); // 0x004bff20
// VA: 0x0056ae00 | RegTable: 0x00f32f80 // calls: 0x00401b20, 0x00401b20
int Lua_exist(lua_State* L); // 0x0056ae00
// VA: 0x00cac200 | RegTable: 0x00f7bd10 // calls: 0x00401b20, 0x00401b20
int Lua_floor(lua_State* L); // 0x00cac200
// VA: 0x00cac5b0 | RegTable: 0x00f7bd28 // calls: 0x00401b20, 0x00401b20
int Lua_frac(lua_State* L); // 0x00cac5b0
// VA: 0x00cac460 | RegTable: 0x00f7bd20 // calls: 0x00401b20, 0x00401b20
int Lua_intg(lua_State* L); // 0x00cac460
// VA: 0x004c19e0 | RegTable: 0x00f7bcd0 // calls: 0x00401ab0, 0x00401ab0
int Lua_isEqual(lua_State* L); // 0x004c19e0
// VA: 0x00cabf00 | RegTable: 0x00f7bd00 // calls: 0x00401b20, 0x00401b20
int Lua_mod(lua_State* L); // 0x00cabf00
// VA: 0x004c1200 | RegTable: 0x00f2def8 // calls: 0x004bf270, 0x004c13fe
int Lua_out(lua_State* L); // 0x004c1200
// VA: 0x00cabb60 | RegTable: 0x00f7bce0 // calls: 0x00401b20, 0x00401b20
int Lua_parse(lua_State* L); // 0x00cabb60
// VA: 0x0056d170 | RegTable: 0x00f33000 // calls: 0x00401b20
int Lua_playAnimation(lua_State* L); // 0x0056d170
// VA: 0x00568060 | RegTable: 0x00f32ee8 // calls: 0x00401b20, 0x00401b20
int Lua_pos(lua_State* L); // 0x00568060
// VA: 0x0056a2f0 | RegTable: 0x00f32f58 // calls: 0x00401b20, 0x00401b20
int Lua_postEvent(lua_State* L); // 0x0056a2f0
// VA: 0x00cac850 | RegTable: 0x00f7bce8 // calls: 0x00401b20, 0x00401b20
int Lua_print(lua_State* L); // 0x00cac850
// VA: 0x004c0da0 | RegTable: 0x00f2df10 // calls: 0x004bf5c0, 0x004bf900
int Lua_random(lua_State* L); // 0x004c0da0
// VA: 0x005dfc30 | RegTable: 0x00f35140 // calls: 0x00401b20, 0x00401b20
int Lua_random(lua_State* L); // 0x005dfc30
// VA: 0x0056b1d0 | RegTable: 0x00f32f90 // calls: 0x00401b20, 0x00401b20
int Lua_removeUnit(lua_State* L); // 0x0056b1d0
// VA: 0x00cac710 | RegTable: 0x00f7bd30 // calls: 0x00401b20, 0x00401b20
int Lua_round(lua_State* L); // 0x00cac710
// VA: 0x0056cf60 | RegTable: 0x00f32fe8 // calls: 0x00401b20, 0x00401b20
int Lua_setATB(lua_State* L); // 0x0056cf60
// VA: 0x0056df10 | RegTable: 0x00f33068 // calls: 0x00401b20, 0x00401b20
int Lua_shots(lua_State* L); // 0x0056df10
// VA: 0x0056e180 | RegTable: 0x00f33070 // calls: 0x00401b20, 0x00401b20
int Lua_shotsNumber(lua_State* L); // 0x0056e180
// VA: 0x0056a890 | RegTable: 0x00f32f70 // calls: 0x00401b20, 0x00401b20
int Lua_showHighlighting(lua_State* L); // 0x0056a890
// VA: 0x0056d900 | RegTable: 0x00f33048 // calls: 0x00401b20, 0x00401b20
int Lua_showMessage(lua_State* L); // 0x0056d900
// VA: 0x004bfe80 | RegTable: 0x00f7bcb8
int Lua_sleep(lua_State* L); // 0x004bfe80
// VA: 0x00cac090 | RegTable: 0x00f7bd08 // calls: 0x00401b20, 0x00401b20
int Lua_sqrt(lua_State* L); // 0x00cac090
// VA: 0x004bfe20 | RegTable: 0x00f7bcc0 // calls: 0x00b9c2e0
int Lua_startThread(lua_State* L); // 0x004bfe20
// VA: 0x0056ddb0 | RegTable: 0x00f33060 // calls: 0x00401b20, 0x00401b20
int Lua_subject(lua_State* L); // 0x0056ddb0
// VA: 0x0056d7d0 | RegTable: 0x00f33040 // calls: 0x00401b20, 0x00401b20
int Lua_toggleTutorialMode(lua_State* L); // 0x0056d7d0
// VA: 0x0056afa0 | RegTable: 0x00f32f88 // calls: 0x00401b20, 0x00401b20
int Lua_unitNames(lua_State* L); // 0x0056afa0

extern const SLuaRegistrationEntry g_HoMM5LuaCApiTable[] = {
    { "AddCreature", &Lua_AddCreature, 0x00f32f18 }, // handler: 0x00568d90
    { "AddHeroCreatures", &Lua_AddHeroCreatures, 0x00f34ce0 }, // handler: 0x005c6140
    { "AddObjectCreatures", &Lua_AddObjectCreatures, 0x00f34dc8 }, // handler: 0x005ccec0
    { "AllowHeroHiringByRaceForAI", &Lua_AllowHeroHiringByRaceForAI, 0x00f35210 }, // handler: 0x005e3d40
    { "AllowHeroHiringByRaceInTown", &Lua_AllowHeroHiringByRaceInTown, 0x00f35220 }, // handler: 0x005e4270
    { "AllowHiringOfHeroForAI", &Lua_AllowHiringOfHeroForAI, 0x00f35218 }, // handler: 0x005e4000
    { "AllowHiringOfHeroInTown", &Lua_AllowHiringOfHeroInTown, 0x00f35228 }, // handler: 0x005e4530
    { "AllowOpenFogOfWarForAlly", &Lua_AllowOpenFogOfWarForAlly, 0x00f35248 }, // handler: 0x005e51f0
    { "AllowPlayerTavernHero", &Lua_AllowPlayerTavernHero, 0x00f35238 }, // handler: 0x005e4ac0
    { "AllowPlayerTavernRace", &Lua_AllowPlayerTavernRace, 0x00f35230 }, // handler: 0x005e47f0
    { "BlockGame", &Lua_BlockGame, 0x00f35120 }, // handler: 0x005deeb0
    { "BlockTownGarrisonForAI", &Lua_BlockTownGarrisonForAI, 0x00f35208 }, // handler: 0x005e3aa0
    { "Break", &Lua_Break, 0x00f32ec0 }, // handler: 0x00567660
    { "CalcAverageMonstersTier", &Lua_CalcAverageMonstersTier, 0x00f34e48 }, // handler: 0x005cfdc0
    { "CalcHeroMoveCost", &Lua_CalcHeroMoveCost, 0x00f34e10 }, // handler: 0x005ceaf0
    { "Callback", &Lua_Callback, 0x00f7bd40 }, // handler: 0x00cad310
    { "CanMoveHero", &Lua_CanMoveHero, 0x00f34e08 }, // handler: 0x005ce790
    { "CanShowToPlayer", &Lua_CanShowToPlayer, 0x00f35350 }, // handler: 0x005e8060
    { "ChangeHeroStat", &Lua_ChangeHeroStat, 0x00f34c88 }, // handler: 0x005c3620
    { "ControlHeroCustomAbility", &Lua_ControlHeroCustomAbility, 0x00f34ef8 }, // handler: 0x005d2560
    { "CreateArtifact", &Lua_CreateArtifact, 0x00f34d58 }, // handler: 0x005c8730
    { "CreateCaravan", &Lua_CreateCaravan, 0x00f351f0 }, // handler: 0x005e32c0
    { "CreateDwelling", &Lua_CreateDwelling, 0x00f34ea8 }, // handler: 0x005c9530
    { "CreateMonster", &Lua_CreateMonster, 0x00f34d60 }, // handler: 0x005c8aa0
    { "CreateStatic", &Lua_CreateStatic, 0x00f34ea0 }, // handler: 0x005d1810
    { "CreateTreasure", &Lua_CreateTreasure, 0x00f34e98 }, // handler: 0x005d1410
    { "DenyAIHeroFlee", &Lua_DenyAIHeroFlee, 0x00f351b8 }, // handler: 0x005e22b0
    { "DenyAIHeroesFlee", &Lua_DenyAIHeroesFlee, 0x00f351c0 }, // handler: 0x005e2760
    { "DenyGarrisonCreaturesTakeAway", &Lua_DenyGarrisonCreaturesTakeAway, 0x00f34ee8 }, // handler: 0x005d1fc0
    { "DeployReserveHero", &Lua_DeployReserveHero, 0x00f34c60 }, // handler: 0x005c27f0
    { "DestroyTownBuildingToLevel", &Lua_DestroyTownBuildingToLevel, 0x00f34d48 }, // handler: 0x005c8140
    { "DisableAutoEnterTown", &Lua_DisableAutoEnterTown, 0x00f352a0 }, // handler: 0x005e6a00
    { "DisableCameraFollowHeroes", &Lua_DisableCameraFollowHeroes, 0x00f35138 }, // handler: 0x005df710
    { "DisableCameraFollowHeroesForPlayers", &Lua_DisableCameraFollowHeroesForPlayers, 0x00f35318 }, // handler: 0x005df870
    { "DoNotGiveTurnToPlayerAIIfNoTownsAndActiveHeroes", &Lua_DoNotGiveTurnToPlayerAIIfNoTownsAndActiveHeroes, 0x00f352c8 }, // handler: 0x005e7750
    { "EnableAIHeroHiring", &Lua_EnableAIHeroHiring, 0x00f35168 }, // handler: 0x005da7a0
    { "EnableAutoFinish", &Lua_EnableAutoFinish, 0x00f32ea8 }, // handler: 0x00567210
    { "EnableCinematicCamera", &Lua_EnableCinematicCamera, 0x00f33080 }, // handler: 0x0056e370
    { "EnableDynamicBattleMode", &Lua_EnableDynamicBattleMode, 0x00f32e90 }, // handler: 0x00566b60
    { "EnableHeroAI", &Lua_EnableHeroAI, 0x00f35150 }, // handler: 0x005da500
    { "Error", &Lua_Error, 0x00f7bd48 }, // handler: 0x00cad4c0
    { "ExitGame", &Lua_ExitGame, 0x00f35008 }, // handler: 0x005e1480
    { "Finish", &Lua_Finish, 0x00f32eb8 }, // handler: 0x005673f0
    { "GenerateMonsters", &Lua_GenerateMonsters, 0x00f34e38 }, // handler: 0x005cf8c0
    { "GetAllNames", &Lua_GetAllNames, 0x00f35030 }, // handler: 0x005d7f20
    { "GetArtifactSetItemsCount", &Lua_GetArtifactSetItemsCount, 0x00f34eb8 }, // handler: 0x005c57c0
    { "GetBuildingType", &Lua_GetBuildingType, 0x00f32f10 }, // handler: 0x00568b60
    { "GetCreatureNumber", &Lua_GetCreatureNumber, 0x00f32f00 }, // handler: 0x005686e0
    { "GetCreatureType", &Lua_GetCreatureType, 0x00f32ef8 }, // handler: 0x005684b0
    { "GetCurrentMoonWeek", &Lua_GetCurrentMoonWeek, 0x00f34f10 }, // handler: 0x005d2cc0
    { "GetCurrentPlayer", &Lua_GetCurrentPlayer, 0x00f35028 }, // handler: 0x005d73d0
    { "GetDate", &Lua_GetDate, 0x00f35020 }, // handler: 0x005d7170
    { "GetDifficulty", &Lua_GetDifficulty, 0x00f39a0c }, // handler: 0x006b5730
    { "GetGameVar", &Lua_GetGameVar, 0x00f32f60 }, // handler: 0x0056a3f0
    { "GetGameVar", &Lua_GetGameVar, 0x00f35110 }, // handler: 0x005dea90
    { "GetGameVar", &Lua_GetGameVar, 0x00f38144 }, // handler: 0x00663f40
    { "GetGlobalVar", &Lua_GetGlobalVar, 0x00f2df40 }, // handler: 0x004c1b00
    { "GetGuardsTier", &Lua_GetGuardsTier, 0x00f35188 }, // handler: 0x005e0c30
    { "GetHeroArtifactsCount", &Lua_GetHeroArtifactsCount, 0x00f34cc0 }, // handler: 0x005c4f70
    { "GetHeroCreatures", &Lua_GetHeroCreatures, 0x00f34cd8 }, // handler: 0x005c5ed0
    { "GetHeroCreaturesTypes", &Lua_GetHeroCreaturesTypes, 0x00f34ee0 }, // handler: 0x005ccb70
    { "GetHeroLevel", &Lua_GetHeroLevel, 0x00f34c78 }, // handler: 0x005c3190
    { "GetHeroName", &Lua_GetHeroName, 0x00f32ef0 }, // handler: 0x00568290
    { "GetHeroSkillMastery", &Lua_GetHeroSkillMastery, 0x00f34e88 }, // handler: 0x005c4140
    { "GetHeroStat", &Lua_GetHeroStat, 0x00f34c80 }, // handler: 0x005c33a0
    { "GetHeroTown", &Lua_GetHeroTown, 0x00f34e70 }, // handler: 0x005d0a90
    { "GetHost", &Lua_GetHost, 0x00f32ea0 }, // handler: 0x00567000
    { "GetLastSavedCombatIndex", &Lua_GetLastSavedCombatIndex, 0x00f35270 }, // handler: 0x005e5c50
    { "GetMapDataPath", &Lua_GetMapDataPath, 0x00f39a24 }, // handler: 0x006b5c70
    { "GetMaxFloor", &Lua_GetMaxFloor, 0x00f35000 }, // handler: 0x005e1570
    { "GetObjectArmySlotCreature", &Lua_GetObjectArmySlotCreature, 0x00f34f28 }, // handler: 0x005d33f0
    { "GetObjectCreatures", &Lua_GetObjectCreatures, 0x00f34dc0 }, // handler: 0x005cc590
    { "GetObjectCreaturesTypes", &Lua_GetObjectCreaturesTypes, 0x00f34ed8 }, // handler: 0x005cc820
    { "GetObjectDwellingCreatures", &Lua_GetObjectDwellingCreatures, 0x00f34dd8 }, // handler: 0x005cd530
    { "GetObjectNamesByType", &Lua_GetObjectNamesByType, 0x00f35190 }, // handler: 0x005e0660
    { "GetObjectOwner", &Lua_GetObjectOwner, 0x00f34d98 }, // handler: 0x005cb6d0
    { "GetObjectPosition", &Lua_GetObjectPosition, 0x00f34d88 }, // handler: 0x005caf40
    { "GetObjectiveProgress", &Lua_GetObjectiveProgress, 0x00f35058 }, // handler: 0x005d8ea0
    { "GetObjectiveState", &Lua_GetObjectiveState, 0x00f35048 }, // handler: 0x005d8710
    { "GetObjectsFromPath", &Lua_GetObjectsFromPath, 0x00f34e78 }, // handler: 0x005d0cf0
    { "GetObjectsInRegion", &Lua_GetObjectsInRegion, 0x00f34db0 }, // handler: 0x005cbf60
    { "GetPlayerHeroes", &Lua_GetPlayerHeroes, 0x00f34c30 }, // handler: 0x005c1540
    { "GetPlayerNecroEnergy", &Lua_GetPlayerNecroEnergy, 0x00f34f38 }, // handler: 0x005d3900
    { "GetPlayerRace", &Lua_GetPlayerRace, 0x00f34f18 }, // handler: 0x005d2eb0
    { "GetPlayerResource", &Lua_GetPlayerResource, 0x00f34c38 }, // handler: 0x005c19a0
    { "GetPlayerSelectedCampaignBonusIndex", &Lua_GetPlayerSelectedCampaignBonusIndex, 0x00f34f00 }, // handler: 0x005d2830
    { "GetPlayerState", &Lua_GetPlayerState, 0x00f34c28 }, // handler: 0x005c1390
    { "GetPlayerTeam", &Lua_GetPlayerTeam, 0x00f34f30 }, // handler: 0x005d3670
    { "GetRageLevel", &Lua_GetRageLevel, 0x00f33010 }, // handler: 0x0056e710
    { "GetRagePoints", &Lua_GetRagePoints, 0x00f33008 }, // handler: 0x0056e4a0
    { "GetSavedCombatArmyCreatureInfo", &Lua_GetSavedCombatArmyCreatureInfo, 0x00f35298 }, // handler: 0x005e6720
    { "GetSavedCombatArmyCreaturesCount", &Lua_GetSavedCombatArmyCreaturesCount, 0x00f35290 }, // handler: 0x005e64d0
    { "GetSavedCombatArmyHero", &Lua_GetSavedCombatArmyHero, 0x00f35288 }, // handler: 0x005e6260
    { "GetSavedCombatArmyPlayer", &Lua_GetSavedCombatArmyPlayer, 0x00f35280 }, // handler: 0x005e6030
    { "GetSavedCombatResult", &Lua_GetSavedCombatResult, 0x00f35278 }, // handler: 0x005e5e40
    { "GetSoundTimeInSleeps", &Lua_GetSoundTimeInSleeps, 0x00f39a2c }, // handler: 0x006b5e60
    { "GetStandState", &Lua_GetStandState, 0x00f34e58 }, // handler: 0x005d0360
    { "GetStandStatesCount", &Lua_GetStandStatesCount, 0x00f34e50 }, // handler: 0x005d0150
    { "GetTerrainSize", &Lua_GetTerrainSize, 0x00f35178 }, // handler: 0x005e0f90
    { "GetTownBuildingLevel", &Lua_GetTownBuildingLevel, 0x00f34d28 }, // handler: 0x005c7590
    { "GetTownBuildingLimitLevel", &Lua_GetTownBuildingLimitLevel, 0x00f34d38 }, // handler: 0x005c7b10
    { "GetTownBuildingMaxLevel", &Lua_GetTownBuildingMaxLevel, 0x00f34d30 }, // handler: 0x005c7850
    { "GetTownHero", &Lua_GetTownHero, 0x00f34e68 }, // handler: 0x005d0840
    { "GetTownRace", &Lua_GetTownRace, 0x00f35378 }, // handler: 0x005e8c30
    { "GetTurnTimeLeft", &Lua_GetTurnTimeLeft, 0x00f35358 }, // handler: 0x005e8300
    { "GetUnitManaPoints", &Lua_GetUnitManaPoints, 0x00f32f28 }, // handler: 0x00569570
    { "GetUnitMaxManaPoints", &Lua_GetUnitMaxManaPoints, 0x00f32f30 }, // handler: 0x00569790
    { "GetUnitPosition", &Lua_GetUnitPosition, 0x00f32ee0 }, // handler: 0x00568060
    { "GetUnitSide", &Lua_GetUnitSide, 0x00f32ec8 }, // handler: 0x005677a0
    { "GetUnitType", &Lua_GetUnitType, 0x00f32ed0 }, // handler: 0x005679f0
    { "GetUnits", &Lua_GetUnits, 0x00f32ed8 }, // handler: 0x00567c70
    { "GetWarMachineType", &Lua_GetWarMachineType, 0x00f32f08 }, // handler: 0x00568930
    { "GiveArtefact", &Lua_GiveArtefact, 0x00f34cc8 }, // handler: 0x005c5200
    { "GiveBorderguardKey", &Lua_GiveBorderguardKey, 0x00f34c58 }, // handler: 0x005c2540
    { "GiveHeroBattleBonus", &Lua_GiveHeroBattleBonus, 0x00f35370 }, // handler: 0x005e89b0
    { "GiveHeroSkill", &Lua_GiveHeroSkill, 0x00f34cb0 }, // handler: 0x005c43a0
    { "GiveHeroWarMachine", &Lua_GiveHeroWarMachine, 0x00f34d08 }, // handler: 0x005c6a50
    { "HasArtefact", &Lua_HasArtefact, 0x00f34cb8 }, // handler: 0x005c4cd0
    { "HasBorderguardKey", &Lua_HasBorderguardKey, 0x00f34c50 }, // handler: 0x005c22a0
    { "HasHeroSkill", &Lua_HasHeroSkill, 0x00f34c98 }, // handler: 0x005c3ee0
    { "HasHeroWarMachine", &Lua_HasHeroWarMachine, 0x00f34d00 }, // handler: 0x005c67f0
    { "IsAIPlayer", &Lua_IsAIPlayer, 0x00f34f20 }, // handler: 0x005d3140
    { "IsEqual", &Lua_IsEqual, 0x00f2df30 }, // handler: 0x004c19e0
    { "IsHeroAlive", &Lua_IsHeroAlive, 0x00f34c70 }, // handler: 0x005c2f80
    { "IsHeroInBoat", &Lua_IsHeroInBoat, 0x00f351f8 }, // handler: 0x005e3580
    { "IsHeroInTown", &Lua_IsHeroInTown, 0x00f34ec8 }, // handler: 0x005c9c50
    { "IsHeroLootable", &Lua_IsHeroLootable, 0x00f34cf0 }, // handler: 0x005c5a70
    { "IsObjectEnabled", &Lua_IsObjectEnabled, 0x00f34de8 }, // handler: 0x005cdbd0
    { "IsObjectExists", &Lua_IsObjectExists, 0x00f34d50 }, // handler: 0x005c8520
    { "IsObjectInRegion", &Lua_IsObjectInRegion, 0x00f34da8 }, // handler: 0x005cbcd0
    { "IsObjectVisible", &Lua_IsObjectVisible, 0x00f34db8 }, // handler: 0x005cc2a0
    { "IsObjectiveVisible", &Lua_IsObjectiveVisible, 0x00f35038 }, // handler: 0x005d8120
    { "IsPlayerCurrent", &Lua_IsPlayerCurrent, 0x00f35328 }, // handler: 0x005d75c0
    { "IsPlayerInGhostMode", &Lua_IsPlayerInGhostMode, 0x00f35330 }, // handler: 0x005d7810
    { "IsPlayerLost", &Lua_IsPlayerLost, 0x00f35348 }, // handler: 0x005e7db0
    { "IsPlayerWaitingForTurn", &Lua_IsPlayerWaitingForTurn, 0x00f35338 }, // handler: 0x005d7a60
    { "IsRegionBlocked", &Lua_IsRegionBlocked, 0x00f35070 }, // handler: 0x005d97a0
    { "IsTeamCurrent", &Lua_IsTeamCurrent, 0x00f35340 }, // handler: 0x005d7cb0
    { "IsTilePassable", &Lua_IsTilePassable, 0x00f35180 }, // handler: 0x005e11a0
    { "IsTutorialEnabled", &Lua_IsTutorialEnabled, 0x00f3e02c }, // handler: 0x007742f0
    { "IsTutorialItemEnabled", &Lua_IsTutorialItemEnabled, 0x00f3e004 }, // handler: 0x00773c00
    { "IsTutorialMessageBoxOpen", &Lua_IsTutorialMessageBoxOpen, 0x00f3e024 }, // handler: 0x00774160
    { "IsValid", &Lua_IsValid, 0x00f2df28 }, // handler: 0x00b2d470
    { "KnowHeroSpell", &Lua_KnowHeroSpell, 0x00f34ca0 }, // handler: 0x005c4770
    { "LevelUpHero", &Lua_LevelUpHero, 0x00f34c90 }, // handler: 0x005c3c00
    { "Load", &Lua_Load, 0x00f39a1c }, // handler: 0x006b5aa0
    { "LockMinHeroSkillsAndAttributes", &Lua_LockMinHeroSkillsAndAttributes, 0x00f352b0 }, // handler: 0x005e7010
    { "Loose", &Lua_Loose, 0x00f35018 }, // handler: 0x005d6ed0
    { "LuaTest", &Lua_LuaTest, 0x00f2df48 }, // handler: 0x004c2100
    { "MakeHeroInteractWithObject", &Lua_MakeHeroInteractWithObject, 0x00f34ef0 }, // handler: 0x005d2220
    { "MakeHeroNecromancer", &Lua_MakeHeroNecromancer, 0x00f35250 }, // handler: 0x005e4d50
    { "MakeHeroReturnToTavernAfterDeath", &Lua_MakeHeroReturnToTavernAfterDeath, 0x00f35240 }, // handler: 0x005e4fc0
    { "MakeTownMovable", &Lua_MakeTownMovable, 0x00f35260 }, // handler: 0x005e5770
    { "MarkObjectAsVisited", &Lua_MarkObjectAsVisited, 0x00f34df8 }, // handler: 0x005ce0f0
    { "MessageBox", &Lua_MessageBox, 0x00f33018 }, // handler: 0x0056e980
    { "MessageBox", &Lua_MessageBox, 0x00f350b0 }, // handler: 0x005db320
    { "MessageBoxForPlayers", &Lua_MessageBoxForPlayers, 0x00f352e0 }, // handler: 0x005db910
    { "MessageBoxInt", &Lua_MessageBoxInt, 0x00f350a0 }, // handler: 0x005db320
    { "MoveCamera", &Lua_MoveCamera, 0x00f35130 }, // handler: 0x005df150
    { "MoveCameraForPlayers", &Lua_MoveCameraForPlayers, 0x00f35310 }, // handler: 0x005df430
    { "MoveHero", &Lua_MoveHero, 0x00f34e18 }, // handler: 0x005cef50
    { "MoveHeroRealTime", &Lua_MoveHeroRealTime, 0x00f34e20 }, // handler: 0x005cf310
    { "ObjPtr", &Lua_ObjPtr, 0x00f2df20 }, // handler: 0x00b2d320
    { "OpenCircleFog", &Lua_OpenCircleFog, 0x00f35088 }, // handler: 0x005da1e0
    { "OpenPuzzleMap", &Lua_OpenPuzzleMap, 0x00f351d0 }, // handler: 0x005e29f0
    { "OpenRegionFog", &Lua_OpenRegionFog, 0x00f35080 }, // handler: 0x005d9df0
    { "OverrideAdvMapDialogPos", &Lua_OverrideAdvMapDialogPos, 0x00f351e8 }, // handler: 0x005e3090
    { "OverrideObjectTooltipNameAndDescription", &Lua_OverrideObjectTooltipNameAndDescription, 0x00f352c0 }, // handler: 0x005e74e0
    { "Play2DSound", &Lua_Play2DSound, 0x00f33028 }, // handler: 0x0056eff0
    { "Play2DSound", &Lua_Play2DSound, 0x00f350c8 }, // handler: 0x005dc970
    { "Play2DSoundForPlayers", &Lua_Play2DSoundForPlayers, 0x00f352f0 }, // handler: 0x005dcf40
    { "Play3DSound", &Lua_Play3DSound, 0x00f33030 }, // handler: 0x0056f1e0
    { "Play3DSound", &Lua_Play3DSound, 0x00f350d0 }, // handler: 0x005dcb90
    { "Play3DSoundForPlayers", &Lua_Play3DSoundForPlayers, 0x00f352f8 }, // handler: 0x005dd170
    { "PlayObjectAnimation", &Lua_PlayObjectAnimation, 0x00f351a8 }, // handler: 0x005e1d90
    { "PlayVisualEffect", &Lua_PlayVisualEffect, 0x00f35198 }, // handler: 0x005e1710
    { "Ptr", &Lua_Ptr, 0x00f2df18 }, // handler: 0x00b2d200
    { "QuestionBox", &Lua_QuestionBox, 0x00f350b8 }, // handler: 0x005db5f0
    { "QuestionBoxForPlayers", &Lua_QuestionBoxForPlayers, 0x00f352e8 }, // handler: 0x005dbc30
    { "QuestionBoxInt", &Lua_QuestionBoxInt, 0x00f350a8 }, // handler: 0x005db5f0
    { "RazeBuilding", &Lua_RazeBuilding, 0x00f34d78 }, // handler: 0x005ca830
    { "RazeTown", &Lua_RazeTown, 0x00f34d70 }, // handler: 0x005ca3f0
    { "RegionToPoint", &Lua_RegionToPoint, 0x00f35068 }, // handler: 0x005d9540
    { "RemoveAllMonsters", &Lua_RemoveAllMonsters, 0x00f34e40 }, // handler: 0x005cfba0
    { "RemoveAllUnits", &Lua_RemoveAllUnits, 0x00f32f98 }, // handler: 0x0056b400
    { "RemoveArtefact", &Lua_RemoveArtefact, 0x00f34cd0 }, // handler: 0x005c54d0
    { "RemoveHeroCreatures", &Lua_RemoveHeroCreatures, 0x00f34ce8 }, // handler: 0x005c64a0
    { "RemoveHeroWarMachine", &Lua_RemoveHeroWarMachine, 0x00f34d10 }, // handler: 0x005c6d10
    { "RemoveObject", &Lua_RemoveObject, 0x00f34d80 }, // handler: 0x005cacb0
    { "RemoveObjectCreatures", &Lua_RemoveObjectCreatures, 0x00f34de0 }, // handler: 0x005cd7b0
    { "ReplaceDwelling", &Lua_ReplaceDwelling, 0x00f34eb0 }, // handler: 0x005d1c50
    { "ResetHeroCombatScript", &Lua_ResetHeroCombatScript, 0x00f34d20 }, // handler: 0x005c72e0
    { "Save", &Lua_Save, 0x00f39a14 }, // handler: 0x006b58d0
    { "SetAIHeroAttractor", &Lua_SetAIHeroAttractor, 0x00f35158 }, // handler: 0x005dfe10
    { "SetAIHeroFleeControl", &Lua_SetAIHeroFleeControl, 0x00f351c8 }, // handler: 0x005e2510
    { "SetAIPlayerAttractor", &Lua_SetAIPlayerAttractor, 0x00f35160 }, // handler: 0x005e00e0
    { "SetAmbientLight", &Lua_SetAmbientLight, 0x00f35090 }, // handler: 0x005dab00
    { "SetAmbientLightForPlayers", &Lua_SetAmbientLightForPlayers, 0x00f352d8 }, // handler: 0x005dad90
    { "SetCombatAmbientLight", &Lua_SetCombatAmbientLight, 0x00f35170 }, // handler: 0x005e03d0
    { "SetControlMode", &Lua_SetControlMode, 0x00f32e98 }, // handler: 0x00566d70
    { "SetDisabledObjectMode", &Lua_SetDisabledObjectMode, 0x00f35258 }, // handler: 0x005e54b0
    { "SetGameVar", &Lua_SetGameVar, 0x00f32f68 }, // handler: 0x0056a660
    { "SetGameVar", &Lua_SetGameVar, 0x00f35118 }, // handler: 0x005decb0
    { "SetGameVar", &Lua_SetGameVar, 0x00f3814c }, // handler: 0x00664160
    { "SetGlobalVar", &Lua_SetGlobalVar, 0x00f2df38 }, // handler: 0x004c1cc0
    { "SetHeroBiography", &Lua_SetHeroBiography, 0x00f34f08 }, // handler: 0x005d2a50
    { "SetHeroCombatScript", &Lua_SetHeroCombatScript, 0x00f34d18 }, // handler: 0x005c6fd0
    { "SetHeroLootable", &Lua_SetHeroLootable, 0x00f34cf8 }, // handler: 0x005c5c80
    { "SetHeroRoleMode", &Lua_SetHeroRoleMode, 0x00f352b8 }, // handler: 0x005e7260
    { "SetHeroesExpCoef", &Lua_SetHeroesExpCoef, 0x00f351d8 }, // handler: 0x005e2c70
    { "SetMonsterCourageAndMood", &Lua_SetMonsterCourageAndMood, 0x00f34ed0 }, // handler: 0x005c9240
    { "SetMonsterNames", &Lua_SetMonsterNames, 0x00f34ec0 }, // handler: 0x005c8fd0
    { "SetMonsterSelectionType", &Lua_SetMonsterSelectionType, 0x00f34e00 }, // handler: 0x005ce490
    { "SetObjectDwellingCreatures", &Lua_SetObjectDwellingCreatures, 0x00f34dd0 }, // handler: 0x005cd270
    { "SetObjectEnabled", &Lua_SetObjectEnabled, 0x00f34df0 }, // handler: 0x005cde30
    { "SetObjectFlashlight", &Lua_SetObjectFlashlight, 0x00f35098 }, // handler: 0x005db020
    { "SetObjectOwner", &Lua_SetObjectOwner, 0x00f34da0 }, // handler: 0x005cb910
    { "SetObjectPosition", &Lua_SetObjectPosition, 0x00f34d90 }, // handler: 0x005cb1b0
    { "SetObjectRotation", &Lua_SetObjectRotation, 0x00f34e90 }, // handler: 0x005cb440
    { "SetObjectiveProgress", &Lua_SetObjectiveProgress, 0x00f35060 }, // handler: 0x005d9170
    { "SetObjectiveState", &Lua_SetObjectiveState, 0x00f35050 }, // handler: 0x005d89e0
    { "SetObjectiveVisible", &Lua_SetObjectiveVisible, 0x00f35040 }, // handler: 0x005d83f0
    { "SetPlayerHeroesCountNotForHire", &Lua_SetPlayerHeroesCountNotForHire, 0x00f34e80 }, // handler: 0x005d11c0
    { "SetPlayerResource", &Lua_SetPlayerResource, 0x00f34c40 }, // handler: 0x005c1c30
    { "SetPlayerStartResources", &Lua_SetPlayerStartResources, 0x00f34c48 }, // handler: 0x005c1fb0
    { "SetPlayerTeam", &Lua_SetPlayerTeam, 0x00f351b0 }, // handler: 0x005e2030
    { "SetRegionAutoObjectEnable", &Lua_SetRegionAutoObjectEnable, 0x00f352a8 }, // handler: 0x005e6ca0
    { "SetRegionBlocked", &Lua_SetRegionBlocked, 0x00f35078 }, // handler: 0x005d9a50
    { "SetStandState", &Lua_SetStandState, 0x00f34e60 }, // handler: 0x005d0570
    { "SetTownBuildingLimitLevel", &Lua_SetTownBuildingLimitLevel, 0x00f34d40 }, // handler: 0x005c7dd0
    { "SetTrigger", &Lua_SetTrigger, 0x00f350d8 }, // handler: 0x005dd530
    { "SetUnitManaPoints", &Lua_SetUnitManaPoints, 0x00f32f38 }, // handler: 0x005699b0
    { "SetWarfogBehaviour", &Lua_SetWarfogBehaviour, 0x00f35148 }, // handler: 0x005df9d0
    { "ShowFlyingSign", &Lua_ShowFlyingSign, 0x00f33020 }, // handler: 0x0056ebe0
    { "ShowFlyingSign", &Lua_ShowFlyingSign, 0x00f350c0 }, // handler: 0x005dc450
    { "SiegeTown", &Lua_SiegeTown, 0x00f34e30 }, // handler: 0x005d4460
    { "SinkHero", &Lua_SinkHero, 0x00f35200 }, // handler: 0x005e37f0
    { "Sleep", &Lua_Sleep, 0x00f2df00 }, // handler: 0x004bfe80
    { "StartAdvMapDialog", &Lua_StartAdvMapDialog, 0x00f351e0 }, // handler: 0x005e2e80
    { "StartCombat", &Lua_StartCombat, 0x00f34e28 }, // handler: 0x005d3b90
    { "StartCutScene", &Lua_StartCutScene, 0x00f35108 }, // handler: 0x005de340
    { "StartCutSceneForPlayers", &Lua_StartCutSceneForPlayers, 0x00f35308 }, // handler: 0x005de820
    { "StartCutSceneInt", &Lua_StartCutSceneInt, 0x00f350f8 }, // handler: 0x005de340
    { "StartDialogScene", &Lua_StartDialogScene, 0x00f35100 }, // handler: 0x005de0d0
    { "StartDialogSceneForPlayers", &Lua_StartDialogSceneForPlayers, 0x00f35300 }, // handler: 0x005de5a0
    { "StartDialogSceneInt", &Lua_StartDialogSceneInt, 0x00f350f0 }, // handler: 0x005de0d0
    { "StartThread", &Lua_StartThread, 0x00f2df08 }, // handler: 0x004bfe20
    { "StopTrigger", &Lua_StopTrigger, 0x00f350e8 }, // handler: 0x005ddfa0
    { "StopVisualEffects", &Lua_StopVisualEffects, 0x00f351a0 }, // handler: 0x005e1bb0
    { "SummonCreature", &Lua_SummonCreature, 0x00f32f20 }, // handler: 0x00569180
    { "TakeAwayHeroExp", &Lua_TakeAwayHeroExp, 0x00f35268 }, // handler: 0x005e5a00
    { "TalkBoxForPlayers", &Lua_TalkBoxForPlayers, 0x00f35380 }, // handler: 0x005dbfa0
    { "TeachHeroSpell", &Lua_TeachHeroSpell, 0x00f34ca8 }, // handler: 0x005c49e0
    { "TransformTown", &Lua_TransformTown, 0x00f34d68 }, // handler: 0x005c9ee0
    { "Trigger", &Lua_Trigger, 0x00f350e0 }, // handler: 0x005dd530
    { "TutorialActivateHint", &Lua_TutorialActivateHint, 0x00f3e00c }, // handler: 0x00773da0
    { "TutorialMessageBox", &Lua_TutorialMessageBox, 0x00f3e01c }, // handler: 0x00774020
    { "TutorialSetBlink", &Lua_TutorialSetBlink, 0x00f3e014 }, // handler: 0x00773ee0
    { "UnblockGame", &Lua_UnblockGame, 0x00f35128 }, // handler: 0x005df000
    { "UnitCastAimedSpell", &Lua_UnitCastAimedSpell, 0x00f32f50 }, // handler: 0x0056a080
    { "UnitCastAreaSpell", &Lua_UnitCastAreaSpell, 0x00f32f48 }, // handler: 0x00569e60
    { "UnitCastGlobalSpell", &Lua_UnitCastGlobalSpell, 0x00f32f40 }, // handler: 0x00569c30
    { "UnreserveHero", &Lua_UnreserveHero, 0x00f34c68 }, // handler: 0x005c2c90
    { "UpgradeTownBuilding", &Lua_UpgradeTownBuilding, 0x00f352d0 }, // handler: 0x005e7a30
    { "WarpHeroExp", &Lua_WarpHeroExp, 0x00f35368 }, // handler: 0x005e8720
    { "WarpToMap", &Lua_WarpToMap, 0x00f35360 }, // handler: 0x005e85c0
    { "Win", &Lua_Win, 0x00f35010 }, // handler: 0x005d69d0
    { "WinTeam", &Lua_WinTeam, 0x00f35320 }, // handler: 0x005d6c70
    { "addUnit", &Lua_addUnit, 0x00f32f78 }, // handler: 0x0056a9f0
    { "ceil", &Lua_ceil, 0x00f7bd18 }, // handler: 0x00cac330
    { "changeSubject", &Lua_changeSubject, 0x00f33058 }, // handler: 0x0056dbc0
    { "clearMessage", &Lua_clearMessage, 0x00f33050 }, // handler: 0x0056da90
    { "combatEnableFinish", &Lua_combatEnableFinish, 0x00f32eb0 }, // handler: 0x00567210
    { "combatPlayEmotion", &Lua_combatPlayEmotion, 0x00f32ff8 }, // handler: 0x0056d5c0
    { "combatReadyPerson", &Lua_combatReadyPerson, 0x00f32fe0 }, // handler: 0x0056cd60
    { "combatSetPause", &Lua_combatSetPause, 0x00f32ff0 }, // handler: 0x0056d3c0
    { "combatStarted", &Lua_combatStarted, 0x00f32fd8 }, // handler: 0x0056cbd0
    { "commandDefend", &Lua_commandDefend, 0x00f32fa8 }, // handler: 0x0056b950
    { "commandDoSpecial", &Lua_commandDoSpecial, 0x00f32fc8 }, // handler: 0x0056c4b0
    { "commandDoSpell", &Lua_commandDoSpell, 0x00f32fd0 }, // handler: 0x0056c830
    { "commandMove", &Lua_commandMove, 0x00f32fb0 }, // handler: 0x0056bb30
    { "commandMoveAttack", &Lua_commandMoveAttack, 0x00f32fb8 }, // handler: 0x0056bec0
    { "commandShot", &Lua_commandShot, 0x00f32fc0 }, // handler: 0x0056c200
    { "consoleCmd", &Lua_consoleCmd, 0x00f7bcf0 }, // handler: 0x00cabca0
    { "displace", &Lua_displace, 0x00f32fa0 }, // handler: 0x0056b680
    { "doFile", &Lua_doFile, 0x00f7bcf8 }, // handler: 0x00cabda0
    { "errorHook", &Lua_errorHook, 0x00f7bcc8 }, // handler: 0x004bff20
    { "exist", &Lua_exist, 0x00f32f80 }, // handler: 0x0056ae00
    { "floor", &Lua_floor, 0x00f7bd10 }, // handler: 0x00cac200
    { "frac", &Lua_frac, 0x00f7bd28 }, // handler: 0x00cac5b0
    { "intg", &Lua_intg, 0x00f7bd20 }, // handler: 0x00cac460
    { "isEqual", &Lua_isEqual, 0x00f7bcd0 }, // handler: 0x004c19e0
    { "mod", &Lua_mod, 0x00f7bd00 }, // handler: 0x00cabf00
    { "out", &Lua_out, 0x00f2def8 }, // handler: 0x004c1200
    { "parse", &Lua_parse, 0x00f7bce0 }, // handler: 0x00cabb60
    { "playAnimation", &Lua_playAnimation, 0x00f33000 }, // handler: 0x0056d170
    { "pos", &Lua_pos, 0x00f32ee8 }, // handler: 0x00568060
    { "postEvent", &Lua_postEvent, 0x00f32f58 }, // handler: 0x0056a2f0
    { "print", &Lua_print, 0x00f7bce8 }, // handler: 0x00cac850
    { "random", &Lua_random, 0x00f2df10 }, // handler: 0x004c0da0
    { "random", &Lua_random, 0x00f35140 }, // handler: 0x005dfc30
    { "removeUnit", &Lua_removeUnit, 0x00f32f90 }, // handler: 0x0056b1d0
    { "round", &Lua_round, 0x00f7bd30 }, // handler: 0x00cac710
    { "setATB", &Lua_setATB, 0x00f32fe8 }, // handler: 0x0056cf60
    { "shots", &Lua_shots, 0x00f33068 }, // handler: 0x0056df10
    { "shotsNumber", &Lua_shotsNumber, 0x00f33070 }, // handler: 0x0056e180
    { "showHighlighting", &Lua_showHighlighting, 0x00f32f70 }, // handler: 0x0056a890
    { "showMessage", &Lua_showMessage, 0x00f33048 }, // handler: 0x0056d900
    { "sleep", &Lua_sleep, 0x00f7bcb8 }, // handler: 0x004bfe80
    { "sqrt", &Lua_sqrt, 0x00f7bd08 }, // handler: 0x00cac090
    { "startThread", &Lua_startThread, 0x00f7bcc0 }, // handler: 0x004bfe20
    { "subject", &Lua_subject, 0x00f33060 }, // handler: 0x0056ddb0
    { "toggleTutorialMode", &Lua_toggleTutorialMode, 0x00f33040 }, // handler: 0x0056d7d0
    { "unitNames", &Lua_unitNames, 0x00f32f88 }, // handler: 0x0056afa0
};
