"""Builds Yjet!'s props headless:

    blender --background --factory-startup --python art/space/build.py -- [--preview] [--export]

--preview renders them into art/space/previews/ (gitignored), --export writes Assets/Games/Space/Art/SpaceProps.fbx and
the shared palette next to it (art/space/props.py: the aliens, the tow ship, asteroids, the star, the pad, planet
dressing). The astronaut is a chibi (art/chibi: SpaceSuit, SpacePants, SpaceBoots, Jetpack).
"""

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))

import bpy  # noqa: E402,F401

import props  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
t0 = time.time()
print("[space] props", props.build())
if "--preview" in args:
    print("[space] preview", props.preview())
if "--export" in args:
    print("[space] exported", props.export())
print(f"[space] done in {time.time() - t0:.1f} s")
