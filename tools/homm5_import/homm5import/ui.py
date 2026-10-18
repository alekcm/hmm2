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
from .gamedata import load_heroes, model_geometry, texture_dds_path
from .localization import Localization
from .xdb import XDB


class UiXDB(XDB):
    """The expansion's layout overrides the base data where a file exists."""
    def __init__(self, data_root):
        super().__init__(data_root)
        self.overlay = XDB(str(Path(data_root).parent / "a2p1-data"))
        texts_root = Path(data_root).parent / "texts"
        self.localization = Localization(str(texts_root)) if texts_root.is_dir() else None

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
    result = {"version": 1, "assets": {}, "windows": {}, "heroPortraits": {},
              "spellbook": {},
              "source": "shipped XDB/DDS; a2p1 layout overrides"}
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

    def export_spellbook_desc():
        href = "/UI/SpellBook/SpellBook.(SpellBookDesc).xdb"
        node = xdb.load(href)
        if node is None:
            return

        def items(name):
            return [int(xdb.number(i)) for i in xdb.items(node.find(name))]

        def vec(name):
            value = node.find(name)
            return [float(xdb.number(value.find(axis))) for axis in ("x", "y", "z")]

        model, model_href = xdb.resolve(node.find("Model"), href)
        geometry, model_textures = model_geometry(xdb, model, model_href)
        result["spellbook"] = {
            "source": href,
            "model": model_href,
            "geometryUid": geometry,
            "modelTextures": model_textures,
            "firstPageParts": items("PartSpellsFirstPage"),
            "secondPageParts": items("PartSpellsSecondPage"),
            "schoolParts": items("PartSpellSchools"),
            "combatPart": int(xdb.number(node.find("PartSpellsCombat"))),
            "adventurePart": int(xdb.number(node.find("PartSpellsAdventure"))),
            "spellPointsPart": int(xdb.number(node.find("PartSP"))),
            "closePart": int(xdb.number(node.find("PartClose"))),
            "nextPagePart": int(xdb.number(node.find("PartNextPage"))),
            "prevPagePart": int(xdb.number(node.find("PartPrevPage"))),
            "center": vec("SpellbookCenterPosition"),
            "outlinerSize": vec("OutlinerSize"),
            "spellIcon": {
                axis: int(xdb.number(node.find("SpellIcon/" + axis)))
                for axis in ("x1", "y1", "x2", "y2")
            },
            "spellTextPosition": vec("SpellTextPosition"),
            "spellMaterial": vec("SpellMaterial"),
            "spellHighlightMaterial": vec("SpellHighlightMaterial"),
            "spellHighlightOverBright": float(xdb.number(node.find("SpellHighlightMaterialOverBright"))),
            "backFadeDuration": float(xdb.number(node.find("BackFadeDuration"))),
            "backFadeValue": float(xdb.number(node.find("BackFadeValue"))),
            "schoolHighlightColor": vec("SchoolHighlightColor"),
        }

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
        # WindowMSButton stores its caption in a TextFileRef relative to the
        # XDB record. Export the shipped localized value rather than replacing
        # it with an English runtime label. The relative href is resolved in
        # the same XDB directory rules as every other source reference.
        text_ref = node.find("TextFileRef")
        if text_ref is not None and text_ref.get("href") and xdb.localization is not None:
            text_href = xdb.absolutize(text_ref.get("href"), href).split("#", 1)[0]
            text = xdb.localization.read(text_href)
            if text is not None:
                data["text"] = text.strip()
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
    # MainMenu2 is a separate source tree from the adventure screen. Keep its
    # authored 1024x768 placement and button order in the same catalog so the
    # runtime does not stretch a code-built panel over the source layout.
    window("mainMenu:panel", "/UI/MainMenu2/Menu.(WindowSimple).xdb")
    window("mainMenu:logo", "/UI/MainMenu2/Logo.(WindowSimple).xdb")
    for name in ("SingleGame", "MultiPlayer", "Options", "Credits", "Quit"):
        window("mainMenu:" + name, "/UI/MainMenu2/" + name + ".(WindowMSButton).xdb")
    # The root buttons dispatch into these authored submenus. Export their
    # panels and every button placement as separate records instead of laying
    # the campaign/custom-game choices over the root menu by approximation.
    window("mainMenu:single:panel", "/UI/MainMenu2/SinglePlayer/MenuPanel.(WindowSimple).xdb")
    window("mainMenu:single:root", "/UI/MainMenu2/SinglePlayer/SinglePlayer.(WindowSimple).xdb")
    for name in ("Campaign", "CustomGame", "HallOfFame", "LoadGame", "UserCampaign", "Back"):
        window("mainMenu:single:" + name,
               "/UI/MainMenu2/SinglePlayer/" + name + ".(WindowMSButton).xdb")
    window("mainMenu:multi:panel", "/UI/MainMenu2/MultiPlayer/MenuPanel.(WindowSimple).xdb")
    window("mainMenu:multi:root", "/UI/MainMenu2/MultiPlayer/MultiPlayer.(WindowSimple).xdb")
    for name in ("HotSeat", "LAN", "LoadGame", "UbiCom", "Back"):
        window("mainMenu:multi:" + name,
               "/UI/MainMenu2/MultiPlayer/" + name + ".(WindowMSButton).xdb")
    for name in ("wood", "ore", "mercury", "crystals", "sulfur", "gems", "gold"):
        window("resource:" + name, "/UI/ResourceBar/resource-bar_(2)/" + name + "-icon.(WindowSimple).xdb")
        text_name = "crystal" if name == "crystals" else name
        window("value:" + name, "/UI/ResourceBar/resource-bar_(2)/" + text_name + "-text.(WindowStaticText).xdb")
    for key, path in (("menuPanel", "/UI/MainMenu2/Menu.(Texture).dds"),
                      ("menuLogo", "/UI/MainMenu2/Logo.(Texture).dds"),
                      ("rosterFrame", "/Textures/AdventureScreen-FPP-2/List-C.dds"),
                      # The book model's authored textures are the original
                      # adventure spellbook chrome. Back.tga is referenced by
                      # the legacy BackgroundSimpleTexture record but is not
                      # shipped as a loose file in this extraction.
                      ("spellbookBook", "/Textures/auto-imported_______320/Interface/Spellbook/Spellbook/book.dds"),
                      ("spellbookPage", "/Textures/auto-imported_______320/Interface/Spellbook/Spellbook/page.dds"),
                      ("spellbookPoints", "/Textures/auto-imported_______320/Interface/Spellbook/Spellbook/Spell_points.tga.dds"),
                      ("spellbookArrow", "/Textures/auto-imported_______320/Interface/Spellbook/Spellbook/Arrow.tga.dds")):
        image(key, path)
    export_spellbook_desc()
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
