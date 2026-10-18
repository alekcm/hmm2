"""CLI entry point for homm5-decompile."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from .pe_analyzer import H5ExeAnalyzer


def main() -> int:
    parser = argparse.ArgumentParser(
        prog="homm5-decompile",
        description="Extract RTTI, vftables, Lua C-API, and decompiled NWorld formulas from H5_Game_PEST.exe",
    )
    parser.add_argument(
        "--exe",
        type=Path,
        default=Path("bin/H5_Game_PEST.exe"),
        help="Path to H5_Game_PEST.exe",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=Path("UnityProject/Assets/StreamingAssets/homm5_extracted/decompiled_exe_db.json"),
        help="Output JSON database path",
    )
    args = parser.parse_args()

    analyzer = H5ExeAnalyzer(args.exe)
    db = analyzer.build_database()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(db.to_dict(), indent=2), encoding="utf-8")

    print(
        f"[homm5-decompile] {db.exe_name} ({db.exe_size:,} bytes): "
        f"{db.rtti_summary['total_type_descriptors']} RTTI types, "
        f"{db.rtti_summary['total_vtables']} vftables, "
        f"{db.rtti_summary['unique_virtual_methods']} unique virtual methods, "
        f"{len(db.lua_c_api)} Lua C-API functions, "
        f"{len(db.decompiled_functions)} decompiled NWorld formulas -> {args.out}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
