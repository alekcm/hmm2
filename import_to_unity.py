#!/usr/bin/env python3
"""One-command importer.  Run this from anywhere:

    python import_to_unity.py              # everything
    python import_to_unity.py gamedata     # one stage
    python import_to_unity.py --help

It exists because the pipeline is a package (``homm5import``) and a package
cannot be run by pointing python at its ``cli.py`` -- that fails with
"attempted relative import with no known parent package".  This script puts the
package on ``sys.path`` first, so there is no directory to be in and nothing to
install.

Paths are resolved relative to this file, so it works from any working
directory and on any platform:

    <repo>/data                                    game data   (input)
    <repo>/texts                                   localization (input)
    <repo>/UnityProject/Assets/StreamingAssets/HoMM5            (output)
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
IMPORTER_DIR = os.path.join(HERE, "tools", "homm5_import")
DATA = os.path.join(HERE, "data")
TEXTS = os.path.join(HERE, "texts")
DEFAULT_OUT = os.path.join(HERE, "UnityProject", "Assets", "StreamingAssets", "HoMM5")


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)

    if not os.path.isdir(IMPORTER_DIR):
        print("error: importer not found at %s" % IMPORTER_DIR, file=sys.stderr)
        print("       is this script still at the root of the repository?", file=sys.stderr)
        return 2
    sys.path.insert(0, IMPORTER_DIR)

    from homm5import.cli import main as cli_main

    if not os.path.isdir(DATA):
        print("error: no game data at %s" % DATA, file=sys.stderr)
        print("       unpack data.pak into a 'data' folder next to this script.", file=sys.stderr)
        return 2

    # Supply the roots and the output unless the caller already did.
    if not any(a.startswith("--data") for a in argv):
        argv += ["--data", DATA]
    if os.path.isdir(TEXTS) and not any(a.startswith("--texts") for a in argv):
        argv += ["--texts", TEXTS]
    out = DEFAULT_OUT
    for flag in ("-o", "--out"):
        if flag in argv:
            i = argv.index(flag)
            if i + 1 < len(argv):
                out = argv[i + 1]
            break
    else:
        argv += ["-o", DEFAULT_OUT]

    print("game data : %s" % DATA)
    print("texts     : %s" % (TEXTS if os.path.isdir(TEXTS) else "(none)"))
    print("output    : %s" % out)
    print()

    if not argv or argv[0].startswith("-"):
        argv = ["all"] + argv

    return cli_main(argv)


if __name__ == "__main__":
    sys.exit(main())
