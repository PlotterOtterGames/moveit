"""The Tundu logo, defined once as geometry and drawn both as SVG and as anti-aliased Pillow images.

The wordmark is "tundu" in one thick rounded stroke (no font: it's our own lettering). The last "u" is also a
little person cheering with both arms up and jumping (it floats a bit above the baseline): the u's stems are the
arms, the bowl is the body, and the dot between the arms is the head. Arms up is also the box's "start" gesture.

Units: stroke width = 1, baseline at y = 0, y points UP. Everything else in tools/branding uses this module.
"""

import math
from dataclasses import dataclass, field

from PIL import Image, ImageDraw

STROKE = 1.0
HALF = STROKE / 2
LETTERS = ("t", "u", "n", "d")
X_TOP = 3.5          # x-height (stroke centre line); outer top = 4.0
BASE = HALF          # baseline (stroke centre line); outer bottom = 0
GAP = 0.55           # space between letters (outer edges)
BOWL = 1.1           # radius of u / n bowls (centre line)


@dataclass
class Path:
    """A stroked centre line: straight lines and circular arcs, round caps and joins."""
    group: str       # the letter ("t", "u", "n", "d") or "figure"
    role: str        # colour role: "letters", "figure", "head"
    start: tuple
    cmds: list = field(default_factory=list)   # ("L", x, y) | ("A", cx, cy, r, a0, a1)

    def line(self, x, y):
        self.cmds.append(("L", x, y))
        return self

    def arc(self, cx, cy, r, a0, a1):
        """Arc around (cx, cy) from angle a0 to a1 (degrees, counter-clockwise when a1 > a0)."""
        self.cmds.append(("A", cx, cy, r, a0, a1))
        return self

    def points(self, step_deg=4.0):
        pts = [self.start]
        for c in self.cmds:
            if c[0] == "L":
                pts.append((c[1], c[2]))
            else:
                _, cx, cy, r, a0, a1 = c
                n = max(2, int(abs(a1 - a0) / step_deg))
                for i in range(1, n + 1):
                    a = math.radians(a0 + (a1 - a0) * i / n)
                    pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        return pts


@dataclass
class Dot:
    group: str
    role: str
    cx: float
    cy: float
    r: float


def wordmark(lift=0.5, arm_tilt_deg=16.0):
    """All elements of the full logo, plus its bounding box (x0, y0, x1, y1) including stroke width."""
    els = []
    # t: stem with a rounded foot, crossbar at x-height
    tx = 1.4
    els.append(Path("t", "letters", (tx, 4.9)).line(tx, 1.4).arc(tx + 0.9, 1.4, 0.9, 180, 270))
    els.append(Path("t", "letters", (tx - 0.9, X_TOP)).line(tx + 0.9, X_TOP))
    x = tx + 0.9 + HALF + GAP + HALF
    # u
    els.append(Path("u", "letters", (x, X_TOP)).line(x, BASE + BOWL).arc(x + BOWL, BASE + BOWL, BOWL, 180, 360)
               .line(x + 2 * BOWL, X_TOP))
    els.append(Path("u", "letters", (x + 2 * BOWL, BASE + BOWL)).line(x + 2 * BOWL, BASE))
    x += 2 * BOWL + HALF + GAP + HALF
    # n
    els.append(Path("n", "letters", (x, BASE)).line(x, X_TOP))
    els.append(Path("n", "letters", (x, X_TOP - BOWL)).arc(x + BOWL, X_TOP - BOWL, BOWL, 180, 0)
               .line(x + 2 * BOWL, BASE))
    x += 2 * BOWL + HALF + GAP
    # d: round bowl (x-height circle) + tall stem
    r_d = (X_TOP - BASE) / 2
    cx_d = x + HALF + r_d
    els.append(Path("d", "letters", (cx_d + r_d, BASE + r_d)).arc(cx_d, BASE + r_d, r_d, 0, 360))
    els.append(Path("d", "letters", (cx_d + r_d, 5.4)).line(cx_d + r_d, BASE))
    x = cx_d + r_d + HALF
    # the jumping figure "u": bowl = body, arms splayed up and out, head between the arms
    r_f = 1.2
    tilt = math.radians(arm_tilt_deg)
    arm = 2.6
    reach = r_f + arm * math.sin(tilt)
    cx_f = x + 0.45 + HALF + reach
    cy_f = BASE + r_f + lift
    left_tip = (cx_f - reach, cy_f + arm * math.cos(tilt))
    right_tip = (cx_f + reach, cy_f + arm * math.cos(tilt))
    els.append(Path("figure", "figure", left_tip).line(cx_f - r_f, cy_f).arc(cx_f, cy_f, r_f, 180, 360)
               .line(*right_tip))
    els.append(Dot("figure", "head", cx_f, cy_f + 1.95, 0.78))
    return els, bbox(els)


def figure_only(lift=0.0):
    els, _ = wordmark(lift=lift)
    return part(els, "figure")


def part(els, *groups):
    """The elements of some groups (e.g. one letter) and their bounding box."""
    els = [e for e in els if e.group in groups]
    return els, bbox(els)


def bbox(els):
    xs, ys = [], []
    for e in els:
        if isinstance(e, Dot):
            xs += [e.cx - e.r, e.cx + e.r]
            ys += [e.cy - e.r, e.cy + e.r]
        else:
            for px, py in e.points():
                xs += [px - HALF, px + HALF]
                ys += [py - HALF, py + HALF]
    return min(xs), min(ys), max(xs), max(ys)


# ---------------------------------------------------------------------------------------------------- SVG

def _svg_path(p, H):
    f = lambda v: f"{v:.3f}".rstrip("0").rstrip(".")
    d = [f"M{f(p.start[0])} {f(H - p.start[1])}"]
    cur = p.start
    for c in p.cmds:
        if c[0] == "L":
            d.append(f"L{f(c[1])} {f(H - c[2])}")
            cur = (c[1], c[2])
        else:
            _, cx, cy, r, a0, a1 = c
            # SVG arcs can't do a full circle in one command: split into <=180 degree pieces
            span = a1 - a0
            pieces = max(1, math.ceil(abs(span) / 180 - 1e-9))
            for i in range(1, pieces + 1):
                a = math.radians(a0 + span * i / pieces)
                ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
                sweep = 0 if span > 0 else 1   # counter-clockwise (y up) = sweep 0 in SVG's y-down space
                d.append(f"A{f(r)} {f(r)} 0 0 {sweep} {f(ex)} {f(H - ey)}")
                cur = (ex, ey)
    return " ".join(d)


def svg(els, box, colors, background=None, pad=0.6, corner=None):
    """colors: role -> '#rrggbb'. background: optional '#rrggbb' fill (rounded square when corner is given)."""
    x0, y0, x1, y1 = box
    w, h = x1 - x0 + 2 * pad, y1 - y0 + 2 * pad
    H = y1 + pad  # flip: svg_y = H - y
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x0 - pad:.3f} 0 {w:.3f} {h:.3f}" '
           f'width="{w * 40:.0f}" height="{h * 40:.0f}">']
    if background:
        rx = f' rx="{corner:.3f}"' if corner else ""
        out.append(f'<rect x="{x0 - pad:.3f}" y="0" width="{w:.3f}" height="{h:.3f}"{rx} fill="{background}"/>')
    def strokes(paths, role):
        if not paths:
            return []
        return ([f'<g fill="none" stroke="{colors[role]}" stroke-width="{STROKE}" stroke-linecap="round" '
                 f'stroke-linejoin="round">'] + [f'<path d="{_svg_path(p, H)}"/>' for p in paths] + ["</g>"])

    out += strokes([e for e in els if isinstance(e, Path) and e.group != "figure"], "letters")
    figure = [e for e in els if e.group == "figure"]
    if figure:
        # the jumping figure as its own group, so it can be animated (id="figure")
        out.append('<g id="figure">')
        out += strokes([e for e in figure if isinstance(e, Path)], "figure")
        out += [f'<circle cx="{e.cx:.3f}" cy="{H - e.cy:.3f}" r="{e.r:.3f}" fill="{colors[e.role]}"/>'
                for e in figure if isinstance(e, Dot)]
        out.append("</g>")
    out.append("</svg>")
    return "\n".join(out) + "\n"


# ---------------------------------------------------------------------------------------------------- raster

def _rgba(c, alpha=1.0):
    if isinstance(c, str):
        h = c.lstrip("#")
        c = tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
    return (*c[:3], round(255 * alpha))


def render(els, box, colors, height_px, pad=0.0, ss=4, groups=None, size=None, origin=None):
    """Anti-aliased RGBA image of the elements, `height_px` = height of `box` in pixels.
    groups: only draw these groups (e.g. set(LETTERS)); the image still covers the whole box, so separately
    rendered groups line up when composited. size/origin: render into a bigger canvas at origin (pixels)."""
    x0, y0, x1, y1 = box
    k = height_px / (y1 - y0)
    if size is None:
        size = (round((x1 - x0 + 2 * pad) * k), round((y1 - y0 + 2 * pad) * k))
    if origin is None:
        origin = (pad * k, pad * k)
    big = Image.new("RGBA", (size[0] * ss, size[1] * ss), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    to_px = lambda p: ((origin[0] + (p[0] - x0) * k) * ss, (origin[1] + (y1 - p[1]) * k) * ss)
    w = STROKE * k * ss
    for e in els:
        if groups and e.group not in groups:
            continue
        col = _rgba(colors[e.role])
        if isinstance(e, Dot):
            cx, cy = to_px((e.cx, e.cy))
            r = e.r * k * ss
            draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=col)
            continue
        # Each segment as a quad plus a round joint at every point (Pillow's wide lines leave hairline gaps on curves).
        pts = [to_px(p) for p in e.points(step_deg=3.0)]
        hw = w / 2
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            dx, dy = bx - ax, by - ay
            n = math.hypot(dx, dy)
            if n < 1e-6:
                continue
            nx, ny = -dy / n * hw, dx / n * hw
            draw.polygon([(ax + nx, ay + ny), (bx + nx, by + ny), (bx - nx, by - ny), (ax - nx, ay - ny)], fill=col)
        for px, py in pts:
            draw.ellipse((px - hw, py - hw, px + hw, py + hw), fill=col)
    return big.resize(size, Image.LANCZOS)
