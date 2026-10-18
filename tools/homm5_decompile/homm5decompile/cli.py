"""CLI entry point for homm5-decompile."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from .pe_analyzer import H5ExeAnalyzer
from .cpp_exporter import export_decompiled_cpp_tree


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
        "--types-xml",
        type=Path,
        default=Path("data/types.xml"),
        help="Path to data/types.xml for NDb C++ header reconstruction",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=Path("UnityProject/Assets/StreamingAssets/homm5_extracted/decompiled_exe_db.json"),
        help="Output JSON database path",
    )
    parser.add_argument(
        "--cpp-out",
        type=Path,
        default=Path("decompiled_src"),
        help="Output directory for reconstructed C++ headers (.h) and decompiled sources (.cpp)",
    )
    args = parser.parse_args()

    analyzer = H5ExeAnalyzer(args.exe)
    db = analyzer.build_database()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(db.to_dict(), indent=2), encoding="utf-8")

    gr2_c_src = None
    try:
        from homm5import.gr2 import _OODLE1_C_SOURCE
        gr2_c_src = _OODLE1_C_SOURCE
    except Exception:
        pass

    cpp_stats = export_decompiled_cpp_tree(
        exe_path=args.exe,
        types_xml_path=args.types_xml,
        out_dir=args.cpp_out,
        gr2_oodle1_c_source=gr2_c_src,
    )

    print(
        f"[homm5-decompile] {db.exe_name} ({db.exe_size:,} bytes): "
        f"{db.rtti_summary['total_type_descriptors']} RTTI types, "
        f"{db.rtti_summary['total_vtables']} vftables, "
        f"{db.rtti_summary['unique_virtual_methods']} unique virtual methods, "
        f"{len(db.lua_c_api)} Lua C-API functions, "
        f"{len(db.decompiled_functions)} decompiled NWorld formulas -> {args.out} & {args.cpp_out} ({cpp_stats['total_rtti_classes']} C++ classes)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
