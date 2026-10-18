"""HoMM5 ToTE 3.1 (H5_Game_PEST.exe) static binary analyzer and decompiler."""

from .pe_analyzer import (
    H5ExeAnalyzer,
    DecompiledExeDatabase,
    demangle_msvc_type_name,
    compute_hero_xp_table,
    calc_terrain_step_cost,
    calc_attack_defense_multiplier,
    calc_atb_unit_tick_period,
    calc_atb_hero_tick_period,
    calc_unit_experience_value,
)

__all__ = [
    "H5ExeAnalyzer",
    "DecompiledExeDatabase",
    "demangle_msvc_type_name",
    "compute_hero_xp_table",
    "calc_terrain_step_cost",
    "calc_attack_defense_multiplier",
    "calc_atb_unit_tick_period",
    "calc_atb_hero_tick_period",
    "calc_unit_experience_value",
]
