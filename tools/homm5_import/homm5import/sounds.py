"""Sound and music catalog -- ``data/Sounds``, the audio files, and what binds them.

Three things are needed to make the game audible, and all three are in the
shipped data:

* **What a sound is.** ``data/Sounds/_(Sound)/**/*.xdb`` are ``<Sound>``
  records. Each names its audio by a GUID (``<uid>``), and the GUID is the file
  name in ``soundsfx/bin/Sounds/`` (2013 of 2018 records resolve; the other five
  are ambient/animation leftovers the shipped data itself does not ship).
  ``<SrcName>`` keeps the original WAV name, which is what a developer would
  recognise.  The files are plain RIFF WAV (mostly IMA ADPCM, which the runtime
  decodes itself) or Ogg Vorbis -- Unity reads Ogg natively, so nothing is
  transcoded.

* **What a theme is.** ``data/Sounds/_(Music)/**/*.xdb`` are ``<Music>``
  records: a ``<FileName>`` into the repository's ``music/`` tree, fade times
  and the random pause between repeats. ``music/`` already holds Ogg files.

* **What plays when.** Creatures, spells and the interface name sounds by
  reference. A creature's animation clips reference ``Sounds/_(Sound)/Creatures``
  folders (``attack``, ``hit``, ``death``, ``rangeattack`` ...), and a spell's
  ``SpellVisual`` names its ``SoundEffect`` and the ``Effect`` whose particle
  textures give the spell its look. Those bindings are resolved here, so the
  runtime reads ``creatures.json`` / ``spells.json`` instead of guessing names.

Output (under the import root):

    Game/sounds.json              {"sounds": {key: SoundDef}, "music": {key: MusicDef}}
    Audio/Sfx/<UID>.<ext>         copied verbatim
    Audio/Music/<path>.ogg        copied verbatim, same relative path as music/

Keys are lower-case paths without the namespace folder and suffix:
``_(Sound)/Interface/Ingame/Buy.xdb`` -> ``interface/ingame/buy``, and
``Sounds/_(Music)/Towns/Haven.(Music).xdb`` -> ``towns/haven``. Href-style
references normalise to the same keys (:func:`sound_key_from_href`), so a
lookup never depends on how the reference happened to be spelled.
"""

from __future__ import annotations

import os
import re
import shutil
from typing import Dict, Iterable, List, Optional, Tuple
from xml.etree import ElementTree as ET

from .xdb import XDB, texture_dds_path

SFX_DIRS = ("soundsfx/bin/Sounds", "sound/bin/SoundsLoc")
MUSIC_DIR = "music"

_SUFFIX = re.compile(r"\.\((sound|music)\)$", re.IGNORECASE)
_NON_ALNUM = re.compile(r"[^a-z0-9]")
_EVENT_TRAILING = re.compile(r"[_\-]*\d+$")


def sound_key_from_href(href: Optional[str]) -> Optional[str]:
    """``/Sounds/_(Sound)/Spells/Fireball.xdb#xpointer(/Sound)`` -> ``spells/fireball``.

    Also accepts a music reference (``/Sounds/_(Music)/Towns/Haven.(Music).xdb``)
    and returns ``towns/haven``. Returns None for anything outside ``Sounds/``.
    """
    if not href:
        return None
    path = href.split("#", 1)[0].replace("\\", "/").lstrip("/")
    if not path.lower().startswith("sounds/"):
        return None
    rest = path[len("sounds/"):]
    parts = rest.split("/", 1)
    if len(parts) != 2:
        return None
    rel = parts[1]
    if rel.lower().endswith(".xdb"):
        rel = rel[:-4]
    rel = _SUFFIX.sub("", rel)
    return rel.lower()


def _namespace(rel_under_sounds: str) -> str:
    return rel_under_sounds.split("/", 1)[0].lower()


def _ci_listing(directory: str) -> Dict[str, str]:
    try:
        return {name.lower(): name for name in os.listdir(directory)}
    except OSError:
        return {}


def _find_ci(base: str, rel: str) -> Optional[str]:
    """Resolve ``rel`` under ``base`` case-insensitively; None if absent."""
    cur = base
    for part in rel.replace("\\", "/").split("/"):
        if not part or part == ".":
            continue
        if part == "..":
            cur = os.path.dirname(cur)
            continue
        name = _ci_listing(cur).get(part.lower())
        if name is None:
            return None
        cur = os.path.join(cur, name)
    return cur if os.path.isfile(cur) else None


def _sfx_file(repo_root: str, uid: str) -> Optional[str]:
    """The audio file a sound GUID names, in either shipped SFX folder."""
    if not uid:
        return None
    for sub in SFX_DIRS:
        found = _find_ci(os.path.join(repo_root, sub), uid)
        if found:
            return found
    return None


def _music_file(repo_root: str, file_name: str) -> Optional[str]:
    """``..\\Music\\Town-Themes\\Town-Haven.ogg`` -> ``<repo>/music/Town-Themes/...``."""
    norm = (file_name or "").replace("\\", "/").strip()
    if not norm:
        return None
    lower = norm.lower()
    cut = lower.find("music/")
    rel = norm[cut + len("music/"):] if cut >= 0 else norm.lstrip("./")
    return _find_ci(os.path.join(repo_root, MUSIC_DIR), rel)


def _audio_ext(path: str) -> str:
    """The container, read from the file's magic -- SFX files carry no extension.

    ``RIFF`` is WAV, ``OggS`` is Ogg Vorbis; anything else keeps whatever
    extension the file has, so an unknown format is still copied rather than
    silently renamed to something it is not.
    """
    with open(path, "rb") as fh:
        head = fh.read(4)
    if head == b"RIFF":
        return ".wav"
    if head == b"OggS":
        return ".ogg"
    return os.path.splitext(path)[1].lower() or ".bin"


def _text(elem: Optional[ET.Element], default: str = "") -> str:
    return XDB.text(elem, default)


def _num(elem: Optional[ET.Element], default: float = 0.0) -> float:
    return XDB.number(elem, default)


def load_sound_catalog(xdb: XDB, repo_root: str) -> Tuple[Dict[str, dict], Dict[str, dict], dict]:
    """Every ``<Sound>`` and ``<Music>`` record that resolves to a real file.

    Returns ``(sounds, music, report)``. ``report`` counts what was found and
    what was dropped, so the CLI can say so instead of silently shrinking.
    """
    sounds: Dict[str, dict] = {}
    music: Dict[str, dict] = {}
    report = {"soundRecords": 0, "soundMissingFile": 0, "soundDuplicateKey": 0,
              "musicRecords": 0, "musicMissingFile": 0, "musicDuplicateKey": 0}

    for path in sorted(xdb.iter_files("Sounds")):
        rel = os.path.relpath(path, xdb.data_root).replace("\\", "/")
        under = rel[len("Sounds/"):] if rel.startswith("Sounds/") else rel
        ns = _namespace(under)
        key = sound_key_from_href("/" + rel)
        if key is None:
            continue
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue

        if root.tag == "Sound" and ns == "_(sound)":
            report["soundRecords"] += 1
            uid = _text(root.find("uid")).lower()
            src = _sfx_file(repo_root, uid)
            if src is None:
                report["soundMissingFile"] += 1
                continue
            if key in sounds:
                report["soundDuplicateKey"] += 1
                continue
            sounds[key] = {
                "uid": uid,
                "file": "Audio/Sfx/%s%s" % (uid, _audio_ext(src)),
                "source": (root.find("SrcName").get("href") or "") if root.find("SrcName") is not None else "",
                "loop": XDB.boolean(root.find("Loop")),
                "is3D": XDB.boolean(root.find("Is3D")),
                "minDistance": _num(root.find("MinDistance")),
                "maxDistance": _num(root.find("MaxDistance")),
                "volume": _num(root.find("VolumeBase"), 1.0),
                "priority": int(_num(root.find("Priority"))),
            }
        elif root.tag == "Music" and ns == "_(music)":
            report["musicRecords"] += 1
            src = _music_file(repo_root, _text(root.find("FileName")))
            if src is None:
                report["musicMissingFile"] += 1
                continue
            if key in music:
                report["musicDuplicateKey"] += 1
                continue
            rel_music = os.path.relpath(src, os.path.join(repo_root, MUSIC_DIR)).replace("\\", "/")
            music[key] = {
                "file": "Audio/Music/" + rel_music,
                "fadeIn": int(_num(root.find("FadeIn"))),
                "fadeOut": int(_num(root.find("FadeOut"))),
                "minPause": int(_num(root.find("MinPausedTime"))),
                "maxPause": int(_num(root.find("MaxPausedTime"))),
            }
    return sounds, music, report


def copy_audio(repo_root: str, out_root: str, sounds: Dict[str, dict],
               music: Dict[str, dict]) -> Tuple[int, int]:
    """Copy every catalogued file into the import root. Returns (copied, skipped).

    A file already present with the same size is skipped, so re-running the
    stage after a partial run is cheap.
    """
    copied = skipped = 0
    jobs: List[Tuple[str, str]] = []
    for entry in sounds.values():
        jobs.append((_sfx_file(repo_root, entry["uid"]), os.path.join(out_root, entry["file"])))
    for entry in music.values():
        rel = entry["file"][len("Audio/Music/"):]
        src = _find_ci(os.path.join(repo_root, MUSIC_DIR), rel)
        jobs.append((src, os.path.join(out_root, entry["file"])))
    for src, dst in jobs:
        if not src:
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if os.path.isfile(dst) and os.path.getsize(dst) == os.path.getsize(src):
            skipped += 1
            continue
        shutil.copyfile(src, dst)
        copied += 1
    return copied, skipped


# ---------------------------------------------------------------------------
# Creatures: which sound file is their "attack", "hit", "death" ...
# ---------------------------------------------------------------------------

CREATURE_SOUND_ROOT = "Sounds/_(Sound)/Creatures"


def normalise_name(name: str) -> str:
    """``Arch_Magi`` / ``ArchMagi`` / ``Arch Magi`` -> ``archmagi``."""
    return _NON_ALNUM.sub("", (name or "").lower())


def _event_name(stem: str, owner: str) -> str:
    """``ArchMagi_hit`` under ``ArchMagi`` -> ``hit``; ``attack00`` -> ``attack``."""
    lower = stem.lower()
    prefix = owner.lower()
    if lower.startswith(prefix):
        lower = lower[len(prefix):].lstrip("_- ")
    lower = _EVENT_TRAILING.sub("", lower)
    return lower.strip("_- ")


def load_creature_sounds(xdb: XDB) -> Dict[str, Dict[str, str]]:
    """``normalise_name(folder)`` -> ``{event: sound key}``.

    Creature sound folders are not uniformly nested (``Dwarves/T1_Dwarf/
    Deffender/attack00.xdb`` and ``Academy/ArchMagi/attack.xdb``), so the owner
    is simply the directory an event file sits in. When two files reduce to the
    same event, the first in sorted order wins -- ``attack`` before ``attack00``.
    """
    out: Dict[str, Dict[str, str]] = {}
    files = sorted(xdb.iter_files(CREATURE_SOUND_ROOT))
    for path in files:
        owner_dir = os.path.basename(os.path.dirname(path))
        owner = normalise_name(owner_dir)
        if not owner:
            continue
        stem = os.path.splitext(os.path.basename(path))[0]
        event = _event_name(stem, owner_dir)
        if not event:
            continue
        key = sound_key_from_href("/" + os.path.relpath(path, xdb.data_root).replace("\\", "/"))
        if key is None:
            continue
        bucket = out.setdefault(owner, {})
        bucket.setdefault(event, key)
    return out


_KIND_SUFFIX = re.compile(r"\d+$")


def creature_anim_sounds(xdb: XDB, anim: Optional[ET.Element],
                         anim_href: Optional[str]) -> Dict[str, str]:
    """The sound table a creature's own animation set names -- the exact one.

    ``AnimCharacter`` -> ``ArenaAnimSet`` lists ``Kind``/``Anim`` pairs, and each
    ``Anim`` is a ``BasicSkelAnim`` whose ``<Sound>`` is the clip's sound. The
    Kind (``attack00``, ``rangeattack``, ``death`` ...) is the event name, with
    its trailing frame number stripped. Empty when the chain does not resolve.
    """
    out: Dict[str, str] = {}
    if anim is None:
        return out
    arena, arena_href = xdb.resolve(anim.find("ArenaAnimSet"), anim_href)
    if arena is None:
        return out
    for item in XDB.items(arena.find("animations")):
        kind = XDB.text(item.find("Kind")).lower()
        kind = _KIND_SUFFIX.sub("", kind).strip("_- ")
        if not kind:
            continue
        clip, _clip_href = xdb.resolve(item.find("Anim"), arena_href)
        if clip is None:
            continue
        sound_elem = clip.find("Sound")
        key = sound_key_from_href(sound_elem.get("href") if sound_elem is not None else None)
        if key:
            out.setdefault(kind, key)
    return out


def creature_sounds_for(creature_id: str, index: Dict[str, Dict[str, str]]) -> Dict[str, str]:
    """The sound table of one creature record, matched by name. Empty if none."""
    return dict(index.get(normalise_name(creature_id), {}))


# ---------------------------------------------------------------------------
# Spells: the SpellVisual each cast uses -- its sound and the particle textures.
# ---------------------------------------------------------------------------

MAX_EFFECT_TEXTURES = 16


def manifest_key_on_disk(xdb: XDB, dds_key: str) -> Optional[str]:
    """``/Textures/a/B.dds`` -> the same key spelled the way the file is on disk.

    The texture manifest is keyed by the real file name, and the data refers to
    some textures with different case. Windows never noticed; a case-sensitive
    lookup would miss, so the key is re-spelled here, or dropped if no file exists.
    """
    rel = dds_key.replace("\\", "/").lstrip("/")
    found = _find_ci(xdb.data_root, rel)
    if found is None:
        return None
    return "/" + os.path.relpath(found, xdb.data_root).replace("\\", "/")


def effect_textures(xdb: XDB, effect_root: Optional[ET.Element],
                    effect_href: Optional[str]) -> List[str]:
    """Every particle texture an ``<Effect>`` uses, as manifest keys, in order."""
    if effect_root is None:
        return []
    out: List[str] = []
    seen = set()
    for textures in effect_root.iter("Textures"):
        for item in XDB.items(textures):
            href = item.get("href")
            if not href or href.startswith("#"):
                continue
            tex_elem, tex_href = xdb.resolve(item, effect_href)
            key = texture_dds_path(xdb, tex_elem, tex_href) if tex_elem is not None else None
            key = manifest_key_on_disk(xdb, key) if key else None
            if not key or key in seen:
                continue
            seen.add(key)
            out.append(key)
            if len(out) >= MAX_EFFECT_TEXTURES:
                return out
    return out


def resolve_spell_visuals(xdb: XDB, spell_href: Optional[str],
                          visual_refs: Iterable[str]) -> List[dict]:
    """Decode a spell's ``visuals`` list into sound + effect data.

    ``visual_refs`` are the raw strings the importer stored (``Fireball.(SpellVisual).xdb#xpointer(/SpellVisual)``),
    relative to the spell record. Anything that does not resolve is dropped so
    the runtime never has to check for holes.
    """
    out: List[dict] = []
    for ref in visual_refs or []:
        if not ref:
            continue
        visual, visual_href = xdb.resolve(ET.Element("x", {"href": ref}), spell_href)
        if visual is None or visual.tag != "SpellVisual":
            continue
        sound_elem = visual.find("SoundEffect")
        sound = sound_key_from_href(sound_elem.get("href") if sound_elem is not None else None) or ""
        effect_root, effect_href = xdb.resolve(visual.find("Effect"), visual_href)
        out.append({
            "sound": sound,
            "textures": effect_textures(xdb, effect_root, effect_href),
            "particleDuration": _num(visual.find("ParticleDuration")),
            "hitDelay": _num(visual.find("HitDelay")),
        })
    return out
