"""Localization tables.

The ``texts`` tree mirrors ``data`` one-for-one: a ``.xdb`` field such as
``<NameFileRef href="/Text/Game/Creatures/Academy/3rd/CombatMage_Name.txt"/>``
resolves against the texts root.  Files are UTF-16 LE with a BOM.
"""

from __future__ import annotations

import os
from typing import Dict, Iterator, Optional


class Localization:
    def __init__(self, texts_root: str, patch_texts_root: Optional[str] = None):
        self.texts_root = os.path.abspath(texts_root)
        if patch_texts_root is None and os.path.basename(self.texts_root) == "texts":
            cand = os.path.join(os.path.dirname(self.texts_root), "a2p1-texts")
            if os.path.isdir(cand):
                patch_texts_root = cand
        self.patch_texts_root: Optional[str] = (
            os.path.abspath(patch_texts_root) if patch_texts_root else None
        )
        self._cache: Dict[str, Optional[str]] = {}

    def _resolve_path(self, href: str) -> str:
        rel = href.lstrip("/").replace("\\", "/")
        if self.patch_texts_root:
            patch_cand = os.path.normpath(os.path.join(self.patch_texts_root, rel))
            if os.path.isfile(patch_cand):
                return patch_cand
        return os.path.normpath(os.path.join(self.texts_root, rel))

    def read(self, href: str) -> Optional[str]:
        """Read one text file by its ``/Text/...`` href."""
        key = self._resolve_path(href)
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
        return os.path.isfile(self._resolve_path(href))

    def iter_files(self) -> Iterator[str]:
        seen = set()
        roots = [r for r in (self.patch_texts_root, self.texts_root) if r is not None]
        for root in roots:
            for dirpath, _dirs, files in os.walk(root):
                for f in files:
                    full = os.path.join(dirpath, f)
                    rel = os.path.relpath(full, root).replace("\\", "/").lower()
                    if rel not in seen:
                        seen.add(rel)
                        yield full

    def dump_all(self) -> Dict[str, str]:
        """Every text keyed by its href, for one flat JSON table."""
        out: Dict[str, str] = {}
        roots = [r for r in (self.texts_root, self.patch_texts_root) if r is not None]
        for root in roots:
            for dirpath, _dirs, files in os.walk(root):
                for f in files:
                    path = os.path.join(dirpath, f)
                    rel = os.path.relpath(path, root).replace("\\", "/")
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
