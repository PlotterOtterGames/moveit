"""Pikturo!'s colouring pages, drawn as code: black line art on a transparent background, 1408x1056 (4:3, the shape
of Pikturo's paper since 2026-11-10), written to unity/MoveItGames/Assets/Resources/Paint/Pages/<name>.png (Unity
imports them alpha-only, see PaintPageImport.cs).

    motion-service-py/.venv/Scripts/python tools/paint/pages.py            # all pages
    motion-service-py/.venv/Scripts/python tools/paint/pages.py dino       # pages whose name contains "dino"
    ... --preview                                                          # also previews in tools/paint/out/

Your own pages work too: any 4:3 PNG with dark lines on a transparent background in that folder. Keep the outlines
closed: the game colours one area at a time (the paint stays inside the lines, an area that is mostly coloured fills
itself, PageRegions.cs), and counts the picture as finished when every area is done. Few, big areas suit a hand in the
air: an area narrower than ~60 px of the page is hard to colour.

Figures are drawn as filled shapes and outlined by morphology (dilate minus erode), so overlapping body parts give
one clean outer line; a figure hides the lines behind it (the ground line doesn't run through a foot). Details (eyes,
spots, stripes) are drawn as lines on top. Everything is drawn at 2x and scaled down, for smooth lines.

--preview writes <name>.png (the page on white) and <name>_areas.png (every area the game will find in its own
colour, the small ones hatched red: check that no outline leaks) and prints each page's areas.
"""

import argparse
import math
import os

import cv2
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "unity", "MoveItGames", "Assets", "Resources", "Paint", "Pages")
PREVIEW_DIR = os.path.join(os.path.dirname(__file__), "out")
W, H = 1408, 1056
S = 2                    # supersampling
LINE = 12                # line width in final pixels (9 on the screen: the paper shows the page at 3/4)
THIN = 9
INK = (36, 30, 30)       # BGR of the lines (only alpha is used by the game)
CANVAS = (1056, 792)     # the game's paper, for the area preview
SMALL_AREA = 1200        # canvas pixels: smaller areas don't count toward "finished" (PaintGame.countArea)


class Page:
    def __init__(self):
        self.mask = np.zeros((H * S, W * S), np.uint8)
        self.k, self.ox, self.oy = 1.0, 0.0, 0.0

    def view(self, k=1.0, ox=0.0, oy=0.0):
        """What follows is drawn scaled by k and moved by (ox, oy): x' = x * k + ox."""
        self.k, self.ox, self.oy = k, ox, oy
        return self

    def p(self, pt):
        return int(round((pt[0] * self.k + self.ox) * S)), int(round((pt[1] * self.k + self.oy) * S))

    def r(self, v):
        return max(1, int(round(v * self.k * S)))

    # ---- details: plain lines
    def line(self, pts, closed=False, width=LINE):
        pts = np.array([self.p(q) for q in pts], np.int32)
        cv2.polylines(self.mask, [pts], closed, 255, width * S, cv2.LINE_AA)

    def circle(self, c, r, width=LINE):
        cv2.circle(self.mask, self.p(c), self.r(r), 255, width * S, cv2.LINE_AA)

    def dot(self, c, r):
        cv2.circle(self.mask, self.p(c), self.r(r), 255, -1, cv2.LINE_AA)

    def ellipse(self, c, axes, angle=0, start=0, end=360, width=LINE):
        cv2.ellipse(self.mask, self.p(c), (self.r(axes[0]), self.r(axes[1])), angle, start, end, 255, width * S, cv2.LINE_AA)

    # ---- figures: filled shapes, outlined together
    def figure(self, draw, width=LINE, opaque=True):
        """draw(Shape) adds filled parts; the union's outline is added to the page. An opaque figure hides the lines
        already drawn behind it."""
        shape = Shape(self)
        draw(shape)
        r = width * S // 2
        k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))
        if opaque:
            self.mask[cv2.erode(shape.mask, k) > 0] = 0
        outline = cv2.subtract(cv2.dilate(shape.mask, k), cv2.erode(shape.mask, k))
        self.mask = np.maximum(self.mask, outline)
        return shape

    def inside(self, shape, draw):
        """Details drawn by draw(page) only where they fall inside the figure (e.g. stripes on a fish)."""
        saved = self.mask
        self.mask = np.zeros_like(saved)
        draw(self)
        k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (LINE * S, LINE * S))
        self.mask = np.maximum(saved, cv2.min(self.mask, cv2.erode(shape.mask, k)))

    def alpha(self):
        return cv2.resize(self.mask, (W, H), interpolation=cv2.INTER_AREA)

    def save(self, name):
        img = np.zeros((H, W, 4), np.uint8)
        img[..., :3] = INK
        img[..., 3] = self.alpha()
        os.makedirs(OUT_DIR, exist_ok=True)
        path = os.path.join(OUT_DIR, f"{name}.png")
        cv2.imwrite(path, img)
        return path


class Shape:
    def __init__(self, page):
        self.page = page
        self.mask = np.zeros((H * S, W * S), np.uint8)

    def ellipse(self, c, axes, angle=0):
        cv2.ellipse(self.mask, self.page.p(c), (self.page.r(axes[0]), self.page.r(axes[1])), angle, 0, 360, 255, -1, cv2.LINE_AA)
        return self

    def circle(self, c, r):
        return self.ellipse(c, (r, r))

    def poly(self, pts):
        cv2.fillPoly(self.mask, [np.array([self.page.p(q) for q in pts], np.int32)], 255, cv2.LINE_AA)
        return self

    def thick(self, pts, width):
        cv2.polylines(self.mask, [np.array([self.page.p(q) for q in pts], np.int32)], False, 255, self.page.r(width), cv2.LINE_AA)
        return self

    def rect(self, x0, y0, x1, y1, r=0):
        if r <= 0:
            return self.poly([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])
        self.poly([(x0 + r, y0), (x1 - r, y0), (x1, y0 + r), (x1, y1 - r), (x1 - r, y1), (x0 + r, y1), (x0, y1 - r), (x0, y0 + r)])
        for cx, cy in ((x0 + r, y0 + r), (x1 - r, y0 + r), (x1 - r, y1 - r), (x0 + r, y1 - r)):
            self.circle((cx, cy), r)
        return self


def bezier(p0, p1, p2, p3, n=40):
    out = []
    for i in range(n + 1):
        t = i / n
        a, b, c, d = (1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t * t, t ** 3
        out.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return out


def star_points(c, outer, inner, n=5, rot=-90):
    pts = []
    for i in range(2 * n):
        r = outer if i % 2 == 0 else inner
        a = math.radians(rot + i * 180 / n)
        pts.append((c[0] + r * math.cos(a), c[1] + r * math.sin(a)))
    return pts


def flower(page, c, r, petals=5, rot=0):
    """A flower head: petals around a centre (separate areas to colour)."""
    def head(s):
        for k in range(petals):
            a = math.radians(rot + k * 360 / petals)
            s.circle((c[0] + r * 0.62 * math.cos(a), c[1] + r * 0.62 * math.sin(a)), r * 0.45)
        s.circle(c, r * 0.4)
    page.figure(head)
    page.circle(c, r * 0.32)


def ground(page, y):
    """The ground line, from edge to edge of the page (so the sky and the ground are two areas)."""
    k, ox, oy = page.k, page.ox, page.oy
    yy = y * k + oy
    page.view()
    page.line([(-20, yy), (W + 20, yy)])
    page.view(k, ox, oy)


def sun(page, c, r, rays=10, face=True):
    page.figure(lambda s: s.circle(c, r))
    for i in range(rays):
        a = math.radians(i * 360 / rays + 9)
        page.line([(c[0] + (r + 28) * math.cos(a), c[1] + (r + 28) * math.sin(a)),
                   (c[0] + (r + 72) * math.cos(a), c[1] + (r + 72) * math.sin(a))])
    if face:
        page.dot((c[0] - r * 0.33, c[1] - r * 0.15), r * 0.1)
        page.dot((c[0] + r * 0.33, c[1] - r * 0.15), r * 0.1)
        page.line(bezier((c[0] - r * 0.4, c[1] + r * 0.28), (c[0] - r * 0.15, c[1] + r * 0.58),
                         (c[0] + r * 0.15, c[1] + r * 0.58), (c[0] + r * 0.4, c[1] + r * 0.28), 12))


def cloud(page, c, k=1.0):
    page.figure(lambda s: [s.circle((c[0] + dx * k, c[1] + dy * k), r * k)
                           for dx, dy, r in ((-85, 20, 62), (0, -18, 84), (88, 20, 62), (0, 42, 62))])


# ---------------------------------------------------------------------------------------------- pages


def dino(page):
    """Tundu's dino in its world: a chunky T-rex smiling, its egg, a volcano with smoke, the sun and clouds."""
    sun(page, (170, 180), 86)
    cloud(page, (560, 200), 0.95)
    cloud(page, (930, 130), 0.8)
    page.view(0.92, 704 - 972 * 0.92, 930 - 900 * 0.92)
    ground(page, 900)
    # volcano behind, with a smoke cloud
    page.figure(lambda s: s.poly([(1330, 900), (1470, 510), (1570, 510), (1710, 900)]))
    page.ellipse((1520, 512), (50, 16), 0, 0, 180)                      # crater lip
    page.line([(1490, 525), (1478, 600), (1505, 640), (1495, 700)])      # lava
    page.figure(lambda s: [s.circle((1520 + dx, 420 + dy), 52) for dx, dy in ((-55, 25), (15, -10), (80, 30), (10, 50))])
    page.figure(lambda s: s.ellipse((300, 830), (66, 84)))               # egg with a zigzag crack
    page.line([(232, 822), (262, 800), (284, 826), (305, 800), (330, 826), (368, 804)])

    def body(s):
        s.ellipse((900, 700), (240, 175))                               # body
        s.ellipse((1080, 500), (90, 150), -20)                          # neck
        s.ellipse((1170, 375), (145, 110), -5)                          # head
        s.ellipse((1230, 430), (105, 58), 0)                            # snout
        s.poly(bezier((700, 600), (560, 620), (470, 720), (400, 850), 30)
               + bezier((400, 850), (520, 810), (620, 810), (730, 800), 30))   # tail
        s.ellipse((790, 862), (75, 55))                                 # back leg
        s.ellipse((1005, 862), (80, 55))                                # front leg
        s.ellipse((1110, 650), (62, 26), 35)                            # little arm
        for i, x in enumerate(range(720, 1060, 70)):                    # spikes on the back
            y = 700 - 175 * math.sqrt(max(0.0, 1 - ((x - 900) / 240) ** 2))
            s.poly([(x - 34, y + 30), (x, y - 50), (x + 34, y + 30)])
    page.figure(body)
    page.ellipse((960, 745), (150, 100), 0, 10, 170)                     # belly
    page.dot((1190, 345), 22)                                            # eye
    page.line(bezier((1150, 445), (1200, 480), (1265, 478), (1315, 440), 20))   # smile
    page.dot((1300, 382), 7)                                             # nostril
    for x in (760, 790, 820, 975, 1005, 1035):                           # toes
        page.line([(x, 912), (x, 892)], width=THIN)
    for x in (560, 1250):                                                # grass tufts
        page.line([(x - 30, 900), (x - 40, 850)])
        page.line([(x, 900), (x, 835)])
        page.line([(x + 30, 900), (x + 40, 850)])


def house(page):
    """A house with a sun, clouds, a tree and flowers."""
    cloud(page, (700, 150), 0.8)
    page.view(0.97, 704 - 925 * 0.97, 960 - 940 * 0.97)
    ground(page, 940)
    # sun with rays
    sun(page, (420, 300), 85)
    # cloud
    page.figure(lambda s: [s.circle(c, r) for c, r in (((1250, 260), 70), ((1330, 220), 90), ((1420, 260), 70), ((1330, 290), 70))])
    # tree
    page.figure(lambda s: s.rect(1380, 700, 1440, 940))
    page.figure(lambda s: [s.circle(c, r) for c, r in (((1410, 600), 130), ((1320, 650), 90), ((1500, 650), 90), ((1410, 500), 100))])
    for c in ((1360, 590), (1460, 560), (1420, 660)):
        page.circle(c, 22)                                               # apples
    page.figure(lambda s: s.poly([(520, 580), (810, 330), (1100, 580)]).rect(940, 360, 1000, 470))   # roof + chimney
    page.figure(lambda s: s.rect(560, 572, 1060, 940))                  # walls
    page.figure(lambda s: s.rect(760, 740, 870, 940, 10))               # door
    page.dot((850, 845), 9)
    for x0 in (600, 910):                                                # windows (one area each: a cross would cut
        page.figure(lambda s, x0=x0: s.rect(x0, 630, x0 + 120, 740, 8))   # them into panes too small for a hand)
        page.line([(x0 - 12, 752), (x0 + 132, 752)], width=THIN)         # sill
    page.figure(lambda s: s.rect(775, 445, 845, 515, 35))                # round attic window
    # flowers along the ground
    for x in (400, 1220):
        page.line([(x, 940), (x, 830)])
        flower(page, (x, 795), 48)


def fish(page):
    """Under the sea: a big fish, a small fish, bubbles, seaweed, a starfish, a shell."""
    page.line(bezier((-20, 118), (350, 60), (700, 170), (1060, 105), 60) + bezier((1060, 105), (1200, 80), (1320, 130), (1430, 100), 30))   # waves
    page.view(0.93, 704 - 960 * 0.93, 985 - 960 * 0.93)
    page.line(bezier((-200, 960), (500, 930), (1300, 990), (2100, 950), 60))   # sand
    for x in (300, 400, 1650):                                           # seaweed
        page.figure(lambda s, x=x: s.thick(bezier((x, 975), (x - 60, 830), (x + 60, 710), (x, 570), 30), 40))

    def big(s):
        s.ellipse((880, 520), (300, 190))
        s.poly([(1130, 520), (1330, 360), (1300, 520), (1330, 680)])    # tail
        s.poly([(780, 340), (900, 230), (980, 345)])                     # top fin
    body = page.figure(big)
    page.inside(body, lambda pg: [pg.line(bezier((x, 300), (x + 40, 450), (x + 40, 590), (x, 740), 20)) for x in (820, 930, 1040)]
                and pg.line(bezier((700, 340), (650, 450), (650, 600), (700, 710), 20)))   # stripes, gill
    page.figure(lambda s: s.circle((640, 470), 42))                     # eye
    page.dot((650, 465), 18)
    page.line(bezier((600, 580), (620, 600), (650, 600), (670, 585), 10))      # smile

    def small(s):
        s.ellipse((1450, 760), (110, 65))
        s.poly([(1550, 760), (1640, 690), (1625, 760), (1640, 830)])
    page.figure(small)
    page.dot((1390, 745), 12)
    for c, r in (((560, 300), 42), ((505, 205), 30), ((1500, 590), 38), ((1455, 495), 28), ((1180, 250), 36)):
        page.circle(c, r)                                                # bubbles
    page.figure(lambda s: s.poly(star_points((580, 870), 86, 40)))       # starfish
    page.figure(lambda s: s.ellipse((1150, 905), (95, 62)))              # shell
    for k in range(-2, 3):
        page.line([(1150, 958), (1150 + k * 40, 852 + abs(k) * 10)], width=THIN)


def butterfly(page):
    """A big butterfly over flowers, the sun and a cloud."""
    sun(page, (175, 175), 82)
    cloud(page, (1180, 150), 0.85)
    page.view(0.95, 704 - 960 * 0.95, 975 - 960 * 0.95)
    ground(page, 960)
    for x, h in ((330, 260), (520, 180), (1400, 200), (1600, 270)):      # flowers
        page.line([(x, 960), (x, 960 - h)])
        page.figure(lambda s, x=x, h=h: s.ellipse((x + 48, 960 - h * 0.45), (48, 20), -30))   # leaf
        flower(page, (x, 960 - h - 40), 62, petals=6, rot=15)

    for cx, tilt in ((760, -30), (1160, 30)):                             # four wings, each its own area: the lower
        page.figure(lambda s, cx=cx, tilt=tilt: s.ellipse((cx, 400), (190, 150), tilt))   # ones lie over the upper ones
    for cx, tilt in ((800, 25), (1120, -25)):
        page.figure(lambda s, cx=cx, tilt=tilt: s.ellipse((cx, 640), (130, 110), tilt))
    page.figure(lambda s: s.ellipse((960, 520), (40, 190)))              # body
    page.figure(lambda s: s.circle((960, 310), 48))                      # head
    page.dot((942, 300), 9)
    page.dot((978, 300), 9)
    page.line(bezier((935, 330), (950, 345), (970, 345), (985, 330), 10), width=THIN)
    page.line(bezier((940, 270), (920, 210), (880, 180), (850, 175), 20))     # antennae
    page.line(bezier((980, 270), (1000, 210), (1040, 180), (1070, 175), 20))
    page.dot((850, 175), 14)
    page.dot((1070, 175), 14)
    for c, r in (((740, 390), 62), ((1180, 390), 62), ((810, 650), 44), ((1110, 650), 44)):   # wing spots
        page.circle(c, r)


def rocket(page):
    """A rocket flying past planets and stars."""
    # planet with a ring
    page.figure(lambda s: s.circle((250, 310), 118))
    page.ellipse((250, 310), (196, 50), -15)
    # moon with craters
    page.figure(lambda s: s.circle((1165, 255), 100))
    for c, r in (((1135, 225), 24), ((1205, 285), 18), ((1150, 300), 14)):
        page.circle(c, r, width=THIN)
    # stars
    for c, r in (((165, 780), 58), ((420, 610), 44), ((1010, 660), 52), ((1235, 850), 62), ((560, 130), 38), ((930, 100), 34)):
        page.figure(lambda s, c=c, r=r: s.poly(star_points(c, r, r * 0.45)))
    for c in ((330, 940), (1100, 480), (1330, 560), (90, 520), (1010, 930), (640, 320)):
        page.dot(c, 9)
    page.view(1.12, 704 - 960 * 1.12, 528 - 625 * 1.12)

    def ship(s):
        s.ellipse((960, 520), (130, 300))
        s.poly([(850, 650), (720, 860), (850, 800)])                      # fins
        s.poly([(1070, 650), (1200, 860), (1070, 800)])
    page.figure(lambda s: s.poly(bezier((905, 815), (930, 900), (940, 960), (960, 1030), 16)
                                 + bezier((960, 1030), (980, 960), (990, 900), (1015, 815), 16)))   # flame
    body = page.figure(ship)
    page.inside(body, lambda pg: pg.line(bezier((800, 380), (900, 330), (1020, 330), (1120, 380), 20)))   # nose cone
    page.inside(body, lambda pg: pg.line([(840, 650), (840, 810)]) or pg.line([(1080, 650), (1080, 810)]))   # fins apart
    page.figure(lambda s: s.circle((960, 500), 66))                      # window
    page.circle((960, 500), 42)


def cake(page):
    """A birthday cake with candles, balloons, presents and a string of flags."""
    string = bezier((-20, 40), (350, 150), (1050, 150), (1430, 40), 60)
    page.line(string)
    for i in range(4, 60, 7):                                             # flags hanging from the string
        (x0, y0), (x1, y1) = string[i - 3], string[i + 3]
        page.figure(lambda s, x0=x0, y0=y0, x1=x1, y1=y1: s.poly([(x0, y0), (x1, y1), ((x0 + x1) / 2, (y0 + y1) / 2 + 105)]))
    page.view(0.94, 704 - 960 * 0.94, 965 - 940 * 0.94)
    ground(page, 940)
    for (cx, cy), tail in (((330, 400), (360, 700)), ((520, 330), (500, 660)), ((1430, 360), (1400, 680)), ((1610, 440), (1580, 720))):
        page.line(bezier((cx, cy + 120), (cx + 30, cy + 220), (tail[0] - 30, tail[1] - 100), tail, 24))
        page.figure(lambda s, cx=cx, cy=cy: s.ellipse((cx, cy), (88, 108)).poly([(cx - 16, cy + 102), (cx + 16, cy + 102), (cx, cy + 124)]))
        page.ellipse((cx - 36, cy - 42), (14, 26), 30, 0, 360, width=7)  # shine
    for x0, y0, w, h in ((290, 760, 230, 180), (1410, 780, 210, 160)):  # presents with ribbons
        page.figure(lambda s, x0=x0, y0=y0, w=w, h=h: s.rect(x0, y0, x0 + w, y0 + h, 8))
        page.line([(x0 + w / 2, y0), (x0 + w / 2, y0 + h)])
        page.line([(x0, y0 + h / 2), (x0 + w, y0 + h / 2)])
        page.figure(lambda s, x0=x0, y0=y0, w=w: s.ellipse((x0 + w / 2 - 38, y0 - 26), (38, 24), 20).ellipse((x0 + w / 2 + 38, y0 - 26), (38, 24), -20))
    page.figure(lambda s: s.ellipse((960, 905), (370, 42)))              # plate
    low = page.figure(lambda s: s.rect(660, 640, 1260, 900, 20))         # bottom layer
    top = page.figure(lambda s: s.rect(740, 470, 1180, 650, 20))         # top layer
    page.inside(low, lambda pg: pg.line(bezier((640, 700), (760, 770), (860, 640), (960, 705), 20) + bezier((960, 705), (1060, 770), (1160, 640), (1280, 700), 20)))   # icing
    page.inside(top, lambda pg: pg.line(bezier((720, 520), (830, 580), (870, 480), (960, 532), 20) + bezier((960, 532), (1050, 585), (1090, 490), (1200, 525), 20)))
    for x in (820, 960, 1100):                                           # candles + flames
        page.figure(lambda s, x=x: s.rect(x - 20, 350, x + 20, 470, 6))
        page.figure(lambda s, x=x: s.poly(bezier((x, 252), (x + 44, 306), (x + 28, 340), (x, 340), 12) + bezier((x, 340), (x - 28, 340), (x - 44, 306), (x, 252), 12)))
    for x in (760, 880, 1040, 1160):                                     # sprinkles
        page.circle((x, 810), 16, width=7)


PAGES = {"01_dino": dino, "02_house": house, "03_fish": fish, "04_butterfly": butterfly, "05_rocket": rocket, "06_cake": cake}


# ---------------------------------------------------------------------------------------------- previews


def areas(alpha):
    """The areas the game will find (PageRegions.FromWalls on its 1056x792 paper: a line pixel is alpha > 100,
    4-connected). Returns (labels, sizes) with label 0 = lines."""
    small = cv2.resize(alpha, CANVAS, interpolation=cv2.INTER_AREA)
    free = (small <= 100).astype(np.uint8)
    n, labels = cv2.connectedComponents(free, connectivity=4)
    sizes = [int((labels == i).sum()) for i in range(n)]
    return small, labels, sizes


def preview(name, page):
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    alpha = page.alpha()
    white = np.full((H, W, 3), 255, np.uint8)
    a = alpha[..., None] / 255.0
    cv2.imwrite(os.path.join(PREVIEW_DIR, f"{name}.png"), (white * (1 - a) + np.array(INK) * a).astype(np.uint8))

    small, labels, sizes = areas(alpha)
    rng = np.random.default_rng(7)
    out = np.full((CANVAS[1], CANVAS[0], 3), 255, np.uint8)
    counted = tiny = 0
    for i in range(1, len(sizes)):
        m = labels == i
        if sizes[i] >= SMALL_AREA:
            counted += 1
            hsv = np.uint8([[[int(rng.integers(0, 180)), 90, 255]]])
            out[m] = cv2.cvtColor(hsv, cv2.COLOR_HSV2BGR)[0, 0]
        else:
            tiny += 1
            out[m] = (90, 90, 255)
    sa = small[..., None] / 255.0
    out = (out * (1 - sa) + np.array(INK) * sa).astype(np.uint8)
    cv2.imwrite(os.path.join(PREVIEW_DIR, f"{name}_areas.png"), out)
    big = sorted((s for s in sizes[1:] if s >= SMALL_AREA), reverse=True)
    print(f"      {counted} areas to colour (largest {big[0]} px, smallest {big[-1]} px of {CANVAS[0] * CANVAS[1]}), "
          f"{tiny} too small to count (red in {name}_areas.png)")


def main():
    ap = argparse.ArgumentParser(description="Write Pikturo's colouring pages")
    ap.add_argument("names", nargs="*", help="only pages whose name contains one of these")
    ap.add_argument("--preview", action="store_true", help="also write previews and the areas the game will find to tools/paint/out/")
    args = ap.parse_args()
    for name, draw in PAGES.items():
        if args.names and not any(n in name for n in args.names):
            continue
        page = Page()
        draw(page)
        print("  ", os.path.relpath(page.save(name), ROOT))
        if args.preview:
            preview(name, page)


if __name__ == "__main__":
    main()
