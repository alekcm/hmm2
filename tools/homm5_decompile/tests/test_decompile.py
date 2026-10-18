"""Unit tests for homm5_decompile against the real bin/H5_Game_PEST.exe binary."""

from __future__ import annotations

from pathlib import Path
import math
import pytest

from homm5decompile import (
    H5ExeAnalyzer,
    calc_atb_hero_tick_period,
    calc_atb_unit_tick_period,
    calc_attack_defense_multiplier,
    calc_terrain_step_cost,
    calc_unit_experience_value,
    compute_hero_xp_table,
)


REPO_ROOT = Path(__file__).resolve().parents[3]
EXE_PATH = REPO_ROOT / "bin" / "H5_Game_PEST.exe"


@pytest.fixture(scope="module")
def analyzer() -> H5ExeAnalyzer:
    assert EXE_PATH.is_file(), f"Missing {EXE_PATH}"
    return H5ExeAnalyzer(EXE_PATH)


@pytest.fixture(scope="module")
def decompiled_db(analyzer: H5ExeAnalyzer):
    return analyzer.build_database()


def test_pe_headers_and_pdb(decompiled_db):
    assert decompiled_db.exe_name == "H5_Game_PEST.exe"
    assert decompiled_db.exe_size == 13_089_792
    assert decompiled_db.image_base == 0x00400000
    assert decompiled_db.entry_point_va == 0x00D11E1C
    assert "H5_code_a2-p1" in decompiled_db.pdb_path
    assert len(decompiled_db.sections) == 6
    assert [s["name"] for s in decompiled_db.sections] == [".text", ".rdata", ".data", ".tls", ".rsrc", ".reloc"]


def test_pe_exports(decompiled_db):
    assert len(decompiled_db.exports) == 11
    names = " ".join(e["name"] for e in decompiled_db.exports)
    assert "SFLB0_GetUnitExperienceValue" in names
    assert "SFLB_CombineEditablesStep" in names
    assert "SFLB_InitDay" in names


def test_rtti_and_vtables(decompiled_db):
    summary = decompiled_db.rtti_summary
    assert summary["total_type_descriptors"] == 3347
    assert summary["total_vtables"] == 5577
    assert summary["total_vtable_slots"] > 50000
    assert summary["unique_virtual_methods"] == 19618
    assert summary["classes_by_namespace"]["NWorld"] > 650

    kc = decompiled_db.key_classes
    assert "NWorld::CHero" in kc
    assert "NWorld::CCombatCreature" in kc
    assert "NWorld::CArenaATB" in kc
    assert "NWorld::CHeroMovementCost" in kc
    assert "NWorld::CTown" in kc


def test_lua_c_api_extraction(decompiled_db):
    assert len(decompiled_db.lua_c_api) == 314
    by_name = {b["name"]: b["func_va"] for b in decompiled_db.lua_c_api}
    assert by_name["GetHeroStat"] == "0x005c33a0"
    assert by_name["ChangeHeroStat"] == "0x005c3620"
    assert by_name["CalcHeroMoveCost"] == "0x005ceaf0"
    assert by_name["IsTilePassable"] == "0x005e11a0"


def test_default_stats_call_xrefs(decompiled_db):
    total_calls = sum(decompiled_db.default_stats_call_summary.values())
    assert total_calls == 499
    assert decompiled_db.default_stats_call_summary["CCombat & Damage/Magic (0x00a58000..0x00a6dfff)"] > 50


def test_hero_xp_table_decompiled_formula(analyzer: H5ExeAnalyzer):
    seed = analyzer.extract_hero_xp_seed_table()
    assert seed == [
        0, 1000, 2000, 3200, 4600, 6200,
        8000, 10000, 12200, 14700, 17500, 20600,
    ]
    xp = compute_hero_xp_table(seed, max_level=40)
    assert len(xp) == 40
    # Level 13 = 20600 + int((20600 - 17500) * 1.2f) = 20600 + 3720 = 24320
    assert xp[12] == 24320
    # Strictly increasing across all 40 levels
    assert all(xp[i] > xp[i - 1] for i in range(1, 40))


def test_terrain_step_cost_decompiled_formula(analyzer: H5ExeAnalyzer):
    penalties = analyzer.extract_terrain_step_penalties()
    assert len(penalties) == 15
    assert penalties[1] == 0    # Grass
    assert penalties[2] == 50   # Sand (+50)
    assert penalties[3] == 50   # Snow (+50)
    assert penalties[4] == 25   # Lava/Subterranean (+25)

    # Road always costs 75 orthogonal, int(75 * sqrt(2)) = 106 diagonal
    assert calc_terrain_step_cost(terrain_type=2, road_type=6, hero_native_terrain=1, is_diagonal=False) == 75
    assert calc_terrain_step_cost(terrain_type=2, road_type=6, hero_native_terrain=1, is_diagonal=True) == 106

    # Native terrain costs 100 orthogonal, 141 diagonal
    assert calc_terrain_step_cost(terrain_type=2, road_type=0, hero_native_terrain=2, is_diagonal=False) == 100
    assert calc_terrain_step_cost(terrain_type=2, road_type=0, hero_native_terrain=2, is_diagonal=True) == 141

    # Non-native Sand (2) costs 150 normal, 125 with Pathfinding, 100 with Snatch
    assert calc_terrain_step_cost(terrain_type=2, road_type=0, hero_native_terrain=1) == 150
    assert calc_terrain_step_cost(terrain_type=2, road_type=0, hero_native_terrain=1, has_pathfinding=True) == 125
    assert calc_terrain_step_cost(terrain_type=2, road_type=0, hero_native_terrain=1, has_snatch=True) == 100


def test_combat_attack_defense_and_atb_formulas():
    # Attack > Defense -> 1 + 0.05 * (A - D)
    assert math.isclose(calc_attack_defense_multiplier(10, 4), 1.30, rel_tol=1e-6)
    # Attack == Defense -> 1.0
    assert math.isclose(calc_attack_defense_multiplier(5, 5), 1.00, rel_tol=1e-6)
    # Defense > Attack -> 1 / (1 + 0.05 * (D - A))
    assert math.isclose(calc_attack_defense_multiplier(5, 25), 0.50, rel_tol=1e-6)

    # ATB tick periods (0x00a2d6c0, 0x00a2d700)
    assert math.isclose(calc_atb_unit_tick_period(10.0), 10000.0, rel_tol=1e-6)
    assert math.isclose(calc_atb_unit_tick_period(0.0), 100000.0, rel_tol=1e-6)
    assert math.isclose(calc_atb_hero_tick_period(10), 10000.0, rel_tol=1e-6)

    # SFLB0_GetUnitExperienceValue (0x004d8ba0)
    assert calc_unit_experience_value(creature_power=15, creature_exp=22, count=10, is_summoned=False) == 220
    assert calc_unit_experience_value(creature_power=15, creature_exp=0, count=10, is_summoned=False) == 150
    assert calc_unit_experience_value(creature_power=15, creature_exp=22, count=10, is_summoned=True) == 0


def test_decompiled_src_cpp_tree():
    decompiled_src = REPO_ROOT / "decompiled_src"
    ndb_h = decompiled_src / "include" / "NDb" / "NDbTypes.h"
    nworld_h = decompiled_src / "include" / "RTTI" / "NWorld_Classes.h"
    nui_h = decompiled_src / "include" / "RTTI" / "NUI_Classes.h"
    ndb_rt_h = decompiled_src / "include" / "RTTI" / "NDb_Runtime_Classes.h"
    engine_h = decompiled_src / "include" / "RTTI" / "Engine_Subsystems_Classes.h"
    lua_cpp = decompiled_src / "src" / "LuaAPI" / "LuaCApiBindings.cpp"
    nworld_cpp = decompiled_src / "src" / "NWorld" / "NWorldDecompiled.cpp"
    granny_cpp = decompiled_src / "src" / "Granny2" / "Granny2Oodle1Decompress.cpp"

    for p in (ndb_h, nworld_h, nui_h, ndb_rt_h, engine_h, lua_cpp, nworld_cpp, granny_cpp):
        assert p.is_file(), f"Missing generated C++ file: {p}"

    ndb_text = ndb_h.read_text(encoding="utf-8")
    assert "enum AdvMapTileType : int32_t" in ndb_text
    assert "class Creature" in ndb_text
    assert "class AdvMapHero" in ndb_text
    assert "class RPGStats" in ndb_text

    nworld_text = nworld_h.read_text(encoding="utf-8")
    assert "/* NWorld::CHero */ CHero" in nworld_text
    assert "/* NWorld::CCombat */ CCombat" in nworld_text
    assert "/* NWorld::CArenaATB */ CArenaATB" in nworld_text
    assert "/* NWorld::CHeroMovementCost */ CHeroMovementCost" in nworld_text

    lua_text = lua_cpp.read_text(encoding="utf-8")
    assert "Lua_GetHeroStat" in lua_text
    assert "Lua_CalcHeroMoveCost" in lua_text

