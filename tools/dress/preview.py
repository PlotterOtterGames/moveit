"""Previews of Vishu!'s pieces: whole looks on the paper doll, coloured the way the game's shader colours them
(tools/dress/out/looks.png), and every item on its own (tools/dress/out/items.png)."""
import math
import os

import cv2
import numpy as np
from PIL import Image, ImageDraw

# template joints of the doll (torso lengths, y down), the arms hanging a little out
SHOULDER = (0.40, 0.0)
ELBOW = (0.50, 0.63)
WRIST = (0.55, 1.19)
HIP = (0.24, 1.0)
KNEE = (0.25, 1.92)
ANKLE = (0.25, 2.85)
GRIP = (0.57, 1.30)

LAYERS = ["back", "doll", "bottom", "top", "sleeve", "hairfront", "head", "face", "hand"]


def tint(rgba, mask, color):
    """The shader's colouring: tinted grey g -> colour * min(g / 0.8, 1), lighter greys toward white."""
    rgb = rgba[..., :3].astype(np.float32) / 255
    a = rgba[..., 3:].astype(np.float32) / 255
    m = cv2.resize(mask, (rgba.shape[1], rgba.shape[0]), interpolation=cv2.INTER_LINEAR).astype(np.float32)[..., None] / 255
    g = rgb[..., :1]
    c = np.asarray(color, np.float32)
    base = c * np.minimum(g / 0.8, 1.0)
    hi = np.clip((g - 0.8) / 0.2, 0, 1)
    tinted = base + (1 - base) * hi * 0.75
    out = rgb * (1 - m) + tinted * m
    return np.dstack([out, a])


def over(canvas, layer):
    a = layer[..., 3:]
    canvas[..., :3] = layer[..., :3] * a + canvas[..., :3] * (1 - a)
    canvas[..., 3:] = a + canvas[..., 3:] * (1 - a)


def place(canvas, img, info, origin, scale, angle=0.0, pivot=(0, 0), flip=False):
    """Draws a coloured piece (H x W x 4 float) so its template point `pivot` lands at canvas `origin`, scaled by
    `scale` canvas px per template unit, rotated by `angle` (radians, clockwise on screen)."""
    h, w = img.shape[:2]
    ox, oy = info["origin"]
    k = scale / info["ppt"]
    if flip:
        img = img[:, ::-1]
        ox = w - ox
        pivot = (-pivot[0], pivot[1])
    # piece px of the pivot
    px = ox + pivot[0] * info["ppt"]
    py = oy + pivot[1] * info["ppt"]
    c, s = math.cos(angle), math.sin(angle)
    M = np.array([[k * c, -k * s, 0], [k * s, k * c, 0]], np.float32)
    M[:, 2] = np.array(origin, np.float32) - M[:, :2] @ np.array([px, py], np.float32)
    layer = cv2.warpAffine(img, M, (canvas.shape[1], canvas.shape[0]), flags=cv2.INTER_LINEAR,
                           borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
    over(canvas, layer)


def bone_angle(a, b):
    """Rotation that turns the piece frame's +y onto the bone a -> b."""
    return math.atan2(-(b[0] - a[0]), b[1] - a[1])


def render_look(images, manifest, pieces, size=(560, 960), scale=210, top=0.95):
    """pieces: [(piece name, colour)]. Returns an RGBA float image."""
    canvas = np.zeros((size[1], size[0], 4), np.float32)
    cx = size[0] / 2
    to = lambda x, y: (cx + x * scale, (y + top) * scale)
    by_layer = sorted(pieces, key=lambda pc: LAYERS.index(manifest["pieces"][pc[0]]["layer"]))
    for name, color in by_layer:
        info = manifest["pieces"][name]
        rgba, mask = images[name]
        img = tint(rgba, mask, color)
        rig = info["rig"]
        if rig == "arm":
            for flip in (False, True):
                sx = -1 if flip else 1
                sh, el = (SHOULDER[0] * sx, SHOULDER[1]), (ELBOW[0] * sx, ELBOW[1])
                place(canvas, img, info, to(*sh), scale, bone_angle(sh, el), flip=flip)
        elif rig == "leg":
            for flip in (False, True):
                sx = -1 if flip else 1
                hp, kn = (HIP[0] * sx, HIP[1]), (KNEE[0] * sx, KNEE[1])
                place(canvas, img, info, to(*hp), scale, bone_angle(hp, kn), flip=flip)
        elif rig == "hand":
            place(canvas, img, info, to(*GRIP), scale, 0.0)
        elif info.get("side") == "both":     # wings: the drawn (right) one and its mirror image
            place(canvas, img, info, to(0, 0), scale)
            place(canvas, img, info, to(0, 0), scale, flip=True)
        else:
            place(canvas, img, info, to(0, 0), scale)
    return canvas


def to_image(rgba_f, bg=(0.93, 0.92, 0.97)):
    out = np.ones(rgba_f.shape[:2] + (3,), np.float32) * np.asarray(bg, np.float32)
    a = rgba_f[..., 3:]
    out = rgba_f[..., :3] + out * (1 - a)      # the canvas holds premultiplied colour (over() starts from black)
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8))


def doll_parts(manifest, items=()):
    """The doll, and her own bob while none of `items` is hair (as in the game)."""
    names = ["doll"] + ([] if any(i.startswith("hair_") for i in items) else ["dollhair"])
    return [(n, (1, 1, 1)) for i in names for n in manifest["items"].get(i, [])]


LOOKS = {
    "princess": [("back_cloak", "#7B4DDB"), ("top_princess", "#FF7EC8"), ("bottom_ballgown", "#FF7EC8"),
                 ("hair_long", "#F4C95D"), ("head_crown", "#FFC94A"), ("hand_wand", "#FFD23F")],
    "fairy": [("back_fairy", "#63D6A6"), ("top_fairy", "#63D6A6"), ("bottom_petals", "#FF7EC8"), ("hair_buns", "#B08CFF"),
              ("head_flowers", "#FF7EC8"), ("face_cheeks", "#FF7EC8"), ("hand_wand", "#B08CFF")],
    "mermaid": [("top_mermaid", "#2EC9D0"), ("bottom_tail", "#2EC9D0"), ("hair_mermaid", "#E37B3B")],
    "hero": [("back_cape", "#E8304A"), ("top_hero", "#3A6BFF"), ("bottom_hero", "#E8304A"), ("hair_ponytail", "#5B3A24"),
             ("face_hero", "#E8304A")],
    "summer": [("top_party", "#FFD23F"), ("bottom_petals", "#FF9F1C"), ("hair_curly", "#262030"), ("head_sunhat", "#FF6B5A"),
               ("face_sunglasses", "#FF7EC8"), ("hand_icecream", "#FF7EC8")],
    "winter": [("back_angel", "#F8F5FF"), ("top_winter", "#6EC3FF"), ("bottom_winter", "#6EC3FF"), ("hair_braid", "#F8F5FF"),
               ("head_tiara", "#D9DEE8")],
    "party": [("back_dragon", "#7B4DDB"), ("top_party", "#B08CFF"), ("bottom_tutu", "#FF7EC8"), ("hair_buns", "#FF7EC8"),
              ("head_catears", "#2E2638"), ("face_hearts", "#FF4F8B"), ("hand_mic", "#7B4DDB")],
    "unicorn": [("back_butterfly", "#B08CFF"), ("top_party", "#F8F5FF"), ("bottom_tutu", "#B08CFF"), ("hair_long", "#FF7EC8"),
                ("head_unicorn", "#FFC94A"), ("face_stars", "#FFD23F"), ("hand_balloon", "#E8304A"), ("hand_bag", "#FF7EC8")],
}


def preview_all(images, manifest, out_dir):
    hexc = lambda h: tuple(int(h.lstrip("#")[i:i + 2], 16) / 255 for i in (0, 2, 4))
    tiles = []
    for name, look in LOOKS.items():
        parts = doll_parts(manifest, [i for i, _ in look])
        for item, color in look:
            if item in manifest["items"]:
                parts += [(p, hexc(color)) for p in manifest["items"][item]]
        img = to_image(render_look(images, manifest, parts))
        ImageDraw.Draw(img).text((12, 10), name, fill=(60, 40, 70))
        tiles.append(img)
    sheet = Image.new("RGB", (sum(t.width for t in tiles), max(t.height for t in tiles)), (255, 255, 255))
    x = 0
    for t in tiles:
        sheet.paste(t, (x, 0))
        x += t.width
    sheet.save(os.path.join(out_dir, "looks.png"))

    # every item alone on the doll, in a neutral colour
    names = [i for i in manifest["items"] if i not in ("doll", "dollhair")]
    cols = 6
    tw, th = 280, 480
    grid = Image.new("RGB", (cols * tw, ((len(names) + cols - 1) // cols) * th), (255, 255, 255))
    for k, item in enumerate(names):
        parts = doll_parts(manifest, [item]) + [(p, hexc("#B08CFF")) for p in manifest["items"][item]]
        img = to_image(render_look(images, manifest, parts, size=(tw, th), scale=105, top=0.95))
        ImageDraw.Draw(img).text((8, 6), item, fill=(60, 40, 70))
        grid.paste(img, ((k % cols) * tw, (k // cols) * th))
    grid.save(os.path.join(out_dir, "items.png"))
    print(f"previews in {out_dir}")
