"""Generate the box's branding from unity/MoveItGames/Assets/Resources/brand.json (name, colors) and logo.py (the logo).

Outputs
    brand/logo/*.svg, *.png                           logo, mark and icon masters (vector + 2048 px), committed
    unity/MoveItGames/Assets/Branding/icon.png        app icon (512x512)
    unity/MoveItGames/Assets/Branding/tv_banner.png   Android TV banner (320x180)
    unity/MoveItGames/Assets/Branding/logo.png        logo for in-game screens (transparent, for dark backgrounds)
    unity/MoveItGames/Assets/Branding/mark.png        the jumping figure alone (transparent)
    tools/branding/out/bootanimation.zip              Android boot animation: the figure jumps in, the letters pop
                                                      in, then it keeps hopping until the box has booted
    tools/branding/out/logo.bmp                       first boot screen (bootloader logo) for the ODM / Rockchip image
    tools/branding/out/boot-preview.gif               the boot animation as a GIF, for reviews

Run with the motion service's Python (Pillow is already installed there):
    motion-service-py/.venv/Scripts/python tools/branding/make_branding.py [--size 1920x1080]
"""

import argparse
import io
import json
import math
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

import logo

ROOT = Path(__file__).resolve().parents[2]
BRAND_JSON = ROOT / "unity/MoveItGames/Assets/Resources/brand.json"
OUT = Path(__file__).resolve().parent / "out"
BRAND_DIR = ROOT / "brand/logo"
UNITY_BRANDING = ROOT / "unity/MoveItGames/Assets/Branding"

# Tagline font until the brand fonts (Fredoka / Nunito, OFL) are added to the repo.
FONT_CANDIDATES = [
    "C:/Windows/Fonts/segoeuib.ttf",
    "C:/Windows/Fonts/arialbd.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
]


def font(size):
    for path in FONT_CANDIDATES:
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    return ImageFont.load_default(size=size)


def rgb(hex_color):
    h = hex_color.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    """Blend color a toward b by t (0..1)."""
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def ease_out(t):
    t = max(0.0, min(1.0, t))
    return 1 - (1 - t) ** 3


def colors_on_dark(brand):
    return {"letters": brand["text"], "figure": brand["primary"], "head": brand["secondary"]}


def colors_on_light(brand):
    return {"letters": brand["background"], "figure": brand["secondary"], "head": brand["grape"]}


# ---------------------------------------------------------------------------------------------------- the logo scene

class Scene:
    """The wordmark split into sprites (one per letter + the figure) at one scale, so frames can animate them."""

    def __init__(self, brand, logo_width_px, colors):
        self.els, self.box = logo.wordmark()
        x0, y0, x1, y1 = self.box
        self.k = logo_width_px / (x1 - x0)          # pixels per logo unit
        self.size = (round((x1 - x0) * self.k), round((y1 - y0) * self.k))
        self.sprites = {}
        for group in (*logo.LETTERS, "figure"):
            els, box = logo.part(self.els, group)
            # the two u's share the group name: render each connected letter separately by x
            for i, (sub, sub_box) in enumerate(split_same_letter(els)):
                img = logo.render(sub, sub_box, colors, (sub_box[3] - sub_box[1]) * self.k)
                self.sprites[(group, i)] = (img, self.place(sub_box))

    def place(self, box):
        """Top-left pixel of a sub-box inside the logo image."""
        return (box[0] - self.box[0]) * self.k, (self.box[3] - box[3]) * self.k

    def draw(self, canvas, origin, letters=1.0, figure_dy=0.0, squash=0.0, letter_delay=0.18, figure_alpha=1.0):
        """letters: 0..1 progress of the letters popping in (one after another). figure_dy: figure offset in logo
        units (+ = up). squash: 0 = normal, + = squashed (landing), - = stretched (flying)."""
        keys = [k for k in self.sprites if k[0] != "figure"]
        keys.sort(key=lambda k: self.sprites[k][1][0])
        n = len(keys)
        for i, key in enumerate(keys):
            t = (letters - i * letter_delay) / max(1e-6, 1 - (n - 1) * letter_delay)
            t = max(0.0, min(1.0, t))
            if t <= 0:
                continue
            img, (px, py) = self.sprites[key]
            s = 0.4 + 0.6 * ease_out(t) + 0.12 * math.sin(math.pi * t)   # pops slightly past full size
            paste_scaled(canvas, img, origin[0] + px, origin[1] + py, s, s, min(1.0, t * 2.5), anchor="bottom")
        img, (px, py) = self.sprites[("figure", 0)]
        sx, sy = 1 + 0.18 * squash, 1 - 0.22 * squash
        paste_scaled(canvas, img, origin[0] + px, origin[1] + py - figure_dy * self.k, sx, sy, figure_alpha,
                     anchor="bottom")

    def shadow(self, canvas, origin, height_units, color, alpha):
        """Oval shadow under the figure: smaller and fainter the higher it is."""
        img, (px, py) = self.sprites[("figure", 0)]
        cx = origin[0] + px + img.width / 2
        gy = origin[1] + self.size[1] - logo.HALF * self.k * 0.2
        f = 1 / (1 + 0.45 * max(0.0, height_units))
        w, h = img.width * 0.62 * f, self.k * 0.32 * f
        layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).ellipse((cx - w / 2, gy - h / 2, cx + w / 2, gy + h / 2),
                                      fill=(*color, round(255 * alpha * f)))
        canvas.alpha_composite(layer)


def split_same_letter(els):
    """Groups elements into connected letters (the two u's share a group name) by overlapping x ranges."""
    items = sorted(els, key=lambda e: logo.bbox([e])[0])
    groups = []
    for e in items:
        b = logo.bbox([e])
        if groups and b[0] < groups[-1][1][2]:
            g, gb = groups[-1]
            g.append(e)
            groups[-1] = (g, logo.bbox(g))
        else:
            groups.append(([e], b))
    return groups


def paste_scaled(canvas, img, x, y, sx, sy, alpha=1.0, anchor="bottom"):
    w, h = max(1, round(img.width * sx)), max(1, round(img.height * sy))
    scaled = img.resize((w, h), Image.LANCZOS) if (w, h) != img.size else img
    if alpha < 1.0:
        a = scaled.getchannel("A").point(lambda v: round(v * alpha))
        scaled = scaled.copy()
        scaled.putalpha(a)
    cx = x + img.width / 2
    if anchor == "bottom":
        top = y + img.height - h
    else:
        top = y + (img.height - h) / 2
    canvas.alpha_composite(scaled, (round(cx - w / 2), round(top)))


# ---------------------------------------------------------------------------------------------------- boot animation

def boot_frame(brand, size, scene, letters=1.0, figure_dy=0.0, squash=0.0, tagline=1.0, dots_phase=None,
               figure_alpha=1.0):
    w, h = size
    bg, text = rgb(brand["background"]), rgb(brand["text"])
    img = Image.new("RGBA", size, (*bg, 255))
    origin = ((w - scene.size[0]) / 2, h * 0.43 - scene.size[1] / 2)
    if figure_alpha > 0:
        scene.shadow(img, origin, figure_dy + 0.5, (0, 0, 0), 0.35 * figure_alpha)
    scene.draw(img, origin, letters=letters, figure_dy=figure_dy, squash=squash, figure_alpha=figure_alpha)
    draw = ImageDraw.Draw(img)
    if tagline > 0:
        fnt = font(int(h * 0.045))
        l, t, r, b = draw.textbbox((0, 0), brand["taglineSq"], font=fnt)
        draw.text((w / 2 - (r - l) / 2 - l, h * 0.66 - (b - t) / 2 - t), brand["taglineSq"], font=fnt,
                  fill=mix(bg, rgb(brand["textMuted"]), tagline))
    if dots_phase is not None:
        r = h * 0.011
        for i in range(3):
            k = max(0.0, 1.0 - abs(((dots_phase * 3) % 3) - i))   # each dot brightens in turn
            cx, cy = w / 2 + (i - 1) * r * 4, h * 0.78
            draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=mix(bg, rgb(brand["primary"]), 0.25 + 0.75 * k))
    return img.convert("RGB")


def boot_frames(brand, size):
    scene = Scene(brand, size[0] * 0.5, colors_on_dark(brand))
    intro = []
    # 0-13: the figure falls in from above (gravity); 14-19: lands and squashes; 20-29: springs back to its jump pose
    for i in range(48):
        if i < 14:
            t = i / 13
            dy, squash, alpha = 9 * (1 - t * t), -0.25 * t, min(1.0, i / 4)
        elif i < 20:
            t = (i - 14) / 5
            dy, squash, alpha = -0.5 * math.sin(math.pi * t) * 0.6, 0.9 * math.sin(math.pi * t), 1.0
        elif i < 30:
            t = (i - 20) / 9
            dy, squash, alpha = 0.6 * math.sin(math.pi * t) * (1 - t) * 1.6, -0.15 * math.sin(math.pi * t), 1.0
        else:
            dy, squash, alpha = 0.0, 0.0, 1.0
        letters = max(0.0, min(1.0, (i - 15) / 20))
        tagline = max(0.0, min(1.0, (i - 32) / 12))
        intro.append(boot_frame(brand, size, scene, letters=letters, figure_dy=dy, squash=squash, tagline=tagline,
                                figure_alpha=alpha))
    # loop (1 s): a small hop with a little squash on take-off and landing, loading dots underneath
    loop = []
    for i in range(30):
        t = i / 30
        hop = max(0.0, math.sin(2 * math.pi * t)) * 0.55
        squash = 0.35 * max(0.0, -math.sin(2 * math.pi * t)) ** 2
        loop.append(boot_frame(brand, size, scene, figure_dy=hop, squash=squash, dots_phase=t))
    return intro, loop


def bootanimation(intro, loop, size, fps=30):
    """Android bootanimation.zip: part0 plays once (intro), part1 loops until boot completes. Must be STORED."""
    path = OUT / "bootanimation.zip"
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_STORED) as z:
        z.writestr("desc.txt", f"{size[0]} {size[1]} {fps}\np 1 0 part0\np 0 0 part1\n")
        for part, frames in (("part0", intro), ("part1", loop)):
            for i, img in enumerate(frames):
                buf = io.BytesIO()
                img.save(buf, "PNG", optimize=True)
                z.writestr(f"{part}/{i:05d}.png", buf.getvalue())
    return path


def preview_gif(intro, loop, path, width=640):
    frames = [f.resize((width, round(f.height * width / f.width)), Image.LANCZOS) for f in intro + loop * 3]
    frames[0].save(path, save_all=True, append_images=frames[1:], duration=33, loop=0, optimize=True)


# ---------------------------------------------------------------------------------------------------- icon, banner

def icon_image(brand, size=512):
    """Sunny rounded square with the jumping figure and its shadow."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=int(size * 0.22), fill=rgb(brand["primary"]))
    els, box = logo.figure_only()
    fig = logo.render(els, box, {"figure": brand["background"], "head": brand["secondary"]}, size * 0.56)
    x, y = (size - fig.width) // 2, int(size * 0.13)
    sw, sy = fig.width * 0.55, y + fig.height + size * 0.075
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse((size / 2 - sw / 2, sy - size * 0.025, size / 2 + sw / 2, sy + size * 0.025),
                                   fill=(*rgb(brand["background"]), 70))
    img.alpha_composite(shadow)
    img.alpha_composite(fig, (x, y))
    return img


def icon_svg(brand):
    els, box = logo.figure_only()
    x0, y0, x1, y1 = box
    side = (y1 - y0) / 0.56
    cx = (x0 + x1) / 2
    top = y1 + side * 0.13          # icon top edge in logo units (y up)
    left = cx - side / 2
    shadow_y = y0 - side * 0.075
    H = top
    body = logo.svg(els, box, {"figure": brand["background"], "head": brand["secondary"]})
    inner = body[body.index(">") + 1: body.rindex("</svg>")]
    # re-anchor: logo.svg flips y with its own H; shift so both agree on our H
    shift = H - (y1 + 0.6)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{left:.3f} 0 {side:.3f} {side:.3f}" width="1024" '
            f'height="1024">\n<rect x="{left:.3f}" y="0" width="{side:.3f}" height="{side:.3f}" rx="{side * 0.22:.3f}" '
            f'fill="{brand["primary"]}"/>\n'
            f'<ellipse cx="{cx:.3f}" cy="{H - shadow_y:.3f}" rx="{(x1 - x0) * 0.55 / 2:.3f}" ry="{side * 0.025:.3f}" '
            f'fill="{brand["background"]}" fill-opacity="0.27"/>\n'
            f'<g transform="translate(0 {shift:.3f})">{inner}</g>\n</svg>\n')


def banner(brand, size=(320, 180)):
    img = Image.new("RGBA", size, (*rgb(brand["background"]), 255))
    els, box = logo.wordmark()
    mark = logo.render(els, box, colors_on_dark(brand), size[1] * 0.42)
    img.alpha_composite(mark, ((size[0] - mark.width) // 2, (size[1] - mark.height) // 2))
    return img.convert("RGB")


# ---------------------------------------------------------------------------------------------------- main

def export_logo_files(brand):
    BRAND_DIR.mkdir(parents=True, exist_ok=True)
    els, box = logo.wordmark()
    fig, fig_box = logo.figure_only()
    variants = {
        "tundu-logo-on-dark": colors_on_dark(brand),
        "tundu-logo-on-light": colors_on_light(brand),
        "tundu-logo-white": {"letters": "#FFFFFF", "figure": "#FFFFFF", "head": "#FFFFFF"},
        "tundu-logo-night": {r: brand["background"] for r in ("letters", "figure", "head")},
    }
    written = []
    for name, colors in variants.items():
        (BRAND_DIR / f"{name}.svg").write_text(logo.svg(els, box, colors), encoding="utf-8")
        logo.render(els, box, colors, 2048 * (box[3] - box[1]) / (box[2] - box[0]), pad=0.6).save(BRAND_DIR / f"{name}.png")
        written.append(name)
    mark_colors = {"figure": brand["primary"], "head": brand["secondary"]}
    (BRAND_DIR / "tundu-mark.svg").write_text(logo.svg(fig, fig_box, mark_colors), encoding="utf-8")
    logo.render(fig, fig_box, mark_colors, 1024, pad=0.3).save(BRAND_DIR / "tundu-mark.png")
    (BRAND_DIR / "tundu-icon.svg").write_text(icon_svg(brand), encoding="utf-8")
    icon_image(brand, 1024).save(BRAND_DIR / "tundu-icon.png")
    return written + ["tundu-mark", "tundu-icon"]


def main():
    p = argparse.ArgumentParser(description="Generate logo files, boot animation, boot logo, icon and TV banner")
    p.add_argument("--size", default="1280x720", help="boot animation / logo resolution, e.g. 1920x1080")
    args = p.parse_args()
    size = tuple(int(v) for v in args.size.lower().split("x"))

    brand = json.loads(BRAND_JSON.read_text(encoding="utf-8"))
    OUT.mkdir(parents=True, exist_ok=True)
    UNITY_BRANDING.mkdir(parents=True, exist_ok=True)

    files = export_logo_files(brand)
    intro, loop = boot_frames(brand, size)
    zip_path = bootanimation(intro, loop, size)
    preview_gif(intro, loop, OUT / "boot-preview.gif")
    intro[-1].save(OUT / "logo.bmp")  # 24-bit BMP, what Rockchip's bootloader logo expects
    intro[-1].save(OUT / "preview.png")
    icon_image(brand).save(UNITY_BRANDING / "icon.png")
    banner(brand).save(UNITY_BRANDING / "tv_banner.png")
    els, box = logo.wordmark()
    logo.render(els, box, colors_on_dark(brand), 1600 * (box[3] - box[1]) / (box[2] - box[0])).save(UNITY_BRANDING / "logo.png")
    fig, fig_box = logo.figure_only()
    logo.render(fig, fig_box, {"figure": brand["primary"], "head": brand["secondary"]}, 512).save(UNITY_BRANDING / "mark.png")

    print(f"Brand: {brand['name']}  ({brand['primary']} on {brand['background']})")
    print(f"  {BRAND_DIR}: {', '.join(files)} (.svg + .png)")
    print(f"  {zip_path}  ({zip_path.stat().st_size / 1024:.0f} KB), boot-preview.gif, logo.bmp")
    print(f"  {UNITY_BRANDING}: icon.png, tv_banner.png, logo.png, mark.png")


if __name__ == "__main__":
    main()
