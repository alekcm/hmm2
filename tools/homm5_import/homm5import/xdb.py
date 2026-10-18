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
        # Tribes of the East 3.1 ships an a2p1-data overlay next to the base
        # data.pak. Windows searches that overlay before the base tree. Keep one
        # logical XDB view for every import stage, not only the UI stage.
        sibling_overlay = os.path.join(os.path.dirname(self.data_root), "a2p1-data")
        self.overlay_root = (os.path.abspath(sibling_overlay)
                             if os.path.isdir(sibling_overlay) else None)
        self._cache: Dict[str, Optional[ET.Element]] = {}
        self._dir_cache: Dict[str, dict] = {}

    # -- loading -----------------------------------------------------------
    def _path_for(self, href: str) -> str:
        rel = href.lstrip("/").replace("\\", "/")
        if self.overlay_root:
            overlay_path = os.path.join(self.overlay_root, rel)
            if os.path.exists(overlay_path):
                return overlay_path
            resolved_overlay = self._resolve_case_insensitive(rel, self.overlay_root)
            if resolved_overlay:
                return resolved_overlay
        path = os.path.join(self.data_root, rel)
        if os.path.exists(path):
            return path
        # Some hrefs are lowercased in the data while the file on disk keeps
        # the original case. Windows folds case, so do the same on Linux.
        resolved = self._resolve_case_insensitive(rel, self.data_root)
        return resolved if resolved else path

    def _resolve_case_insensitive(self, rel: str, root: str) -> Optional[str]:
        cur = root
        for part in rel.split("/"):
            if not part:
                continue
            listing = self._listing(cur)
            if listing is None:
                return None
            match = listing.get(part.lower())
            if match is None:
                return None
            cur = os.path.join(cur, match)
        return cur if os.path.exists(cur) else None

    def _listing(self, directory: str) -> Optional[dict]:
        cached = self._dir_cache.get(directory)
        if cached is None:
            try:
                cached = {name.lower(): name for name in os.listdir(directory)}
            except OSError:
                cached = {}
            self._dir_cache[directory] = cached
        return cached

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

    def canonical_href(self, href: str) -> str:
        """Rewrite an href to the file that is actually on disk.

        References inside the data are not always spelled the way the file is:
        ``Orc_Stronghold.(AdvMapTownShared).xdb`` lists
        ``TownBuildingSharedStats/Stronghold/Blacksmith/Blacksmith.xdb`` while
        the folder holds ``BlackSmith.xdb``.  Windows folds case so the game
        never noticed; on Linux a key built from the href simply does not match
        the key built from the file.  Resolving here keeps one spelling.
        """
        path, pointer = _split_href(href)
        if not path:
            return href
        found = self._path_for(path)
        if not os.path.exists(found):
            return href
        logical = self.href_from_path(found)
        return logical + (("#xpointer(%s)" % pointer) if pointer else "")

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
        """Yield one physical file per logical path, overlay before base.

        Sorting by the logical path keeps callers deterministic even when the
        overlay supplies only a subset of records (sorting physical paths would
        put every ``a2p1-data`` record ahead of its base-tree neighbours).
        """
        chosen = {}
        roots = ([self.overlay_root] if self.overlay_root else []) + [self.data_root]
        for root in roots:
            if not root:
                continue
            base = os.path.join(root, subdir)
            if not os.path.isdir(base):
                continue
            for dirpath, _dirs, files in os.walk(base):
                for f in files:
                    if not f.lower().endswith(suffix):
                        continue
                    path = os.path.join(dirpath, f)
                    logical = os.path.relpath(path, root).replace("\\", "/").lower()
                    if logical not in chosen:
                        chosen[logical] = path
        for logical in sorted(chosen):
            yield chosen[logical]

    def href_from_path(self, path: str) -> str:
        absolute = os.path.abspath(path)
        roots = ([self.overlay_root] if self.overlay_root else []) + [self.data_root]
        for root in roots:
            if not root:
                continue
            try:
                rel = os.path.relpath(absolute, root)
            except ValueError:
                continue
            if rel != os.pardir and not rel.startswith(os.pardir + os.sep):
                return "/" + rel.replace("\\", "/")
        return "/" + os.path.basename(absolute)

    def logical_name(self, href: str) -> str:
        """``/GameMechanics/.../CombatMage.xdb`` -> ``CombatMage``."""
        path, _ = _split_href(href)
        base = os.path.basename(path)
        return re.sub(r"\.(.*)?\.", ".", base).replace(".xdb", "") if ".xdb" in base else base

def texture_dds_path(xdb: "XDB", texture_elem, base_href: Optional[str] = None) -> Optional[str]:
    """Resolve a ``<Texture>`` element to the .dds the game ships.

    The element names the file as ``<DestName href="Snow.dds"/>``, relative to
    the Texture XDB's own folder rather than to the data root.  There is no
    ``<FileName>`` child -- reading one silently yields nothing, which left every
    map object untextured until it was noticed.

    Returns the path in the form ``Textures/_manifest.json`` keys it.
    """
    if texture_elem is None:
        return None
    dest = texture_elem.find("DestName")
    ref = dest.get("href") if dest is not None else None
    href = xdb.absolutize(ref, base_href) if ref else XDB.text(dest)
    if not href:
        href = XDB.text(texture_elem.find("FileName"))
    return "/" + href.lstrip("/") if href else None


def advmap_tile_texture(xdb: "XDB", href: str) -> Optional[str]:
    """``/MapObjects/_(AdvMapTile)/Snow/Snow.xdb#...`` -> ``/Textures/.../Snow.dds``.

    Terrain texture layers reference AdvMapTile records, which point at a
    Texture record, which points at the .dds.  Also returns the tile's paint
    priority through :func:`advmap_tile`.
    """
    info = advmap_tile(xdb, href)
    return info["texture"] if info else None


def advmap_tile(xdb: "XDB", href: str) -> Optional[dict]:
    """Decode one AdvMapTile record: its texture, paint priority, and type."""
    tile = xdb.load(href)
    if tile is None or tile.tag != "AdvMapTile":
        return None
    tex_elem, tex_href = xdb.resolve(tile.find("Texture"))
    return {
        "texture": texture_dds_path(xdb, tex_elem, tex_href),
        "priority": int(XDB.number(tile.find("Priority"))),
        "type": XDB.text(tile.find("Type")),
        "minimap": XDB.vec3(tile.find("MinimapColor")),
    }
