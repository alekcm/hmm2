"""Static PE32 binary analyzer & decompiler for HoMM5 ToTE 3.1 (H5_Game_PEST.exe).

Extracts directly from the compiled 32-bit Windows PE executable:
  1. PE32 section layout, CodeView PDB signature, and exported SFLB* functions.
  2. All 2,718 MSVC 7.1 RTTI TypeDescriptors, 4,871 CompleteObjectLocators &
     vftables, 50,925 vtable slots, 18,333 unique virtual methods, and base
     class hierarchy descriptors.
  3. All 316 Lua C-API (name, fn_va) function bindings from .data.
  4. All 499 call sites to GetDefaultStats() (0x00bcb3f0) across the engine.
  5. Binary tables and decompiled NWorld gameplay formulas:
     - Hero Level 1..40 XP table (seed array at 0x00f755e0 + 0x00b4c6b0 formula)
     - Terrain movement cost table (0x00ca9a70 + jump table at 0x00ca9be8)
     - Hero step cost & diagonal/skill modifiers (0x00cab090)
     - Tile coordinate packing (0x00419690), CTileGameInfoMap::GetTile (0x00bcc310),
       and IsTilePassable (0x005e11a0)
     - Hero primary & secondary attributes (0x00b5ad70, 0x00b4ca10..0x00b4ce00)
     - Creature experience value SFLB0_GetUnitExperienceValue (0x004d8ba0)
     - Combat Attack-vs-Defense multiplier (0x00a58d30)
     - Combat stack base damage roll & preview modes (0x00a597a0)
     - Combat ATB initiative timeline period (0x00a2d6c0, 0x00a2d700)
"""

from __future__ import annotations

from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Dict, List, Optional, Tuple
import math
import re
import struct


IMAGE_BASE = 0x00400000
TYPE_INFO_VFTABLE_VA = 0x00E98370
GET_DEFAULT_STATS_VA = 0x00BCB3F0
HERO_XP_SEED_VA = 0x00F755E0
TERRAIN_MOVE_JUMPTABLE_VA = 0x00CA9BE8


@dataclass
class PESection:
    name: str
    virtual_address: int
    virtual_size: int
    raw_offset: int
    raw_size: int

    @property
    def start_va(self) -> int:
        return IMAGE_BASE + self.virtual_address

    @property
    def end_va(self) -> int:
        return self.start_va + max(self.virtual_size, self.raw_size)


@dataclass
class VTableInfo:
    vftable_va: int
    col_va: int
    vtable_offset: int
    cd_offset: int
    method_vas: List[int]
    base_classes: List[str] = field(default_factory=list)


@dataclass
class RTTIClassInfo:
    mangled_name: str
    demangled_name: str
    namespace: str
    short_name: str
    type_descriptor_va: int
    vtables: List[VTableInfo] = field(default_factory=list)


@dataclass
class LuaBindingInfo:
    name: str
    func_va: int
    table_va: int


@dataclass
class DecompiledFunctionSpec:
    name: str
    va: int
    signature: str
    subsystem: str
    summary: str
    formula: str


@dataclass
class DecompiledExeDatabase:
    exe_name: str
    exe_size: int
    image_base: int
    entry_point_va: int
    pdb_path: str
    sections: List[Dict[str, object]]
    exports: List[Dict[str, object]]
    rtti_summary: Dict[str, object]
    key_classes: Dict[str, Dict[str, object]]
    lua_c_api: List[Dict[str, object]]
    default_stats_call_summary: Dict[str, int]
    hero_xp_table: List[int]
    terrain_step_penalties: Dict[int, int]
    fpu_constants: Dict[str, float]
    decompiled_functions: List[Dict[str, object]]

    def to_dict(self) -> Dict[str, object]:
        return asdict(self)


def demangle_msvc_type_name(mangled: str) -> Tuple[str, str, str]:
    """Demangle an MSVC RTTI TypeDescriptor name (`.?AVFoo@Bar@@` -> (`Bar::Foo`, `Bar`, `Foo`))."""
    s = mangled
    if s.startswith(".?AV") or s.startswith(".?AU"):
        s = s[4:]
    if s.endswith("@@"):
        s = s[:-2]
    # Split Simple @-separated components when not template-heavy
    if "<" not in s and "?" not in s:
        parts = [p for p in s.split("@") if p]
        parts.reverse()
        demangled = "::".join(parts)
        namespace = parts[0] if len(parts) > 1 else "<global>"
        short_name = parts[-1] if parts else mangled
        return demangled, namespace, short_name

    # Template / nested name: extract outer namespace (last @Token before @@) and class name
    tokens = [p for p in re.split(r"@+", s) if p]
    namespace = "<global>"
    for tok in reversed(tokens):
        if tok in ("NWorld", "NUI", "NDb", "NGScene", "NUbi", "NNet", "NSound", "NGame", "std", "boost"):
            namespace = tok
            break
    short_name = tokens[0].lstrip("?$") if tokens else s
    return s, namespace, short_name


def compute_hero_xp_table(seed_levels_1_to_12: Optional[List[int]] = None, max_level: int = 40) -> List[int]:
    """Exact decompiled Hero Level XP progression from 0x00b4c6b0 (bin/H5_Game_PEST.exe).

    In 0x00b4c6b0:
      - Indices 0..11 (Levels 1..12) are copied from 0x00f755e0:
        [0, 1000, 2000, 3200, 4600, 6200, 8000, 10000, 12200, 14700, 17500, 20600]
      - Indices 12..23 (Levels 13..24) add int((xp[i-1] - xp[i-2]) * 1.2f)
        (multiplying by float 0x00e12f1c = 1.20000005f with x87 truncation).
      - Indices 24..39 (Levels 25..40) scale the previous delta by ((i - 12) * 0.1f)
        where i is 1-based index 25..40 -> multiplier (25-13)*0.1 = 1.2, 1.3, ...
    """
    if seed_levels_1_to_12 is None:
        seed_levels_1_to_12 = [
            0, 1000, 2000, 3200, 4600, 6200,
            8000, 10000, 12200, 14700, 17500, 20600,
        ]
    xp = list(seed_levels_1_to_12[:12])
    f32_1_2 = struct.unpack("<f", struct.pack("<f", 1.2))[0]
    f32_0_1 = struct.unpack("<f", struct.pack("<f", 0.1))[0]

    # Levels 13..24 (0-based indices 12..23)
    for idx in range(12, min(24, max_level)):
        prev_delta = xp[idx - 1] - xp[idx - 2]
        next_delta = int(prev_delta * f32_1_2)
        xp.append(xp[idx - 1] + next_delta)

    # Levels 25..max_level (0-based indices 24..max_level-1; 1-based i = idx + 1)
    for idx in range(24, max_level):
        i_one_based = idx + 1
        prev_delta = xp[idx - 1] - xp[idx - 2]
        mult = (i_one_based - 13) * f32_0_1
        next_delta = int(prev_delta * mult)
        xp.append(xp[idx - 1] + next_delta)

    return xp


def calc_terrain_step_cost(
    terrain_type: int,
    road_type: int,
    hero_native_terrain: int,
    is_diagonal: bool = False,
    has_pathfinding: bool = False,
    has_snatch: bool = False,
    penalty_modifier_pct: int = 0,
) -> int:
    """Exact decompiled hero tile movement cost from 0x00cab090 & 0x00ca9a70.

    - Base step cost = 100 (0x64).
    - Road step cost (road_type in {6, 7, 8}) = 75 (0x4b).
    - Off-road on non-native terrain adds penalty from jump table 0x00ca9be8:
        * terrain in {1, 6, 7, 8, 9, 10, 11, 12}: +0 (total 100)
        * terrain in {0, 4, 5, 13, 14}: +25 (total 125)
        * terrain in {2, 3} (Sand, Snow): +50 (total 150)
    - Snatch zeroes the penalty; Pathfinding multiplies penalty by 0.5f;
      penalty_modifier_pct multiplies penalty by (1.0f - penalty_modifier_pct * 0.01f).
    - Diagonal movement multiplies total step cost by sqrt(2) (0x00e557b8 = 1.41421354f)
      and truncates to int via fistp with CW rounding mode 0x0c (truncate).
    """
    if road_type in (6, 7, 8):
        cost = 75
    else:
        base = 100
        if terrain_type == hero_native_terrain:
            penalty = 0
        elif terrain_type in (2, 3):
            penalty = 50
        elif terrain_type in (0, 4, 5, 13, 14):
            penalty = 25
        else:
            penalty = 0

        if penalty > 0:
            if has_snatch:
                penalty = 0
            elif has_pathfinding:
                penalty = int(penalty * 0.5)
            if penalty_modifier_pct != 0:
                penalty = int(penalty * (1.0 - penalty_modifier_pct * 0.01))
        cost = base + penalty

    if is_diagonal:
        sqrt2_f32 = struct.unpack("<f", struct.pack("<I", 0x3FB504F3))[0]
        cost = int(float(cost) * sqrt2_f32)
    return cost


def calc_attack_defense_multiplier(
    attack: float,
    defense: float,
    attack_coeff: float = 0.05,
    defense_coeff: float = 0.05,
) -> float:
    """Exact decompiled combat Attack-vs-Defense multiplier from 0x00a58d30.

    - diff = Attack - Defense
    - If diff > 0:  return 1.0 + attack_coeff * diff
    - If diff <= 0: return 1.0 / (1.0 - defense_coeff * diff)
                         = 1.0 / (1.0 + defense_coeff * (Defense - Attack))
    """
    diff = float(attack) - float(defense)
    if diff > 0.0:
        return 1.0 + attack_coeff * diff
    coeff = max(0.0, defense_coeff)
    return 1.0 / (1.0 - coeff * diff)


def calc_atb_unit_tick_period(initiative: float, atb_base: int = 100000) -> float:
    """Exact decompiled CArenaATB unit tick period from 0x00a2d6c0.

    If initiative <= 0.0f, clamps initiative to 1.0f.
    If atb_base <= 0, uses fallback 0x186a0 (100,000).
    Returns float(atb_base) / initiative.
    """
    eff_init = float(initiative) if initiative > 0.0 else 1.0
    eff_base = atb_base if atb_base > 0 else 100000
    return float(eff_base) / eff_init


def calc_atb_hero_tick_period(hero_initiative: int = 10, atb_base: int = 100000) -> float:
    """Exact decompiled CArenaATB hero tick period from 0x00a2d700."""
    eff_init = float(hero_initiative) if hero_initiative > 0 else 1.0
    eff_base = atb_base if atb_base > 0 else 100000
    return float(eff_base) / eff_init


def calc_unit_experience_value(creature_power: int, creature_exp: int, count: int, is_summoned: bool = False) -> int:
    """Exact decompiled SFLB0_GetUnitExperienceValue from 0x004d8ba0.

    Returns 0 if summoned/gated (info[+8] != 0), else:
      (creature.Exp > 0 ? creature.Exp : creature.Power) * count
    """
    if is_summoned or count <= 0:
        return 0
    per_unit = creature_exp if creature_exp > 0 else creature_power
    return per_unit * count


class H5ExeAnalyzer:
    """Binary parser and static decompiler for bin/H5_Game_PEST.exe."""

    def __init__(self, exe_path: Path):
        self.exe_path = Path(exe_path)
        self.data = self.exe_path.read_bytes()
        self.sections: List[PESection] = []
        self.entry_point_va: int = 0
        self._parse_pe_headers()

    def _parse_pe_headers(self) -> None:
        if self.data[:2] != b"MZ":
            raise ValueError(f"Not a valid MZ/PE executable: {self.exe_path}")
        pe_off = struct.unpack_from("<I", self.data, 0x3C)[0]
        if self.data[pe_off:pe_off + 4] != b"PE\x00\x00":
            raise ValueError("Invalid PE signature")
        num_sections = struct.unpack_from("<H", self.data, pe_off + 6)[0]
        opt_hdr_size = struct.unpack_from("<H", self.data, pe_off + 20)[0]
        opt_off = pe_off + 24
        entry_rva = struct.unpack_from("<I", self.data, opt_off + 16)[0]
        self.entry_point_va = IMAGE_BASE + entry_rva

        sec_off = opt_off + opt_hdr_size
        for i in range(num_sections):
            s_off = sec_off + i * 40
            raw_name = self.data[s_off:s_off + 8].split(b"\x00", 1)[0].decode("ascii", errors="ignore")
            vsize, vaddr, rsize, roff = struct.unpack_from("<4I", self.data, s_off + 8)
            self.sections.append(PESection(raw_name, vaddr, vsize, roff, rsize))

    def va_to_offset(self, va: int) -> Optional[int]:
        rva = va - IMAGE_BASE
        for s in self.sections:
            if s.virtual_address <= rva < s.virtual_address + s.raw_size:
                return s.raw_offset + (rva - s.virtual_address)
        return None

    def offset_to_va(self, offset: int) -> Optional[int]:
        for s in self.sections:
            if s.raw_offset <= offset < s.raw_offset + s.raw_size:
                return IMAGE_BASE + s.virtual_address + (offset - s.raw_offset)
        return None

    def read_u32(self, va: int) -> int:
        off = self.va_to_offset(va)
        if off is None:
            raise ValueError(f"VA 0x{va:08x} is not mapped to file bytes")
        return struct.unpack_from("<I", self.data, off)[0]

    def read_f32(self, va: int) -> float:
        off = self.va_to_offset(va)
        if off is None:
            raise ValueError(f"VA 0x{va:08x} is not mapped to file bytes")
        return struct.unpack_from("<f", self.data, off)[0]

    def read_cstring(self, va: int, max_len: int = 256) -> str:
        off = self.va_to_offset(va)
        if off is None:
            return ""
        end = self.data.find(b"\x00", off, off + max_len)
        if end == -1:
            end = off + max_len
        return self.data[off:end].decode("ascii", errors="ignore")

    def extract_pdb_path(self) -> str:
        idx = self.data.find(b"RSDS")
        if idx == -1:
            return ""
        end = self.data.find(b"\x00", idx + 24, idx + 280)
        if end == -1:
            return ""
        return self.data[idx + 24:end].decode("ascii", errors="ignore")

    def extract_exports(self) -> List[Dict[str, object]]:
        """Extract all PE exported symbols (SFLB* functions)."""
        pe_off = struct.unpack_from("<I", self.data, 0x3C)[0]
        opt_off = pe_off + 24
        exp_rva, exp_sz = struct.unpack_from("<2I", self.data, opt_off + 96)
        if exp_rva == 0 or exp_sz == 0:
            return []
        exp_off = self.va_to_offset(IMAGE_BASE + exp_rva)
        if exp_off is None:
            return []
        num_funcs, num_names, funcs_rva, names_rva, ords_rva = struct.unpack_from(
            "<5I", self.data, exp_off + 20
        )
        exports: List[Dict[str, object]] = []
        for i in range(num_names):
            name_rva = self.read_u32(IMAGE_BASE + names_rva + i * 4)
            ord_idx = struct.unpack_from("<H", self.data, self.va_to_offset(IMAGE_BASE + ords_rva + i * 2))[0]
            fn_rva = self.read_u32(IMAGE_BASE + funcs_rva + ord_idx * 4)
            exports.append({
                "name": self.read_cstring(IMAGE_BASE + name_rva),
                "ordinal": int(ord_idx + 1),
                "va": f"0x{IMAGE_BASE + fn_rva:08x}",
            })
        return exports

    def extract_rtti(self) -> Tuple[Dict[int, RTTIClassInfo], int, int]:
        """Extract all MSVC 7.1 RTTI TypeDescriptors, CompleteObjectLocators, and vftables."""
        text_sec = self.sections[0]
        rdata_sec = self.sections[1]
        data_sec = self.sections[2]

        text_start, text_end = text_sec.start_va, text_sec.end_va
        rdata_start, rdata_end = rdata_sec.start_va, rdata_sec.end_va

        # 1. Scan .data for TypeDescriptors (vftable = 0x00e0ce2c)
        td_pack = struct.pack("<I", TYPE_INFO_VFTABLE_VA)
        classes_by_td: Dict[int, RTTIClassInfo] = {}
        pos = data_sec.raw_offset
        end_pos = data_sec.raw_offset + data_sec.raw_size
        while True:
            idx = self.data.find(td_pack, pos, end_pos)
            if idx == -1:
                break
            spare = struct.unpack_from("<I", self.data, idx + 4)[0]
            if spare == 0 and self.data[idx + 8:idx + 11] == b".?A":
                s_end = self.data.find(b"\x00", idx + 8, idx + 520)
                if s_end != -1:
                    mangled = self.data[idx + 8:s_end].decode("ascii", errors="ignore")
                    td_va = self.offset_to_va(idx)
                    if td_va is not None:
                        demangled, ns, short = demangle_msvc_type_name(mangled)
                        classes_by_td[td_va] = RTTIClassInfo(
                            mangled_name=mangled,
                            demangled_name=demangled,
                            namespace=ns,
                            short_name=short,
                            type_descriptor_va=td_va,
                        )
            pos = idx + 1

        # 2. Scan .rdata for CompleteObjectLocators
        cols_by_va: Dict[int, Tuple[int, int, int, int]] = {}
        r_start = rdata_sec.raw_offset
        r_end = rdata_sec.raw_offset + rdata_sec.raw_size
        for off in range(r_start, r_end - 20, 4):
            sig, voff, cd_off, td_va, chd_va = struct.unpack_from("<5I", self.data, off)
            if sig == 0 and td_va in classes_by_td and (rdata_start <= chd_va < rdata_end):
                col_va = self.offset_to_va(off)
                if col_va is not None:
                    cols_by_va[col_va] = (voff, cd_off, td_va, chd_va)

        # 3. Scan .rdata for vftable meta-pointers (`vftable[-1] == col_va`)
        total_slots = 0
        unique_methods = set()
        for off in range(r_start, r_end - 8, 4):
            ptr = struct.unpack_from("<I", self.data, off)[0]
            if ptr in cols_by_va:
                first_fn = struct.unpack_from("<I", self.data, off + 4)[0]
                if text_start <= first_fn < text_end:
                    vftable_va = self.offset_to_va(off + 4)
                    if vftable_va is None:
                        continue
                    voff, cd_off, td_va, chd_va = cols_by_va[ptr]
                    methods: List[int] = []
                    cur = off + 4
                    while cur < r_end:
                        fn_va = struct.unpack_from("<I", self.data, cur)[0]
                        if text_start <= fn_va < text_end:
                            methods.append(fn_va)
                            unique_methods.add(fn_va)
                            cur += 4
                        else:
                            break
                    total_slots += len(methods)
                    base_classes = self._parse_chd_bases(chd_va, classes_by_td, rdata_start, rdata_end)
                    classes_by_td[td_va].vtables.append(
                        VTableInfo(
                            vftable_va=vftable_va,
                            col_va=ptr,
                            vtable_offset=voff,
                            cd_offset=cd_off,
                            method_vas=methods,
                            base_classes=base_classes,
                        )
                    )

        return classes_by_td, total_slots, len(unique_methods)

    def _parse_chd_bases(
        self,
        chd_va: int,
        classes_by_td: Dict[int, RTTIClassInfo],
        rdata_start: int,
        rdata_end: int,
    ) -> List[str]:
        off = self.va_to_offset(chd_va)
        if off is None:
            return []
        sig, attr, num_bases, bca_va = struct.unpack_from("<4I", self.data, off)
        if sig != 0 or num_bases == 0 or num_bases > 64 or not (rdata_start <= bca_va < rdata_end):
            return []
        bca_off = self.va_to_offset(bca_va)
        if bca_off is None:
            return []
        bases: List[str] = []
        for i in range(num_bases):
            bcd_va = struct.unpack_from("<I", self.data, bca_off + i * 4)[0]
            bcd_off = self.va_to_offset(bcd_va)
            if bcd_off is None:
                continue
            b_td_va = struct.unpack_from("<I", self.data, bcd_off)[0]
            if b_td_va in classes_by_td:
                bases.append(classes_by_td[b_td_va].demangled_name)
        return bases

    def extract_lua_c_api(self) -> List[LuaBindingInfo]:
        """Extract all 316 Lua C-API (name, fn_va) pairs from .data."""
        text_sec = self.sections[0]
        rdata_sec = self.sections[1]
        data_sec = self.sections[2]

        text_start, text_end = text_sec.start_va, text_sec.end_va
        rdata_start, rdata_end = rdata_sec.start_va, rdata_sec.end_va

        seen = set()
        bindings: List[LuaBindingInfo] = []
        d_start = data_sec.raw_offset
        d_end = data_sec.raw_offset + data_sec.raw_size
        for off in range(d_start, d_end - 8, 4):
            p1, p2 = struct.unpack_from("<2I", self.data, off)
            if rdata_start <= p1 < rdata_end and text_start <= p2 < text_end:
                name = self.read_cstring(p1, 80)
                if 2 <= len(name) <= 64 and name[0].isalpha() and name.replace("_", "").isalnum():
                    key = (name, p2)
                    if key not in seen:
                        seen.add(key)
                        table_va = self.offset_to_va(off) or 0
                        bindings.append(LuaBindingInfo(name=name, func_va=p2, table_va=table_va))
        return bindings

    def extract_callers_of(self, target_va: int) -> List[int]:
        """Find all relative E8 call instructions in .text targeting `target_va`."""
        text_sec = self.sections[0]
        callers: List[int] = []
        t_start = text_sec.raw_offset
        t_end = t_start + text_sec.raw_size - 5
        for off in range(t_start, t_end):
            if self.data[off] == 0xE8:
                rel = struct.unpack_from("<i", self.data, off + 1)[0]
                va = text_sec.start_va + (off - t_start)
                if va + 5 + rel == target_va:
                    callers.append(va)
        return callers

    def extract_hero_xp_seed_table(self) -> List[int]:
        """Read the 12-entry Hero Level 1..12 XP seed table from 0x00f755e4 ([1..12] of 0x00f755e0)."""
        off = self.va_to_offset(HERO_XP_SEED_VA + 4)
        if off is None:
            return []
        return list(struct.unpack_from("<12i", self.data, off))

    def extract_terrain_step_penalties(self) -> Dict[int, int]:
        """Decode the 15-entry terrain step cost jump table at 0x00ca9be8 (in 0x00ca9a70)."""
        off = self.va_to_offset(TERRAIN_MOVE_JUMPTABLE_VA)
        if off is None:
            return {}
        targets = struct.unpack_from("<15I", self.data, off)
        penalties: Dict[int, int] = {}
        for terrain_id, target_va in enumerate(targets):
            # In 0x00ca9a70:
            #   0x00ca9aed -> mov [esp+0x1c], 0x19 (25 penalty -> 125 total)
            #   0x00ca9af7 -> mov [esp+0x1c], 0x32 (50 penalty -> 150 total)
            #   0x00ca9b01 / 0x00ca9b05 -> 0 penalty (100 total)
            if target_va == 0x00CA9AF7:
                penalties[terrain_id] = 50
            elif target_va == 0x00CA9AED:
                penalties[terrain_id] = 25
            else:
                penalties[terrain_id] = 0
        return penalties

    def build_database(self) -> DecompiledExeDatabase:
        classes_by_td, total_slots, unique_methods = self.extract_rtti()
        lua_bindings = self.extract_lua_c_api()
        default_stats_callers = self.extract_callers_of(GET_DEFAULT_STATS_VA)

        # Group RTTI classes by namespace
        ns_counts: Dict[str, int] = {}
        for c in classes_by_td.values():
            ns_counts[c.namespace] = ns_counts.get(c.namespace, 0) + 1

        # Extract key NWorld / NDb / NGScene classes with vtable metadata
        key_class_names = {
            "NWorld::CHero",
            "NWorld::CAdvMapHero",
            "NWorld::CHeroMovementCost",
            "NWorld::CTetraMovementCost",
            "NWorld::CTown",
            "NWorld::CAdvMapTown",
            "NWorld::CPlayer",
            "NWorld::CWorld",
            "NWorld::CTerrain",
            "NWorld::CCombat",
            "NWorld::CCombatCreature",
            "NWorld::CArenaATB",
            "NWorld::CTileGameInfoMap",
            "NDb::AdvMapDesc",
            "NDb::AdvMapHero",
            "NDb::AdvMapTown",
            "NDb::AdvMapMonster",
            "NDb::Creature",
            "NDb::Hero",
            "NDb::RPGStats",
            "NDb::TownBuildingShared",
        }
        key_classes: Dict[str, Dict[str, object]] = {}
        for c in classes_by_td.values():
            if c.demangled_name in key_class_names or c.short_name in {
                "CHero", "CAdvMapHero", "CHeroMovementCost", "CTetraMovementCost",
                "CTown", "CAdvMapTown", "CPlayer", "CWorld", "CTerrain",
                "CCombat", "CCombatCreature", "CArenaATB", "CTileGameInfoMap",
            }:
                if c.namespace not in ("NWorld", "NDb"):
                    continue
                key_classes[c.demangled_name] = {
                    "mangled_name": c.mangled_name,
                    "type_descriptor_va": f"0x{c.type_descriptor_va:08x}",
                    "vtables": [
                        {
                            "vftable_va": f"0x{vt.vftable_va:08x}",
                            "vtable_offset": vt.vtable_offset,
                            "method_count": len(vt.method_vas),
                            "first_methods": [f"0x{m:08x}" for m in vt.method_vas[:16]],
                            "base_classes": vt.base_classes[:10],
                        }
                        for vt in sorted(c.vtables, key=lambda v: v.vtable_offset)
                    ],
                }

        # Group GetDefaultStats() call sites by engine subsystem
        subsystem_calls = {
            "CCombatCreature (0x008a0000..0x008b1fff)": 0,
            "CArenaATB (0x00a2d000..0x00a34fff)": 0,
            "CCombat & Damage/Magic (0x00a58000..0x00a6dfff)": 0,
            "CHero & Progression (0x00b4b000..0x00b57fff)": 0,
            "CPlayerAI & CombatAI (0x00976000..0x009befff)": 0,
            "CTown & Buildings (0x00cce000..0x00ce6fff)": 0,
            "Other Subsystems": 0,
        }
        for va in default_stats_callers:
            if 0x008A0000 <= va <= 0x008B1FFF:
                subsystem_calls["CCombatCreature (0x008a0000..0x008b1fff)"] += 1
            elif 0x00A2D000 <= va <= 0x00A34FFF:
                subsystem_calls["CArenaATB (0x00a2d000..0x00a34fff)"] += 1
            elif 0x00A58000 <= va <= 0x00A6DFFF:
                subsystem_calls["CCombat & Damage/Magic (0x00a58000..0x00a6dfff)"] += 1
            elif 0x00B4B000 <= va <= 0x00B57FFF:
                subsystem_calls["CHero & Progression (0x00b4b000..0x00b57fff)"] += 1
            elif 0x00976000 <= va <= 0x009BEFFF:
                subsystem_calls["CPlayerAI & CombatAI (0x00976000..0x009befff)"] += 1
            elif 0x00CCE000 <= va <= 0x00CE6FFF:
                subsystem_calls["CTown & Buildings (0x00cce000..0x00ce6fff)"] += 1
            else:
                subsystem_calls["Other Subsystems"] += 1

        seed_xp = self.extract_hero_xp_seed_table()
        full_xp = compute_hero_xp_table(seed_xp, max_level=40)
        terrain_penalties = self.extract_terrain_step_penalties()

        fpu_constants = {
            "0x00e0a7e4 (1.0f)": round(self.read_f32(0x00E0A7E4), 7),
            "0x00e0ab70 (0.5f - Pathfinding / Ranged / AvgDmg)": round(self.read_f32(0x00E0AB70), 7),
            "0x00e12f10 (0.1f - Hero XP lvl 25+)": round(self.read_f32(0x00E12F10), 7),
            "0x00e12f1c (1.2f - Hero XP lvl 13..24)": round(self.read_f32(0x00E12F1C), 7),
            "0x00e15a80 (0.01f - Percent scale)": round(self.read_f32(0x00E15A80), 7),
            "0x00e19950 (0.33333334f - 3-roll damage avg)": round(self.read_f32(0x00E19950), 7),
            "0x00e557b8 (1.41421354f - Diagonal sqrt(2))": round(self.read_f32(0x00E557B8), 7),
        }

        decompiled_functions = [
            DecompiledFunctionSpec(
                name="NDb::GetDefaultStats",
                va=0x00BCB3F0,
                signature="const NDb::RPGStats* __cdecl GetDefaultStats()",
                subsystem="NDb",
                summary="Returns singleton pointer to DefaultStats.xdb (ds:0x01043a00); called 499 times across combat, hero, AI, and town subsystems.",
                formula="return ds_01043a00 ? ds_01043a00 : LoadDefaultStats(ds_010438c0);",
            ),
            DecompiledFunctionSpec(
                name="NDb::GetCreatureDescById",
                va=0x0089FBF0,
                signature="const NDb::Creature* __fastcall GetCreatureDescById(int creatureId)",
                subsystem="NDb",
                summary="Bounds-checks creature ID against 0xb4 (180) and looks up the creature record in Table_Creature_CreatureId (ds:0x01024c04).",
                formula="return (creatureId >= 0 && creatureId < 180) ? Table_Creature[creatureId] : Table_Creature[0];",
            ),
            DecompiledFunctionSpec(
                name="SFLB0_GetUnitExperienceValue",
                va=0x004D8BA0,
                signature="int __stdcall SFLB0_GetUnitExperienceValue(const SUnitExpInfo* info)",
                subsystem="NWorld::Combat",
                summary="Computes XP awarded for defeating a creature stack. Returns 0 if gated/summoned (info[+8] != 0), otherwise multiplies creature Exp (+0x80) or Power (+0x64) by stack count (info[+4]).",
                formula="info->isSummoned ? 0 : (creature->Exp > 0 ? creature->Exp : creature->Power) * info->count",
            ),
            DecompiledFunctionSpec(
                name="NWorld::PackTileCoord",
                va=0x00419690,
                signature="uint32_t __fastcall PackTileCoord(int x, int y, int floor)",
                subsystem="NWorld::Map",
                summary="Packs (x, y, floor) into a 30-bit integer key used by CTileGameInfoMap and Lua C-API.",
                formula="(x & 0x3FF) | ((y & 0x3FF) << 10) | ((floor & 0x3FF) << 20)",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CTileGameInfoMap::GetTile",
                va=0x00BCC310,
                signature="const STileGameInfo* __thiscall GetTile(const CTileGameInfoMap* this, uint32_t packedCoord)",
                subsystem="NWorld::Map",
                summary="Indexes floor array ([this+0x20] + floor*88 + 4), row pointer y, and column x*36 (sizeof(STileGameInfo) == 36).",
                formula="floorLayers[floor].rows[y][x] // sizeof(STileGameInfo) == 36 bytes",
            ),
            DecompiledFunctionSpec(
                name="NWorld::IsTilePassable",
                va=0x005E11A0,
                signature="bool __cdecl IsTilePassable(int x, int y, int floor)",
                subsystem="NWorld::Map",
                summary="Checks whether a map tile is passable for standard ground/underground movement.",
                formula="(tile->passabilityFlag == 1 || tile->passabilityFlag == 4) && tile->walkableState == 1",
            ),
            DecompiledFunctionSpec(
                name="NWorld::STileGameInfo::CalcMoveCost",
                va=0x00CA9A70,
                signature="int __thiscall CalcMoveCost(const STileGameInfo* this, const SHeroMoveModifiers* mods)",
                subsystem="NWorld::Pathfinding",
                summary="Computes orthogonal step cost on a tile: 75 on road (roadType in {6,7,8}), 100 on native terrain, or 100 + penalty (+25 or +50 from jump table 0x00ca9be8) on non-native terrain, modified by Pathfinding (*0.5f), Snatch (0), and percent modifiers.",
                formula="road in {6,7,8} ? 75 : 100 + ModifyPenalty(terrain == native ? 0 : JumpTable_00ca9be8[terrain])",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CHeroMovementCost::GetStepCost",
                va=0x00CAB090,
                signature="int __thiscall GetStepCost(const CHeroMovementCost* this, const STileCoord* from, const STileCoord* to, int curMovePoints, bool* outStop)",
                subsystem="NWorld::Pathfinding",
                summary="Computes full hero step cost between adjacent tiles, multiplying by sqrt(2) (1.41421354f at 0x00e557b8) when dx != 0 && dy != 0.",
                formula="int(STileGameInfo::CalcMoveCost(toTile, &this->mods) * (dx != 0 && dy != 0 ? 1.41421354f : 1.0f))",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CTetraMovementCost::GetStepCost",
                va=0x00876850,
                signature="int __thiscall GetStepCost(const CTetraMovementCost* this, const STileCoord* from, const STileCoord* to, int curMovePoints, bool* outStop)",
                subsystem="NWorld::Pathfinding",
                summary="Computes step cost for 2x2 / multi-tile walkers by taking the maximum CalcMoveCost across all footprint tiles.",
                formula="int(max_{cell in footprint}(CalcMoveCost(cell)) * (isDiagonal ? 1.41421354f : 1.0f))",
            ),
            DecompiledFunctionSpec(
                name="NWorld::InitHeroLevelExperienceTable",
                va=0x00B4C6B0,
                signature="void __cdecl InitHeroLevelExperienceTable()",
                subsystem="NWorld::Hero",
                summary="Populates the global Hero Level XP table at 0x01026d90 from seed table 0x00f755e0 (Levels 1..12), *1.2f delta for Levels 13..24, and *(i-13)*0.1f delta for Levels 25..40.",
                formula="i<=12: seed[i-1]; 13<=i<=24: xp[i-2] + int((xp[i-2]-xp[i-3])*1.2f); i>=25: xp[i-2] + int((xp[i-2]-xp[i-3])*(i-13)*0.1f)",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CHero::GetStat",
                va=0x00B5AD70,
                signature="int __thiscall GetStat(const CHero* this, int statId)",
                subsystem="NWorld::Hero",
                summary="Internal CHero stat accessor dispatch accepts statId 1..10 via the 10-slot table at [this+0x264]. This is distinct from the Lua public STAT_* constants: Lua ChangeHeroStat accepts 0..8 and Lua GetHeroStat's wrapper admits an additional unexposed slot 9.",
                formula="(statId >= 1 && statId <= 10) ? this->statAccessors[statId - 1](this) : 0",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CHero::GetPrimaryAndSecondaryStats",
                va=0x00B4CA10,
                signature="int __thiscall GetAttack/GetDefense/GetSpellPower/GetKnowledge/GetMorale/GetLuck(const CHero* this)",
                subsystem="NWorld::Hero",
                summary="Computes Hero Attack (0x00b4cc50 >=0), Defense (0x00b4ccc0 >=0), SpellPower (0x00b4ccf0 >=1), Knowledge (0x00b4cd80 >=1), ClampedMorale (0x00b4cb40 [-5..+5]), and ClampedLuck (0x00b4cb60 [-5..+5]).",
                formula="Attack=max(0, base+bonus); Defense=max(0, base+bonus); SpellPower=max(1, base+bonus); Knowledge=max(1, base+bonus); Morale=clamp(raw,-5,5); Luck=clamp(raw,-5,5)",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CalcAttackDefenseMultiplier",
                va=0x00A58D30,
                signature="float __stdcall CalcAttackDefenseMultiplier(float attack, float defense)",
                subsystem="NWorld::Combat",
                summary="Computes HoMM5 physical damage Attack-vs-Defense multiplier using DefaultStats[+0x1d0] (0.05f) when Attack > Defense and reciprocal 1 / (1 + DefaultStats[+0x1d8] * (Defense - Attack)) when Defense >= Attack.",
                formula="(Attack > Defense) ? (1.0f + 0.05f * (Attack - Defense)) : (1.0f / (1.0f + 0.05f * (Defense - Attack)))",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CalcBaseStackDamage",
                va=0x00A597A0,
                signature="float __fastcall CalcBaseStackDamage(CCombat* combat, CCombatCreature* attacker, CCombatCreature* defender, int mode, bool isRanged, bool isRetaliation)",
                subsystem="NWorld::Combat",
                summary="Computes stack base damage for Roll (mode=0), Min (mode=1), Avg (mode=2), or Max (mode=3). When mode=0 and stackCount > 3, averages 3 random rolls in [MinDamage, MaxDamage] and multiplies by stackCount * 0.33333334f (0x00e19950).",
                formula="mode==1 ? N*minDmg : mode==2 ? N*(minDmg+maxDmg)*0.5f : mode==3 ? N*maxDmg : (N<=3 ? sum_{1..N} rand(min,max) : N * (rand1+rand2+rand3)/3.0f)",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CArenaATB::CalcUnitTickPeriod",
                va=0x00A2D6C0,
                signature="float __stdcall CalcUnitTickPeriod(float initiative)",
                subsystem="NWorld::ATB",
                summary="Computes ATB timeline tick period for a creature stack with the given initiative using DefaultStats[+0x50] (fallback 0x186a0 = 100,000).",
                formula="float(DefaultStats->ATBBase > 0 ? DefaultStats->ATBBase : 100000) / (initiative > 0.0f ? initiative : 1.0f)",
            ),
            DecompiledFunctionSpec(
                name="NWorld::CArenaATB::CalcHeroTickPeriod",
                va=0x00A2D700,
                signature="float __cdecl CalcHeroTickPeriod()",
                subsystem="NWorld::ATB",
                summary="Computes ATB timeline tick period for a Hero using DefaultStats[+0x48] (Hero Initiative = 10) and DefaultStats[+0x50] (100,000), also used as reference turn unit for Wait (0.5), Defend (1.0), and Good Morale (0.5).",
                formula="float(DefaultStats->ATBBase > 0 ? DefaultStats->ATBBase : 100000) / float(DefaultStats->HeroInitiative > 0 ? DefaultStats->HeroInitiative : 1)",
            ),
        ]

        return DecompiledExeDatabase(
            exe_name=self.exe_path.name,
            exe_size=len(self.data),
            image_base=IMAGE_BASE,
            entry_point_va=self.entry_point_va,
            pdb_path=self.extract_pdb_path(),
            sections=[
                {
                    "name": s.name,
                    "start_va": f"0x{s.start_va:08x}",
                    "virtual_size": s.virtual_size,
                    "raw_offset": f"0x{s.raw_offset:08x}",
                    "raw_size": s.raw_size,
                }
                for s in self.sections
            ],
            exports=self.extract_exports(),
            rtti_summary={
                "total_type_descriptors": len(classes_by_td),
                "total_vtables": sum(len(c.vtables) for c in classes_by_td.values()),
                "total_vtable_slots": total_slots,
                "unique_virtual_methods": unique_methods,
                "classes_by_namespace": dict(sorted(ns_counts.items(), key=lambda kv: -kv[1])[:20]),
            },
            key_classes=key_classes,
            lua_c_api=[
                {
                    "name": b.name,
                    "func_va": f"0x{b.func_va:08x}",
                    "table_va": f"0x{b.table_va:08x}",
                }
                for b in lua_bindings
            ],
            default_stats_call_summary=subsystem_calls,
            hero_xp_table=full_xp,
            terrain_step_penalties=terrain_penalties,
            fpu_constants=fpu_constants,
            decompiled_functions=[asdict(f) | {"va": f"0x{f.va:08x}"} for f in decompiled_functions],
        )
