"""XDB loading with ``href`` resolution, backed by ``data/types.xml``.

An ``.xdb`` is plain XML, but objects refer to each other with
``href="/Path/To/Other.xdb#xpointer(/Root)"`` paths rooted at the ``data``
folder, and can also inline a value with ``href="#n:inline(Type)"``.  This
module resolves both against a game root so that, e.g., a ``Creature`` can walk
to its ``CreatureVisual`` and on to the geometry UID without the caller knowing
anything about the file layout.
"""

from __future__ import annotations

import os
import re
from typing import Dict, Iterator, List, Optional, Tuple
from xml.etree import ElementTree as ET

HREF_SUFFIX = "#xpointer"
INLINE_PREFIX = "#n:inline"
_XPOINTER = re.compile(r"xpointer\(\s*/?(.*?)\s*\)$")

_NUM = re.compile(r"^[-+]?(\d+\.?\d*([eE][-+]?\d+)?|\d+)$")


class XDBError(ValueError):
    pass


def _split_href(href: str) -> Tuple[str, Optional[str]]:
    """``"/A/B.xdb#xpointer(/Root)"`` -> ``("/A/B.xdb", "Root")``."""
    if "#" not in href:
        return href, None
    path, _, pointer = href.partition("#")
    m = _XPOINTER.match(pointer)
    if m:
        return path, m.group(1).strip("/")
    return path, None


class XDB:
    """A view over one game data root (the unpacked ``data.pak``)."""

    def __init__(self, data_root: str):
        self.data_root = os.path.abspath(data_root)
        self._cache: Dict[str, Optional[ET.Element]] = {}

    # -- loading -----------------------------------------------------------
    def _path_for(self, href: str) -> str:
        rel = href.lstrip("/").replace("\\", "/")
        return os.path.join(self.data_root, rel)

    @staticmethod
    def absolutize(href: str, base_href: Optional[str]) -> str:
        """Make an href absolute.

        Most hrefs are rooted at the data folder, but some are relative to the
        file that holds them -- ``<Model href="T4_Combat_Mage.xdb#..."/>`` inside
        ``/Characters/.../(Character).xdb``.  Resolving those against the data
        root silently finds nothing.
        """
        path, pointer = _split_href(href)
        if not path:
            return href
        if path.startswith("/") or base_href is None:
            return href
        base_dir = os.path.dirname(_split_href(base_href)[0])
        joined = os.path.normpath(os.path.join(base_dir, path)).replace("\\", "/")
        return "/" + joined.lstrip("/") + (("#xpointer(%s)" % pointer) if pointer else "")

    def load(self, href: str) -> Optional[ET.Element]:
        """Load the element an href points at, or None if it does not exist."""
        path, pointer = _split_href(href)
        if not path:
            return None
        key = os.path.normpath(self._path_for(path))
        if key not in self._cache:
            if not os.path.isfile(key):
                self._cache[key] = None
            else:
                try:
                    self._cache[key] = ET.parse(key).getroot()
                except ET.ParseError:
                    self._cache[key] = None
        root = self._cache[key]
        if root is None:
            return None
        if pointer:
            target = pointer.strip("/()")
            if target and root.tag != target:
                found = root.find(target)
                return found if found is not None else root
        return root

    def exists(self, href: str) -> bool:
        path, _ = _split_href(href)
        return bool(path) and os.path.isfile(self._path_for(path))

    # -- field access ------------------------------------------------------
    @staticmethod
    def href_of(elem: ET.Element) -> Optional[str]:
        return elem.get("href")

    def resolve_field(self, elem: Optional[ET.Element],
                      base_href: Optional[str] = None) -> Optional[ET.Element]:
        """Follow ``elem``'s href.  Inline values are the element's own child."""
        return self.resolve(elem, base_href)[0]

    def resolve(self, elem: Optional[ET.Element],
                base_href: Optional[str] = None) -> Tuple[Optional[ET.Element], Optional[str]]:
        """Like :meth:`resolve_field`, also returning the absolute href used."""
        if elem is None:
            return None, None
        href = self.href_of(elem)
        if not href:
            return None, None
        if href.startswith(INLINE_PREFIX):
            return (list(elem)[0] if len(elem) else None), None
        if href.startswith("#"):
            return None, None
        absolute = self.absolutize(href, base_href)
        return self.load(absolute), _split_href(absolute)[0] or None

    @staticmethod
    def text(elem: Optional[ET.Element], default: str = "") -> str:
        if elem is None or elem.text is None:
            return default
        return elem.text.strip()

    @classmethod
    def number(cls, elem: Optional[ET.Element], default: float = 0.0) -> float:
        t = cls.text(elem)
        if not t or not _NUM.match(t):
            return default
        return float(t)

    @staticmethod
    def boolean(elem: Optional[ET.Element], default: bool = False) -> bool:
        t = (elem.text or "").strip().lower() if elem is not None else ""
        if t in ("true", "1", "yes"):
            return True
        if t in ("false", "0", "no"):
            return False
        return default

    @staticmethod
    def vec3(elem: Optional[ET.Element]) -> Tuple[float, float, float]:
        if elem is None:
            return (0.0, 0.0, 0.0)
        return tuple(float((elem.find(c).text or "0").strip()) for c in "xyz")  # type: ignore[return-value]

    @staticmethod
    def items(elem: Optional[ET.Element]) -> List[ET.Element]:
        if elem is None:
            return []
        return [c for c in elem if c.tag == "Item"]

    # -- iteration ---------------------------------------------------------
    def iter_files(self, subdir: str, suffix: str = ".xdb") -> Iterator[str]:
        base = os.path.join(self.data_root, subdir)
        if not os.path.isdir(base):
            return
        for dirpath, _dirs, files in os.walk(base):
            for f in files:
                if f.lower().endswith(suffix):
                    yield os.path.join(dirpath, f)

    def href_from_path(self, path: str) -> str:
        rel = os.path.relpath(os.path.abspath(path), self.data_root).replace("\\", "/")
        return "/" + rel

    def logical_name(self, href: str) -> str:
        """``/GameMechanics/.../CombatMage.xdb`` -> ``CombatMage``."""
        path, _ = _split_href(href)
        base = os.path.basename(path)
        return re.sub(r"\.(.*)?\.", ".", base).replace(".xdb", "") if ".xdb" in base else base
