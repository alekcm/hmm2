"""Localization tables.

The ``texts`` tree mirrors ``data`` one-for-one: a ``.xdb`` field such as
``<NameFileRef href="/Text/Game/Creatures/Academy/3rd/CombatMage_Name.txt"/>``
resolves against the texts root.  Files are UTF-16 LE with a BOM.
"""

from __future__ import annotations

import os
from typing import Dict, Iterator, Optional


class Localization:
    def __init__(self, texts_root: str):
        self.texts_root = os.path.abspath(texts_root)
        self._cache: Dict[str, Optional[str]] = {}

    def read(self, href: str) -> Optional[str]:
        """Read one text file by its ``/Text/...`` href."""
        rel = href.lstrip("/").replace("\\", "/")
        key = os.path.normpath(os.path.join(self.texts_root, rel))
        if key in self._cache:
            return self._cache[key]
        value: Optional[str] = None
        if os.path.isfile(key):
            with open(key, "rb") as fh:
                raw = fh.read()
            value = _decode_text(raw)
        self._cache[key] = value
        return value

    def exists(self, href: str) -> bool:
        return os.path.isfile(os.path.join(self.texts_root, href.lstrip("/")))

    def iter_files(self) -> Iterator[str]:
        for dirpath, _dirs, files in os.walk(self.texts_root):
            for f in files:
                yield os.path.join(dirpath, f)

    def dump_all(self) -> Dict[str, str]:
        """Every text keyed by its href, for one flat JSON table."""
        out: Dict[str, str] = {}
        for path in self.iter_files():
            rel = os.path.relpath(path, self.texts_root).replace("\\", "/")
            with open(path, "rb") as fh:
                out["/" + rel] = _decode_text(fh.read())
        return out


def _decode_text(raw: bytes) -> str:
    """UTF-16 LE with BOM, with a latin-1 fallback for the odd stray file."""
    if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return raw.decode("utf-16", "replace").lstrip("\ufeff")
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("cp1251", "replace")
