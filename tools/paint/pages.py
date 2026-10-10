"""Pikturo!'s colouring pages, drawn as code: black line art on a transparent background, 1920x1080, written to
unity/MoveItGames/Assets/Resources/Paint/Pages/<name>.png (Unity imports them alpha-only, see PaintPageImport.cs).

    motion-service-py/.venv/Scripts/python tools/paint/pages.py            # all pages
    motion-service-py/.venv/Scripts/python tools/paint/pages.py dino       # pages whose name contains "dino"
    ... --preview                                                          # also a white-background preview in out/

Your own pages work too: any PNG with dark lines on a transparent background in that folder (keep the outlines
closed, so the paint bucket fills one area at a time). Keep the left and right edges (~190 px) and the top
(~160 px) free: the toolbars cover them.

Figures are drawn as filled shapes and outlined by morphology (dilate minus erode), so overlapping body parts give
one clean outer line; details (eyes, spots, stripes) are drawn as lines on top. Everything is drawn at 2x and
scaled down, for smooth lines.
"""

import argparse
import math
import os

import cv2
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "unity", "MoveItGames", "Assets", "Resources", "Paint", "Pages")
PREVIEW_DIR = os.path.join(os.path.dirname(__file__), "out")
W, H = 1920, 1080
S = 2                    # supersampling
LINE = 9                 # line width in final pixels
INK = (36, 30, 30)       # BGR of the lines (only alpha is used by the game)


class Page:
    def __init__(self):
        self.mask = np.zeros((H * S, W * S), np.uint8)

    @staticmethod
    def p(pt):
        return int(round(pt[0] * S)), int(round(pt[1] * S))

    # ---- details: plain lines
    def line(self, pts, closed=False, width=LINE):
        pts = np.array([self.p(q) for q in pts], np.int32)
        cv2.polylines(self.mask, [pts], closed, 255, width * S, cv2.LINE_AA)

    def circle(self, c, r, width=LINE):
        cv2.circle(self.mask, self.p(c), int(r * S), 255, width * S, cv2.LINE_AA)

    def dot(self, c, r):
        cv2.circle(self.mask, self.p(c), int(r * S), 255, -1, cv2.LINE_AA)

    def ellipse(self, c, axes, angle=0, start=0, end=360, width=LINE):
        cv2.ellipse(self.mask, self.p(c), (int(axes[0] * S), int(axes[1] * S)), angle, start, end, 255, width * S, cv2.LINE_AA)

    # ---- figures: filled shapes, outlined together
    def figure(self, draw, width=LINE):
        """draw(Shape) adds filled parts; the union's outline is added to the page."""
        shape = Shape()
        draw(shape)
        r = width * S // 2
        k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))
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

    def save(self, name, preview=False):
        alpha = cv2.resize(self.mask, (W, H), interpolation=cv2.INTER_AREA)
        img = np.zeros((H, W, 4), np.uint8)
        img[..., :3] = INK
        img[..., 3] = alpha
        os.makedirs(OUT_DIR, exist_ok=True)
        path = os.path.join(OUT_DIR, f"{name}.png")
        cv2.imwrite(path, img)
        if preview:
            os.makedirs(PREVIEW_DIR, exist_ok=True)
            white = np.full((H, W, 3), 255, np.uint8)
            a = alpha[..., None] / 255.0
            out = (white * (1 - a) + np.array(INK) * a).astype(np.uint8)
            cv2.rectangle(out, (0, 0), (190, H), (235, 225, 225), -1)      # toolbars, to check the free margins
            cv2.rectangle(out, (W - 190, 0), (W, H), (235, 225, 225), -1)
            cv2.rectangle(out, (480, 0), (W - 480, 140), (235, 225, 225), -1)
            cv2.imwrite(os.path.join(PREVIEW_DIR, f"{name}.png"), out)
        return path


class Shape:
    def __init__(self):
        self.mask = np.zeros((H * S, W * S), np.uint8)

    def ellipse(self, c, axes, angle=0):
        cv2.ellipse(self.mask, Page.p(c), (int(axes[0] * S), int(axes[1] * S)), angle, 0, 360, 255, -1, cv2.LINE_AA)
        return self

    def circle(self, c, r):
        return self.ellipse(c, (r, r))

    def poly(self, pts):
        cv2.fillPoly(self.mask, [np.array([Page.p(q) for q in pts], np.int32)], 255, cv2.LINE_AA)
        return self

    def thick(self, pts, width):
        cv2.polylines(self.mask, [np.array([Page.p(q) for q in pts], np.int32)], False, 255, int(width * S), cv2.LINE_AA)
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
    """A flower head: petals around a centre (separate regions to colour)."""
    def head(s):
        for k in range(petals):
            a = math.radians(rot + k * 360 / petals)
            s.circle((c[0] + r * 0.62 * math.cos(a), c[1] + r * 0.62 * math.sin(a)), r * 0.45)
        s.circle(c, r * 0.4)
    page.figure(head)
    page.circle(c, r * 0.32)


def ground(page, y, x0=200, x1=1720):
    page.line([(x0, y), (x1, y)])


# ---------------------------------------------------------------------------------------------- pages


def dino(page):
    """Tundu's dino in its world: a chunky T-rex smiling, its egg, a volcano with smoke, grass."""
    # volcano behind, with a smoke cloud
    page.figure(lambda s: s.poly([(1330, 900), (1470, 510), (1570, 510), (1710, 900)]))
    page.ellipse((1520, 512), (50, 16), 0, 0, 180)                      # crater lip
    page.line([(1490, 525), (1478, 600), (1505, 640), (1495, 700)])      # lava
    page.figure(lambda s: [s.circle((1520 + dx, 420 + dy), 52) for dx, dy in ((-55, 25), (15, -10), (80, 30), (10, 50))])
    ground(page, 900)

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
        page.line([(x, 912), (x, 892)], width=7)
    page.figure(lambda s: s.ellipse((300, 830), (62, 80)))               # egg with a zigzag crack
    page.line([(240, 820), (265, 800), (285, 825), (305, 800), (330, 825), (360, 805)])
    for x in (560, 1250, 1690):                                          # grass tufts
        page.line([(x - 30, 900), (x - 40, 850)])
        page.line([(x, 900), (x, 835)])
        page.line([(x + 30, 900), (x + 40, 850)])


def house(page):
    """A house with a sun, a cloud, a tree and flowers."""
    ground(page, 940)
    page.figure(lambda s: s.rect(560, 560, 1060, 940))                  # walls
    page.figure(lambda s: s.poly([(520, 580), (810, 330), (1100, 580)]).rect(940, 360, 1000, 470))   # roof + chimney
    page.figure(lambda s: s.rect(760, 740, 870, 940, 10))               # door
    page.dot((850, 845), 8)
    for x0 in (610, 920):                                                # windows with crosses
        page.figure(lambda s, x0=x0: s.rect(x0, 640, x0 + 100, 730, 8))
        page.line([(x0 + 50, 640), (x0 + 50, 730)], width=7)
        page.line([(x0, 685), (x0 + 100, 685)], width=7)
    page.figure(lambda s: s.rect(780, 450, 840, 510, 30))                # round attic window
    # sun with rays
    page.figure(lambda s: s.circle((420, 310), 85))
    for k in range(10):
        a = math.radians(k * 36)
        page.line([(420 + 115 * math.cos(a), 310 + 115 * math.sin(a)), (420 + 160 * math.cos(a), 310 + 160 * math.sin(a))])
    page.dot((392, 295), 9)
    page.dot((448, 295), 9)
    page.line(bezier((385, 335), (405, 360), (435, 360), (455, 335), 12))
    # cloud
    page.figure(lambda s: [s.circle(c, r) for c, r in (((1250, 260), 70), ((1330, 220), 90), ((1420, 260), 70), ((1330, 290), 70))])
    # tree
    page.figure(lambda s: s.rect(1380, 700, 1440, 940))
    page.figure(lambda s: [s.circle(c, r) for c, r in (((1410, 600), 130), ((1320, 650), 90), ((1500, 650), 90), ((1410, 500), 100))])
    for c in ((1360, 590), (1460, 560), (1420, 660)):
        page.circle(c, 18)                                               # apples
    # flowers along the ground
    for x in (300, 420, 1220, 1620):
        page.line([(x, 940), (x, 830)])
        flower(page, (x, 800), 42)


def fish(page):
    """Under the sea: a big fish, a small fish, bubbles, seaweed, a starfish, a shell."""
    page.line(bezier((200, 960), (600, 930), (1200, 990), (1720, 950), 60))   # sand
    def big(s):
        s.ellipse((880, 520), (300, 190))
        s.poly([(1130, 520), (1330, 360), (1300, 520), (1330, 680)])    # tail
        s.poly([(780, 340), (900, 230), (980, 345)])                     # top fin
    body = page.figure(big)
    page.inside(body, lambda pg: [pg.line(bezier((x, 300), (x + 40, 450), (x + 40, 590), (x, 740), 20)) for x in (820, 930, 1040)]
                and pg.line(bezier((700, 340), (650, 450), (650, 600), (700, 710), 20)))   # stripes, gill
    page.ellipse((880, 600), (60, 30), 30)                               # side fin
    page.figure(lambda s: s.circle((640, 470), 42))                     # eye
    page.dot((650, 465), 18)
    page.line(bezier((600, 580), (620, 600), (650, 600), (670, 585), 10))      # smile
    def small(s):
        s.ellipse((1450, 760), (110, 65))
        s.poly([(1550, 760), (1640, 690), (1625, 760), (1640, 830)])
    page.figure(small)
    page.dot((1390, 745), 12)
    for c, r in (((560, 300), 30), ((520, 220), 22), ((560, 160), 16), ((1500, 600), 26), ((1470, 520), 18)):
        page.circle(c, r)
    for x in (300, 380, 1650):                                           # seaweed
        page.figure(lambda s, x=x: s.thick(bezier((x, 960), (x - 60, 820), (x + 60, 700), (x, 560), 30), 34))
    page.figure(lambda s: s.poly(star_points((560, 860), 80, 36)))       # starfish
    page.figure(lambda s: s.ellipse((1150, 900), (90, 60)))              # shell
    for k in range(-2, 3):
        page.line([(1150, 950), (1150 + k * 38, 850 + abs(k) * 10)], width=7)


def butterfly(page):
    """A big butterfly over flowers."""
    def wings(s):
        s.ellipse((760, 400), (190, 150), -30)
        s.ellipse((1160, 400), (190, 150), 30)
        s.ellipse((800, 640), (130, 110), 25)
        s.ellipse((1120, 640), (130, 110), -25)
    page.figure(wings)
    page.figure(lambda s: s.ellipse((960, 520), (40, 190)))              # body
    page.figure(lambda s: s.circle((960, 310), 48))                      # head
    page.dot((942, 300), 9)
    page.dot((978, 300), 9)
    page.line(bezier((935, 330), (950, 345), (970, 345), (985, 330), 10), width=7)
    page.line(bezier((940, 270), (920, 210), (880, 180), (850, 175), 20))     # antennae
    page.line(bezier((980, 270), (1000, 210), (1040, 180), (1070, 175), 20))
    page.dot((850, 175), 14)
    page.dot((1070, 175), 14)
    for c, r in (((740, 390), 60), ((1180, 390), 60), ((810, 650), 40), ((1110, 650), 40),
                 ((640, 470), 28), ((1280, 470), 28)):                   # wing spots
        page.circle(c, r)
    for x, h in ((330, 260), (520, 180), (1400, 200), (1600, 270)):      # flowers
        page.line([(x, 960), (x, 960 - h)])
        page.figure(lambda s, x=x, h=h: s.ellipse((x + 45, 960 - h * 0.45), (45, 18), -30))   # leaf
        flower(page, (x, 960 - h - 40), 60, petals=6, rot=15)
    ground(page, 960)


def rocket(page):
    """A rocket flying past planets and stars."""
    def ship(s):
        s.ellipse((960, 520), (130, 300))
        s.poly([(850, 650), (720, 860), (850, 800)])                      # fins
        s.poly([(1070, 650), (1200, 860), (1070, 800)])
    page.figure(ship)
    page.line(bezier((850, 360), (900, 330), (1020, 330), (1070, 360), 20))   # nose cone line
    page.figure(lambda s: s.circle((960, 500), 62))                      # window
    page.circle((960, 500), 40)
    page.figure(lambda s: s.poly(bezier((905, 835), (930, 900), (940, 960), (960, 1030), 16)
                                 + bezier((960, 1030), (980, 960), (990, 900), (1015, 835), 16)))   # flame
    page.line(bezier((935, 850), (950, 900), (955, 930), (960, 960), 12) + bezier((960, 960), (965, 930), (970, 900), (985, 850), 12), width=7)
    # planet with a ring
    page.figure(lambda s: s.circle((420, 380), 120))
    page.ellipse((420, 380), (200, 50), -15)
    page.line(bezier((340, 330), (390, 300), (450, 300), (500, 320), 16), width=7)
    # moon with craters
    page.figure(lambda s: s.circle((1500, 330), 95))
    for c, r in (((1470, 300), 22), ((1540, 360), 16), ((1490, 380), 12)):
        page.circle(c, r, width=7)
    # stars
    for c, r in (((300, 760), 55), ((620, 640), 40), ((1300, 700), 50), ((1620, 820), 60), ((1250, 220), 35), ((700, 230), 30)):
        page.figure(lambda s, c=c, r=r: s.poly(star_points(c, r, r * 0.45)))
    for c in ((520, 880), (1450, 560), (1700, 520), (260, 540), (820, 960)):
        page.dot(c, 8)


def cake(page):
    """A birthday cake with candles, balloons and presents."""
    page.figure(lambda s: s.rect(660, 640, 1260, 900, 20))               # bottom layer
    page.figure(lambda s: s.rect(740, 470, 1180, 650, 20))               # top layer
    page.line(bezier((660, 700), (760, 760), (860, 640), (960, 700), 20) + bezier((960, 700), (1060, 760), (1160, 640), (1260, 700), 20))   # icing
    page.line(bezier((740, 520), (830, 570), (870, 480), (960, 530), 20) + bezier((960, 530), (1050, 580), (1090, 490), (1180, 530), 20))
    page.ellipse((960, 905), (360, 40))                                  # plate
    for x in (820, 960, 1100):                                           # candles + flames
        page.figure(lambda s, x=x: s.rect(x - 18, 350, x + 18, 470, 6))
        page.figure(lambda s, x=x: s.poly(bezier((x, 260), (x + 40, 310), (x + 25, 340), (x, 340), 12) + bezier((x, 340), (x - 25, 340), (x - 40, 310), (x, 260), 12)))
    for x in (760, 880, 1040, 1160):                                     # sprinkles
        page.circle((x, 800), 14, width=6)
    for (cx, cy), tail in (((330, 330), (360, 640)), ((520, 260), (500, 600)), ((1430, 300), (1400, 620)), ((1610, 380), (1580, 660))):
        page.figure(lambda s, cx=cx, cy=cy: s.ellipse((cx, cy), (85, 105)).poly([(cx - 15, cy + 100), (cx + 15, cy + 100), (cx, cy + 120)]))
        page.line(bezier((cx, cy + 120), (cx + 30, cy + 220), (tail[0] - 30, tail[1] - 100), tail, 24))
        page.ellipse((cx - 35, cy - 40), (14, 26), 30, 0, 360, width=6)  # shine
    for x0, y0, w, h in ((300, 760, 220, 180), (1420, 780, 200, 160)):  # presents with ribbons
        page.figure(lambda s, x0=x0, y0=y0, w=w, h=h: s.rect(x0, y0, x0 + w, y0 + h, 8))
        page.line([(x0 + w / 2, y0), (x0 + w / 2, y0 + h)])
        page.line([(x0, y0 + h / 2), (x0 + w, y0 + h / 2)])
        page.figure(lambda s, x0=x0, y0=y0, w=w: s.ellipse((x0 + w / 2 - 35, y0 - 25), (35, 22), 20).ellipse((x0 + w / 2 + 35, y0 - 25), (35, 22), -20))
    ground(page, 940)


PAGES = {"01_dino": dino, "02_house": house, "03_fish": fish, "04_butterfly": butterfly, "05_rocket": rocket, "06_cake": cake}


def main():
    ap = argparse.ArgumentParser(description="Write Pikturo's colouring pages")
    ap.add_argument("names", nargs="*", help="only pages whose name contains one of these")
    ap.add_argument("--preview", action="store_true", help="also write previews with the toolbars marked to tools/paint/out/")
    args = ap.parse_args()
    for name, draw in PAGES.items():
        if args.names and not any(n in name for n in args.names):
            continue
        page = Page()
        draw(page)
        print("  ", os.path.relpath(page.save(name, args.preview), ROOT))


if __name__ == "__main__":
    main()
