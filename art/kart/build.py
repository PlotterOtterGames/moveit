"""Builds Gara!'s kart (and the track scenery) headless:

    blender --background --factory-startup --python art/kart/build.py -- [--preview] [--ao] [--export] [--props]

--preview renders the kart in four colours into art/kart/previews/ (gitignored), --ao bakes ambient occlusion into its
parts (Cycles), --export writes Assets/Games/Kart/Art/Kart.fbx. --props builds the scenery (art/kart/props.py: trees,
rocks, houses, the start gantry, ...), previews it and exports one FBX of them all (KartProps.fbx).
"""

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
sys.path.insert(0, os.path.join(HERE, "..", "chibi"))

import bpy  # noqa: E402

import kart  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
t0 = time.time()
if "--props" in args:
    import props  # noqa: E402
    print("[kart] props", props.build())
    if "--preview" in args:
        print("[kart] props preview", props.preview())
    if "--export" in args:
        print("[kart] props exported", props.export())
else:
    print("[kart] build", kart.build())
    if "--ao" in args:
        print("[kart] ao", kart.bake_ao())
    if "--preview" in args:
        print("[kart] preview", kart.preview())
    if "--export" in args:
        print("[kart] exported", kart.export())
print(f"[kart] done in {time.time() - t0:.1f} s")
