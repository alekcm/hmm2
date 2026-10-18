// ============================================================================
// Decompiled NWorld Gameplay Functions & Formulas (bin/H5_Game_PEST.exe)
// Exact C++ reconstructions verified against the x86 machine code in .text
// and binary tables in .rdata / .data.
// ============================================================================

#include <cstdint>
#include <cmath>
#include <algorithm>

namespace NWorld {

// ----------------------------------------------------------------------------
// Exported PE Symbols (SFLB*)
// ----------------------------------------------------------------------------
// Export ordinal #1: ?SFLB0_GetUnitExperienceValue@NWorld@@YIHABUSCreatureKillInfo@1@ABK@Z @ VA 0x004d8ba0
// Export ordinal #2: ?SFLB2_InitVariables@CSeed@NRandom@@AAEXXZ @ VA 0x00b25600
// Export ordinal #3: ?SFLB_AddObservers@CAdventureScreen@NUI@@AAEXXZ @ VA 0x0059a210
// Export ordinal #4: ?SFLB_AddObservers@CTownScreen@NUI@@AAEXXZ @ VA 0x0061bcf0
// Export ordinal #5: ?SFLB_AttemptGameStart@CStateWaitingForPlayers@NUbi@@AAE_NPAUSContext@2@@Z @ VA 0x00c91190
// Export ordinal #6: ?SFLB_CombineEditablesStep@NWorld@@YIXPAUSAdvMapHeroEditable@NDb@@PBU23@1H@Z @ VA 0x009a9c40
// Export ordinal #7: ?SFLB_CreateWin@@YI_NPBD0II0@Z @ VA 0x00c3bbe0
// Export ordinal #8: ?SFLB_GameEnterStep@NLAN@NUbi@@YIXPAUSGameEnter@2@ABUSGameInfo@2@ABV?$basic_string@G@nstl@@PAUIStatusListener@2@@Z @ VA 0x00c8ec00
// Export ordinal #9: ?SFLB_InitApplication@NWinFrame@@YI_NPAUHINSTANCE__@@PBD11@Z @ VA 0x00c3bcb0
// Export ordinal #10: ?SFLB_InitDay@CPlayerAI@NWorld@@AAEXXZ @ VA 0x00867440
// Export ordinal #11: ?SFLB_ParseServers@CStateUninitialized@NUbi@@AAEXABV?$basic_string@D@nstl@@000@Z @ VA 0x0092d500

// ============================================================================
// Function:  NDb::GetDefaultStats
// VA:        0x00bcb3f0
// Subsystem: NDb
// Signature: const NDb::RPGStats* __cdecl GetDefaultStats()
// Summary:   Returns singleton pointer to DefaultStats.xdb (ds:0x01043a00); called 499 times across combat, hero, AI, and town subsystems.
// ============================================================================
/*
   Decompiled Formula / Logic:
   return ds_01043a00 ? ds_01043a00 : LoadDefaultStats(ds_010438c0);
*/

// ============================================================================
// Function:  NDb::GetCreatureDescById
// VA:        0x0089fbf0
// Subsystem: NDb
// Signature: const NDb::Creature* __fastcall GetCreatureDescById(int creatureId)
// Summary:   Bounds-checks creature ID against 0xb4 (180) and looks up the creature record in Table_Creature_CreatureId (ds:0x01024c04).
// ============================================================================
/*
   Decompiled Formula / Logic:
   return (creatureId >= 0 && creatureId < 180) ? Table_Creature[creatureId] : Table_Creature[0];
*/

// ============================================================================
// Function:  SFLB0_GetUnitExperienceValue
// VA:        0x004d8ba0
// Subsystem: NWorld::Combat
// Signature: int __stdcall SFLB0_GetUnitExperienceValue(const SUnitExpInfo* info)
// Summary:   Computes XP awarded for defeating a creature stack. Returns 0 if gated/summoned (info[+8] != 0), otherwise multiplies creature Exp (+0x80) or Power (+0x64) by stack count (info[+4]).
// ============================================================================
/*
   Decompiled Formula / Logic:
   info->isSummoned ? 0 : (creature->Exp > 0 ? creature->Exp : creature->Power) * info->count
*/

// ============================================================================
// Function:  NWorld::PackTileCoord
// VA:        0x00419690
// Subsystem: NWorld::Map
// Signature: uint32_t __fastcall PackTileCoord(int x, int y, int floor)
// Summary:   Packs (x, y, floor) into a 30-bit integer key used by CTileGameInfoMap and Lua C-API.
// ============================================================================
/*
   Decompiled Formula / Logic:
   (x & 0x3FF) | ((y & 0x3FF) << 10) | ((floor & 0x3FF) << 20)
*/

// ============================================================================
// Function:  NWorld::CTileGameInfoMap::GetTile
// VA:        0x00bcc310
// Subsystem: NWorld::Map
// Signature: const STileGameInfo* __thiscall GetTile(const CTileGameInfoMap* this, uint32_t packedCoord)
// Summary:   Indexes floor array ([this+0x20] + floor*88 + 4), row pointer y, and column x*36 (sizeof(STileGameInfo) == 36).
// ============================================================================
/*
   Decompiled Formula / Logic:
   floorLayers[floor].rows[y][x] // sizeof(STileGameInfo) == 36 bytes
*/

// ============================================================================
// Function:  NWorld::IsTilePassable
// VA:        0x005e11a0
// Subsystem: NWorld::Map
// Signature: bool __cdecl IsTilePassable(int x, int y, int floor)
// Summary:   Checks whether a map tile is passable for standard ground/underground movement.
// ============================================================================
/*
   Decompiled Formula / Logic:
   (tile->passabilityFlag == 1 || tile->passabilityFlag == 4) && tile->walkableState == 1
*/

// ============================================================================
// Function:  NWorld::STileGameInfo::CalcMoveCost
// VA:        0x00ca9a70
// Subsystem: NWorld::Pathfinding
// Signature: int __thiscall CalcMoveCost(const STileGameInfo* this, const SHeroMoveModifiers* mods)
// Summary:   Computes orthogonal step cost on a tile: 75 on road (roadType in {6,7,8}), 100 on native terrain, or 100 + penalty (+25 or +50 from jump table 0x00ca9be8) on non-native terrain, modified by Pathfinding (*0.5f), Snatch (0), and percent modifiers.
// ============================================================================
/*
   Decompiled Formula / Logic:
   road in {6,7,8} ? 75 : 100 + ModifyPenalty(terrain == native ? 0 : JumpTable_00ca9be8[terrain])
*/

// ============================================================================
// Function:  NWorld::CHeroMovementCost::GetStepCost
// VA:        0x00cab090
// Subsystem: NWorld::Pathfinding
// Signature: int __thiscall GetStepCost(const CHeroMovementCost* this, const STileCoord* from, const STileCoord* to, int curMovePoints, bool* outStop)
// Summary:   Computes full hero step cost between adjacent tiles, multiplying by sqrt(2) (1.41421354f at 0x00e557b8) when dx != 0 && dy != 0.
// ============================================================================
/*
   Decompiled Formula / Logic:
   int(STileGameInfo::CalcMoveCost(toTile, &this->mods) * (dx != 0 && dy != 0 ? 1.41421354f : 1.0f))
*/

// ============================================================================
// Function:  NWorld::CTetraMovementCost::GetStepCost
// VA:        0x00876850
// Subsystem: NWorld::Pathfinding
// Signature: int __thiscall GetStepCost(const CTetraMovementCost* this, const STileCoord* from, const STileCoord* to, int curMovePoints, bool* outStop)
// Summary:   Computes step cost for 2x2 / multi-tile walkers by taking the maximum CalcMoveCost across all footprint tiles.
// ============================================================================
/*
   Decompiled Formula / Logic:
   int(max_{cell in footprint}(CalcMoveCost(cell)) * (isDiagonal ? 1.41421354f : 1.0f))
*/

// ============================================================================
// Function:  NWorld::InitHeroLevelExperienceTable
// VA:        0x00b4c6b0
// Subsystem: NWorld::Hero
// Signature: void __cdecl InitHeroLevelExperienceTable()
// Summary:   Populates the global Hero Level XP table at 0x01026d90 from seed table 0x00f755e0 (Levels 1..12), *1.2f delta for Levels 13..24, and *(i-13)*0.1f delta for Levels 25..40.
// ============================================================================
/*
   Decompiled Formula / Logic:
   i<=12: seed[i-1]; 13<=i<=24: xp[i-2] + int((xp[i-2]-xp[i-3])*1.2f); i>=25: xp[i-2] + int((xp[i-2]-xp[i-3])*(i-13)*0.1f)
*/

// ============================================================================
// Function:  NWorld::CHero::GetStat
// VA:        0x00b5ad70
// Subsystem: NWorld::Hero
// Signature: int __thiscall GetStat(const CHero* this, int statId)
// Summary:   Internal CHero stat accessor dispatch accepts statId 1..10 via the 10-slot table at [this+0x264]. This is distinct from the Lua public STAT_* constants: Lua ChangeHeroStat accepts 0..8 and Lua GetHeroStat's wrapper admits an additional unexposed slot 9.
// ============================================================================
/*
   Decompiled Formula / Logic:
   (statId >= 1 && statId <= 10) ? this->statAccessors[statId - 1](this) : 0
*/

// ============================================================================
// Function:  NWorld::CHero::GetPrimaryAndSecondaryStats
// VA:        0x00b4ca10
// Subsystem: NWorld::Hero
// Signature: int __thiscall GetAttack/GetDefense/GetSpellPower/GetKnowledge/GetMorale/GetLuck(const CHero* this)
// Summary:   Computes Hero Attack (0x00b4cc50 >=0), Defense (0x00b4ccc0 >=0), SpellPower (0x00b4ccf0 >=1), Knowledge (0x00b4cd80 >=1), ClampedMorale (0x00b4cb40 [-5..+5]), and ClampedLuck (0x00b4cb60 [-5..+5]).
// ============================================================================
/*
   Decompiled Formula / Logic:
   Attack=max(0, base+bonus); Defense=max(0, base+bonus); SpellPower=max(1, base+bonus); Knowledge=max(1, base+bonus); Morale=clamp(raw,-5,5); Luck=clamp(raw,-5,5)
*/

// ============================================================================
// Function:  NWorld::CalcAttackDefenseMultiplier
// VA:        0x00a58d30
// Subsystem: NWorld::Combat
// Signature: float __stdcall CalcAttackDefenseMultiplier(float attack, float defense)
// Summary:   Computes HoMM5 physical damage Attack-vs-Defense multiplier using DefaultStats[+0x1d0] (0.05f) when Attack > Defense and reciprocal 1 / (1 + DefaultStats[+0x1d8] * (Defense - Attack)) when Defense >= Attack.
// ============================================================================
/*
   Decompiled Formula / Logic:
   (Attack > Defense) ? (1.0f + 0.05f * (Attack - Defense)) : (1.0f / (1.0f + 0.05f * (Defense - Attack)))
*/

// ============================================================================
// Function:  NWorld::CalcBaseStackDamage
// VA:        0x00a597a0
// Subsystem: NWorld::Combat
// Signature: float __fastcall CalcBaseStackDamage(CCombat* combat, CCombatCreature* attacker, CCombatCreature* defender, int mode, bool isRanged, bool isRetaliation)
// Summary:   Computes stack base damage for Roll (mode=0), Min (mode=1), Avg (mode=2), or Max (mode=3). When mode=0 and stackCount > 3, averages 3 random rolls in [MinDamage, MaxDamage] and multiplies by stackCount * 0.33333334f (0x00e19950).
// ============================================================================
/*
   Decompiled Formula / Logic:
   mode==1 ? N*minDmg : mode==2 ? N*(minDmg+maxDmg)*0.5f : mode==3 ? N*maxDmg : (N<=3 ? sum_{1..N} rand(min,max) : N * (rand1+rand2+rand3)/3.0f)
*/

// ============================================================================
// Function:  NWorld::CArenaATB::CalcUnitTickPeriod
// VA:        0x00a2d6c0
// Subsystem: NWorld::ATB
// Signature: float __stdcall CalcUnitTickPeriod(float initiative)
// Summary:   Computes ATB timeline tick period for a creature stack with the given initiative using DefaultStats[+0x50] (fallback 0x186a0 = 100,000).
// ============================================================================
/*
   Decompiled Formula / Logic:
   float(DefaultStats->ATBBase > 0 ? DefaultStats->ATBBase : 100000) / (initiative > 0.0f ? initiative : 1.0f)
*/

// ============================================================================
// Function:  NWorld::CArenaATB::CalcHeroTickPeriod
// VA:        0x00a2d700
// Subsystem: NWorld::ATB
// Signature: float __cdecl CalcHeroTickPeriod()
// Summary:   Computes ATB timeline tick period for a Hero using DefaultStats[+0x48] (Hero Initiative = 10) and DefaultStats[+0x50] (100,000), also used as reference turn unit for Wait (0.5), Defend (1.0), and Good Morale (0.5).
// ============================================================================
/*
   Decompiled Formula / Logic:
   float(DefaultStats->ATBBase > 0 ? DefaultStats->ATBBase : 100000) / float(DefaultStats->HeroInitiative > 0 ? DefaultStats->HeroInitiative : 1)
*/

// ----------------------------------------------------------------------------
// Reconstructed Executable C++ Implementations
// ----------------------------------------------------------------------------

// VA: 0x00bcb3f0 (499 call sites across .text)
void* GetDefaultStats() {
    // mov eax, [0x01043a00] ; ret
    return *reinterpret_cast<void**>(0x01043a00);
}

// VA: 0x00419690 & 0x00bcc310 (30-bit Adventure Map tile coordinate packing)
int32_t PackTileCoord(int32_t x, int32_t y, int32_t floor) {
    return (x & 0x3FF) | ((y & 0x3FF) << 10) | ((floor & 0x3FF) << 20);
}

// VA: 0x004d8ba0 (?SFLB0_GetUnitExperienceValue@NWorld@@YIHABUSCreatureKillInfo@1@ABK@Z)
int32_t SFLB0_GetUnitExperienceValue(int32_t creaturePower, int32_t creatureExp, int32_t count, bool isSummoned) {
    if (isSummoned || count <= 0) return 0;
    const int32_t unitExp = (creatureExp > 0) ? creatureExp : creaturePower;
    return unitExp * count;
}

// VA: 0x00b4c6b0 (Hero Level 1..40 Experience Table initializer, seed at 0x00f755e4 -> 0x01026d90)
void InitHeroExperienceTable(int32_t outXpTable[40]) {
    static const int32_t kSeedLevels1To12[12] = {
        0, 1000, 2000, 3200, 4600, 6200, 8000, 10000, 12200, 14700, 17500, 20600
    };
    for (int i = 0; i < 12; ++i) outXpTable[i] = kSeedLevels1To12[i];
    // Levels 13..24: delta = int(prevDelta * 1.2f) (.rdata 0x00e4ff30)
    for (int i = 12; i < 24; ++i) {
        int32_t prevDelta = outXpTable[i - 1] - outXpTable[i - 2];
        outXpTable[i] = outXpTable[i - 1] + static_cast<int32_t>(prevDelta * 1.2f);
    }
    // Levels 25..40: delta = int(prevDelta * (level1Based - 13) * 0.1f) (.rdata 0x00e0ae5c)
    for (int level1Based = 25; level1Based <= 40; ++level1Based) {
        int idx = level1Based - 1;
        int32_t prevDelta = outXpTable[idx - 1] - outXpTable[idx - 2];
        float mult = (level1Based - 13) * 0.1f;
        outXpTable[idx] = outXpTable[idx - 1] + static_cast<int32_t>(prevDelta * mult);
    }
}

// VA: 0x00ca9a70 & 0x00cab090 (NWorld::STileGameInfo::CalcMoveCost & CHeroMovementCost, switch table 0x00ca9be8)
int32_t CalcHeroStepMoveCost(
    int32_t terrainType, int32_t roadType, int32_t heroNativeTerrain,
    bool isDiagonal, bool hasPathfinding, bool hasSnatch,
    int32_t penaltyReductionPercent = 0
) {
    int32_t baseCost = 100;
    float penalty = 0.0f;
    if (roadType == 6 || roadType == 7 || roadType == 8) {
        baseCost = 75; // 0x00ca9ad3
    } else if (terrainType != heroNativeTerrain) {
        switch (terrainType) {
            case 0: case 4: case 5: case 13: case 14:
                penalty = 25.0f; break; // 0x00ca9aed: mov eax, 0x19
            case 2: case 3:
                penalty = 50.0f; break; // 0x00ca9af7: mov eax, 0x32
            default:
                penalty = 0.0f; break;  // 0x00ca9be5
        }
        if (hasPathfinding) penalty *= 0.5f;
        if (hasSnatch) penalty = 0.0f;
        if (penaltyReductionPercent > 0)
            penalty *= 1.0f - static_cast<float>(penaltyReductionPercent) * 0.01f;
    }
    float total = static_cast<float>(baseCost) + penalty;
    if (isDiagonal) total *= 1.41421354f; // .rdata 0x00e557b8 (sqrt(2))
    return static_cast<int32_t>(total);   // cvttss2si at 0x00cab196
}

// VA: 0x00a58d30 & 0x00a58de0 (NWorld::CCombat Attack-vs-Defense physical damage multiplier)
float CalcAttackDefenseMultiplier(int32_t attack, int32_t defense) {
    if (attack > defense) {
        return 1.0f + 0.05f * static_cast<float>(attack - defense); // 0x00e0cd50
    }
    return 1.0f / (1.0f + 0.05f * static_cast<float>(defense - attack));
}

// VA: 0x00a2d6c0 & 0x00a2d700 (NWorld::CArenaATB unit & hero initiative period)
float CalcAtbUnitPeriod(float initiative, int32_t atbBaseTime = 100000) {
    const float effInit = (initiative > 0.0f) ? initiative : 1.0f;
    const float effBase = (atbBaseTime > 0) ? static_cast<float>(atbBaseTime) : 100000.0f;
    return effBase / effInit;
}

} // namespace NWorld
