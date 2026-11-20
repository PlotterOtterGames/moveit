"""Where things land on the screen in Superboks!'s camera shots, before trying them in the game (2026-11-19: placed by eye,
a rope crossed the lens, the referee stood between the camera and the winner, heads were cut; with this every cutaway in
BoxingGame.UpdateCamera was placed in one round). Unity's camera maths (a look-at camera, vertical field of view), metres
from the canvas's middle (add nothing for the ring's height: BoxingGame adds it), the hero at z -0.5 facing +z, the
opponent at z 0.52 facing -z, the ropes at +-2.2 m. Screen x 0 = left .. 1 = right, y 0 = top .. 1 = bottom, depth in m;
points out of the frame are marked.

    python tools/boxing/shots.py my_shots.py    (a script that calls show(...) with the points it cares about)

e.g.  import shots as S
      pts = {}; pts.update(S.hero_points()); pts.update(S.foe_points(lying=True)); pts.update(S.ref_points(0.95, 0.37))
      S.show("count", (-2.1, 1.8, -0.35), (0.35, 0.4, 0.55), 52, pts)
"""
import math, sys

def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def add(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def mul(a, k): return (a[0]*k, a[1]*k, a[2]*k)
def dot(a, b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def norm(a):
    l = math.sqrt(dot(a, a)); return (a[0]/l, a[1]/l, a[2]/l)
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])

def project(pos, look, fov, p, aspect=16/9):
    f = norm(sub(look, pos))
    r = norm(cross((0, 1, 0), f))
    u = cross(f, r)
    d = sub(p, pos)
    z = dot(d, f)
    if z <= 0.05: return None
    t = math.tan(math.radians(fov / 2))
    x = dot(d, r) / z / (t * aspect)
    y = dot(d, u) / z / t
    return (0.5 + 0.5 * x, 0.5 - 0.5 * y, z)

HERO = 0.88
def hero_points(z=-0.5, lying=False):
    if lying:   # falls backward round its heels: head toward -z
        return {"hero feet": (0, 0.05, z), "hero head": (0, 0.2, z - 1.03 * HERO / 0.88 * 0.88)}
    return {"hero feet": (0, 0, z), "hero head": (0, 1.03, z), "hero top": (0, 1.32, z),
            "hero glove L": (-0.42, 1.38, z + 0.08), "hero glove R": (0.42, 1.38, z + 0.08)}

def foe_points(s=1.0, z=0.52, lying=False):
    if lying:   # backward round its heels: head toward +z
        return {"foe feet": (0, 0.05, z), "foe head": (0, 0.22, z + 1.17 * s), "foe mid": (0, 0.2, z + 0.6 * s)}
    return {"foe feet": (0, 0, z), "foe head": (0, 1.17 * s, z), "foe top": (0, 1.5 * s, z)}

def ref_points(x, z):
    return {"ref feet": (x, 0, z), "ref head": (x, 1.12, z), "ref top": (x, 1.44, z)}

def show(name, pos, look, fov, pts):
    print(f"== {name}: pos {pos} look {look} fov {fov}")
    for k, p in pts.items():
        s = project(pos, look, fov, p)
        if s is None: print(f"   {k:14s} behind the camera"); continue
        flag = "" if 0 <= s[0] <= 1 and 0 <= s[1] <= 1 else "  << OUT"
        print(f"   {k:14s} x {s[0]:5.2f} y {s[1]:5.2f} depth {s[2]:4.2f}{flag}")

def winner_look(pos, at, shift):
    f = norm(sub(at, pos)); r = norm(cross((0, 1, 0), f))
    return add(at, mul(r, shift))

if __name__ == "__main__":
    exec(open(sys.argv[1]).read())
