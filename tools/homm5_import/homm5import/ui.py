"""Small, explicit native UI skin, resolved through the shipped XDB graph.

This is not a general WindowScreen interpreter. It exports the adventure HUD
chrome, button states, resource layout and menu art the Unity widgets consume.
PNG output is independent of the full texture import and never uses screenshots.
"""
from __future__ import annotations

import json
import os
from pathlib import Path

from .dds import decode_dds, write_png
from .gamedata import load_heroes, texture_dds_path
from .xdb import XDB


class UiXDB(XDB):
    """The expansion's layout overrides the base data where a file exists."""
    def __init__(self, data_root):
        super().__init__(data_root)
        self.overlay = XDB(str(Path(data_root).parent / "a2p1-data"))

    def _path_for(self, href):
        overlay = self.overlay._path_for(href)
        return overlay if os.path.isfile(overlay) else super()._path_for(href)


def _placement(window, shared):
    result = {}
    for field in ("Position", "Size", "HorAllign", "VerAllign"):
        own = window.find("Placement/" + field)
        node = own if own is not None and own.findtext("Second") == "true" else None
        if node is None and shared is not None:
            node = shared.find("Placement/" + field)
        if node is None:
            continue
        first = node.find("First")
        if first is None:
            continue
        if field in ("Position", "Size"):
            result[field] = [int(first.findtext("x", "0")), int(first.findtext("y", "0"))]
        else:
            result[field] = first.text or "EPA_LOW_END"
    return result


def export_ui(data_root, output_root):
    xdb = UiXDB(data_root)
    out = Path(output_root) / "UI"
    out.mkdir(parents=True, exist_ok=True)
    result = {"version": 1, "assets": {}, "windows": {}, "heroPortraits": {}, "source": "shipped XDB/DDS; a2p1 layout overrides"}
    decoded = {}

    def image(key, source, crop=None):
        if not source:
            return None
        path = xdb._path_for(source)
        if not os.path.isfile(path):
            raise FileNotFoundError(source)
        if path not in decoded:
            decoded[path] = decode_dds(Path(path).read_bytes())
        w, h, rgba = decoded[path]
        # BackgroundSimpleTexture uses native pixels, clipped to its window;
        # resource-bar is a 697x52 region in a padded 1024x64 DDS, not a scaled image.
        cw, ch = (min(w, crop[0]), min(h, crop[1])) if crop else (w, h)
        if cw <= 0 or ch <= 0:
            cw, ch = w, h
        x0 = y0 = 0
        if key in ("menuPanel", "menuLogo"):
            opaque = [(x, y) for y in range(h) for x in range(w) if rgba[(y*w+x)*4+3] > 0]
            if opaque:
                x0 = min(p[0] for p in opaque)
                y0 = min(p[1] for p in opaque)
                cw = max(p[0] for p in opaque) - x0 + 1
                ch = max(p[1] for p in opaque) - y0 + 1
        pixels = b"".join(rgba[((y+y0)*w+x0)*4:((y+y0)*w+x0+cw)*4] for y in range(ch))
        name = key.replace(":", "_").replace("/", "_") + ".png"
        write_png(str(out / name), cw, ch, pixels)
        result["assets"][key] = {"png": "UI/" + name, "width": cw, "height": ch, "source": source}
        return key

    def background(key, node, base, crop=None):
        bg, bg_href = xdb.resolve(node, base)
        if bg is None:
            return None
        tex, tex_href = xdb.resolve(bg.find("Texture"), bg_href)
        return image(key, texture_dds_path(xdb, tex, tex_href), crop)

    def window(key, href):
        node = xdb.load(href)
        if node is None:
            raise FileNotFoundError(href)
        shared, shared_href = xdb.resolve(node.find("Shared"), href)
        placement = _placement(node, shared)
        size = placement.get("Size", [0, 0])
        data = {"x": placement.get("Position", [0, 0])[0], "y": placement.get("Position", [0, 0])[1],
                "width": size[0], "height": size[1], "horizontal": placement.get("HorAllign", "EPA_LOW_END"),
                "vertical": placement.get("VerAllign", "EPA_LOW_END"), "source": href}
        if shared is not None:
            crop = size if min(size) > 0 else None
            for state in ("Normal", "Pushed", "Disabled"):
                ref = shared.find("VisualStates/Item/" + state + "/Background")
                asset = background(key + ":" + state.lower(), ref, shared_href, crop)
                if asset:
                    data[state.lower()] = asset
            if "normal" not in data:
                asset = background(key + ":normal", shared.find("Background"), shared_href, crop)
                if asset:
                    data["normal"] = asset
        result["windows"][key] = data
        return data

    base = "/UI/AdventureScreen-heavy/"
    for key, name, kind in (("dial", "JogDialBase", "WindowSimple"),
                            ("endTurn", "JogDialButton", "WindowMSButton"),
                            ("minimap", "Minimap-heavy", "WindowSimple"),
                            ("resources", "resource-bar-heavy", "WindowSimple")):
        window(key, base + name + ".(" + kind + ").xdb")
    for name in ("Menu", "NextHero", "MoveHero", "Scenario", "Spellbook", "Underground"):
        window(name, base + "OnJog-" + name + ".(WindowMSButton).xdb")
    for name in ("wood", "ore", "mercury", "crystals", "sulfur", "gems", "gold"):
        window("resource:" + name, "/UI/ResourceBar/resource-bar_(2)/" + name + "-icon.(WindowSimple).xdb")
        text_name = "crystal" if name == "crystals" else name
        window("value:" + name, "/UI/ResourceBar/resource-bar_(2)/" + text_name + "-text.(WindowStaticText).xdb")
    for key, path in (("menuPanel", "/UI/MainMenu2/Menu.(Texture).dds"),
                      ("menuLogo", "/UI/MainMenu2/Logo.(Texture).dds"),
                      ("rosterFrame", "/Textures/AdventureScreen-FPP-2/List-C.dds")):
        image(key, path)
    # Native faction icons rather than text on a stretched list-border tile.
    for town, name in (("HEAVEN", "Heaven"), ("INFERNO", "Inferno"),
                       ("NECROMANCY", "Necro"), ("ACADEMY", "Academy"),
                       ("PRESERVE", "Preserve"), ("DUNGEON", "Dungeon")):
        image("town:TOWN_" + town, "/UI/AdventureScreen-FPP-2/Arrows/" + name + ".(Texture).dds")
    image("town:TOWN_FORTRESS", "/UI/H5A1/Icons/Buildings/Towns/Fortress/Adventure_Mode_Icon/128x128/Fortress_lvl1.(Texture).dds")
    image("town:TOWN_STRONGHOLD", "/UI/H5A2/Icons/Adv_map_City/Stronghold.(Texture).dds")
    # Portraits are independent of the full Textures/_manifest.json import.
    for hero_id, hero in load_heroes(xdb).items():
        face = hero.get("faceSmall") or hero.get("face")
        if face:
            image("portrait:" + hero_id, face)
    # Campaign heroes are not in the random-map pools (Godric, Isabel, ...).
    # Keep an href lookup so a map hero can use its own portrait even without
    # a multiplayer-roster record or a localized name match.
    hero_files = sorted((Path(data_root) / "MapObjects").rglob("*.(AdvMapHeroShared).xdb"))
    for file in hero_files:
        href = "/" + file.relative_to(data_root).as_posix()
        node = xdb.load(href)
        if node is None:
            continue
        ref = node.find("FaceTextureSmall")
        if ref is None or not ref.get("href"):
            ref = node.find("FaceTexture")
        texture, texture_href = xdb.resolve(ref, href)
        face = texture_dds_path(xdb, texture, texture_href)
        if not face:
            continue
        parts = href.strip("/").split("/")
        hero_key = parts[1] + "/" + parts[-1].split(".")[0]
        key = "portrait:" + hero_key
        if key not in result["assets"]:
            image(key, face)
        result["heroPortraits"][href.lower()] = key
    (out / "skin.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return result
