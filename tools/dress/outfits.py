"""Vishu!'s wardrobe as code: every outfit piece drawn in the shared body template (draw.py shapes, style.py shading),
packed into one atlas for Unity, with a manifest that tells the game's rig how each piece follows the player.

    motion-service-py/.venv/Scripts/python tools/dress/outfits.py            # atlas + manifest
    motion-service-py/.venv/Scripts/python tools/dress/outfits.py --preview  # also tools/dress/out/*.png
    motion-service-py/.venv/Scripts/python tools/dress/outfits.py --only top_ hair_long   # a quick look (no atlas)

The items live in tops.py, bottoms.py, hair.py, heads.py, faces.py, backs.py, hands.py and doll.py (wardrobe.py holds
the registry and the shared parts; outfits_v1.py the first wardrobe, for items not redrawn yet). Then run
tools/dress/ui_art.py for the thumbnails.

Outputs (unity/MoveItGames/Assets/Games/Dress/Art):
    outfit_atlas.png       colour (RGBA): fixed colours, or a grey where the piece takes the chosen colour
    outfit_mask.png        tint mask (grey, half size): 1 = this pixel shows the chosen colour
    outfit_atlas.json      pieces: atlas rect, template origin + scale, rig, layer, sparkle points, ...

The shader (Tundu/Outfit Shade, style.shade_rgb) colours a tinted grey g: 0.8 = the colour, below it a deeper,
more saturated, slightly cool shade, above it toward white.

Rigs (Unity: Games/Dress/Scripts/OutfitRig.cs):
    torso   tops:     rows at the shoulders (y 0, half width 0.40) and the hips (y 1, 0.24), bilinear
    skirt   skirts:   the waist row (torso at y 0.8), then rigid down along the thighs (never pinched by the knees)
    tail    mermaid:  waist row, knees row, ankles row
    arm     sleeves:  own frame along the arm: y 0 shoulder, 0.64 elbow, 1.21 wrist; x across (torso lengths)
    head    hats:     a similarity from the eyes (template eyes at +-0.064, -0.47; head width 0.30)
    hair    wigs:     head, but below "hang" the hair hangs down (gravity) and sways
    face    glasses:  x from the two eyes, y from the head size
    back    wings:    shoulder centre + torso length + shoulder line; flaps around "pivot"
    cape    capes:    the shoulder row, then rigid down, the hem sways
    hand    held:     own frame, the grip at (0, 0), the item up (-y); mode along / upright / hang / float
    leg     the doll's legs: hip, knee, ankle
Layers, back to front: back (hidden where the body is), bottom, top (both hidden where the arms are), sleeve,
hairfront, head, face, hand.
"""
import argparse
import importlib
import json
import os
import sys
import time

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wardrobe  # noqa: E402
from wardrobe import ITEMS, PIECES  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Dress", "Art")
OUT = os.path.join(HERE, "out")

# item modules in drawing order; an item not found in them comes from the first wardrobe (outfits_v1.py)
MODULES = ["doll", "tops", "bottoms", "hair", "heads", "faces", "backs", "hands"]
ORDER = ["doll", "dollhair",
         "top_princess", "top_fairy", "top_mermaid", "top_hero", "top_winter", "top_party",
         "bottom_ballgown", "bottom_petals", "bottom_tail", "bottom_hero", "bottom_tutu", "bottom_winter",
         "hair_long", "hair_buns", "hair_braid", "hair_curly", "hair_ponytail", "hair_mermaid",
         "head_crown", "head_tiara", "head_flowers", "head_catears", "head_unicorn", "head_sunhat",
         "face_hearts", "face_stars", "face_sunglasses", "face_mask", "face_hero", "face_cheeks",
         "back_fairy", "back_butterfly", "back_angel", "back_cape", "back_cloak", "back_dragon",
         "hand_wand", "hand_bag", "hand_bouquet", "hand_mic", "hand_icecream", "hand_balloon"]


def item_functions():
    """{item id: drawing function}: the new modules first, the first wardrobe for the rest."""
    found = {}
    for name in MODULES:
        try:
            mod = importlib.import_module(name)
        except ModuleNotFoundError as e:
            if e.name != name:
                raise
            continue
        for f in getattr(mod, "ALL", []):
            found[f.__name__] = f
    missing = [i for i in ORDER if i not in found]
    if missing:
        import outfits_v1 as v1
        v1.piece = wardrobe.piece           # the old items register in the same atlas
        for i in missing:
            found[i] = getattr(v1, i)
    return found, missing


GAP = 16   # empty pixels between cells: mipmaps and filtering must not pull a neighbour's edge into a piece


def profile(pc, rgba, n=24):
    """The art's half width (template units) at n heights across the cell, smoothed: the cross-section the game wraps
    the piece around when the body turns (OutfitRig.Turn)."""
    a = rgba[..., 3].astype(np.float32) / 255
    h, w = a.shape
    xs = (np.arange(w) + 0.5 - pc.origin[0]) / pc.ppt
    y0 = (0.5 - pc.origin[1]) / pc.ppt
    y1 = (h - 0.5 - pc.origin[1]) / pc.ppt
    band = max(1, h // (2 * n))
    vals = []
    for k in range(n):
        yc = int(round(k / (n - 1) * (h - 1)))
        cols = a[max(0, yc - band):min(h, yc + band + 1)].max(0) > 0.15
        vals.append(float(np.abs(xs[cols]).max()) if cols.any() else 0.0)
    v = np.array(vals)
    v = np.maximum(v, np.maximum(np.roll(v, 1), np.roll(v, -1)))      # a little wider, never a dent
    v = np.convolve(np.pad(v, 2, mode="edge"), np.ones(5) / 5, mode="valid")
    v = np.maximum(v, 0.04)
    return [round(float(x), 4) for x in v], [round(y0, 4), round(y1, 4)]


def pack(pieces, width):
    """Shelf packing, tallest first, GAP apart. Returns {name: (x, y)} and the atlas height."""
    order = sorted(pieces, key=lambda p: (-p.h, -p.w))
    pos = {}
    x = y = shelf = 0
    for p in order:
        if x + p.w > width:
            x, y, shelf = 0, y + shelf + GAP, 0
        pos[p.name] = (x, y)
        x += p.w + GAP
        shelf = max(shelf, p.h)
    return pos, y + shelf


def build(preview, only=None):
    wardrobe.clear()
    funcs, legacy = item_functions()
    if legacy and not only:
        print(f"from the first wardrobe still: {', '.join(legacy)}")
    for item in ORDER:
        if only and item not in ("doll", "dollhair") and not any(item.startswith(o) for o in only):
            continue
        t0 = time.time()
        funcs[item]()
        print(f"  {item}: {time.time() - t0:.1f} s", flush=True)
    images = {p.name: p.image() for p in PIECES}
    for pc in PIECES:
        pc.meta["profile"], pc.meta["profileY"] = profile(pc, images[pc.name][0])
        a = images[pc.name][0][..., 3]
        edge = max(a[:2].max(), a[-2:].max(), a[:, :2].max(), a[:, -2:].max())
        if edge > 25:
            sides = [n for n, v in (("top", a[:2].max()), ("bottom", a[-2:].max()), ("left", a[:, :2].max()),
                                    ("right", a[:, -2:].max())) if v > 25]
            print(f"  WARNING {pc.name}: the art reaches its cell's {', '.join(sides)} edge (cut off): widen its bounds")
    if only:   # a quick look at some items: previews only, the atlas stays as it is
        os.makedirs(OUT, exist_ok=True)
        from preview import preview_all
        info = {"pieces": {p.name: {"origin": list(p.origin), "ppt": p.ppt, **p.meta} for p in PIECES}, "items": dict(ITEMS)}
        preview_all(images, info, OUT, items=[i for i in ORDER if i in ITEMS and i not in ("doll", "dollhair")])
        return
    width = 4096
    pos, height = pack(PIECES, width)
    used = height
    height = 1 << (height - 1).bit_length()
    print(f"atlas {width} x {height}, {used} rows used ({len(PIECES)} pieces, {len(ITEMS)} items)")
    atlas = np.zeros((height, width, 4), np.uint8)
    mask = np.zeros((height, width), np.uint8)
    manifest = {"atlas": [width, height], "pieces": {}, "items": dict(ITEMS)}
    for p in PIECES:
        x, y = pos[p.name]
        rgba, m = images[p.name]
        atlas[y:y + p.h, x:x + p.w] = rgba
        mask[y:y + p.h, x:x + p.w] = m
        meta = {k: v for k, v in p.meta.items()}
        manifest["pieces"][p.name] = {
            "rect": [x, y, p.w, p.h], "origin": [round(p.origin[0], 2), round(p.origin[1], 2)], "ppt": p.ppt, **meta}
    # one opaque white texel block in the bottom-right corner, for lines (a balloon's string)
    assert atlas[-8:, -8:, 3].max() == 0, "a piece reaches the atlas corner kept for the white texels"
    atlas[-8:, -8:] = 255
    mask[-8:, -8:] = 0
    os.makedirs(ART, exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(os.path.join(ART, "outfit_atlas.png"), optimize=True)
    Image.fromarray(mask, "L").resize((width // 2, height // 2), Image.LANCZOS).save(os.path.join(ART, "outfit_mask.png"))
    with open(os.path.join(ART, "outfit_atlas.json"), "w", encoding="utf-8") as f:
        json.dump(unity_manifest(manifest), f, indent=1)
    if preview:
        os.makedirs(OUT, exist_ok=True)
        from preview import preview_all
        preview_all(images, manifest, OUT)


def unity_manifest(manifest):
    """Lists instead of dictionaries, every field present (Unity's JsonUtility reads no dictionaries)."""
    pieces = []
    for name, info in manifest["pieces"].items():
        pieces.append({
            "name": name, "item": name.split(".")[0], "rig": info["rig"], "layer": info["layer"], "rect": info["rect"],
            "origin": info["origin"], "ppt": info["ppt"], "side": info.get("side", ""),
            "pivot": info.get("pivot", [0, 0]), "flap": info.get("flap", 0), "mode": info.get("mode", ""),
            "tip": info.get("tip", [0, 0]), "knot": info.get("knot", [0, 0]), "hang": info.get("hang", 99),
            "sparkles": [v for s in info.get("sparkles", []) for v in s],
            "profile": info.get("profile", []), "profileY": info.get("profileY", [0, 1])})
    return {"atlas": manifest["atlas"], "white": [manifest["atlas"][0] - 4, manifest["atlas"][1] - 4],
            "pieces": pieces, "items": [{"id": k, "pieces": v} for k, v in manifest["items"].items()]}


def load_manifest(path):
    """The Unity manifest back as {"atlas", "pieces": {name: info}, "items": {id: [names]}} (for ui_art.py)."""
    with open(path, encoding="utf-8") as f:
        m = json.load(f)
    return {"atlas": m["atlas"], "pieces": {p["name"]: p for p in m["pieces"]},
            "items": {i["id"]: i["pieces"] for i in m["items"]}}


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--only", nargs="*", help="draw only these items (name prefixes) for a quick preview")
    args = ap.parse_args()
    build(args.preview, args.only)


if __name__ == "__main__":
    main()
