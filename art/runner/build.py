"""Builds Vrapo!'s art headless (Blender 5.1):

    blender --background --factory-startup --python art/runner/build.py -- [--valley] [--dino] [--ao] [--preview] [--export] [--game]

--valley: the valley kit (art/runner/valley.py) -> Assets/Games/Runner/Art/Valley.fbx + art/runner/valley.json;
--dino: the hero (art/runner/dino.py + dino_rig.py) -> Assets/Games/Runner/Art/Dino.fbx;
--ao bakes ambient occlusion into the vertex colours' alpha (slow: Cycles), --preview renders sheets into
art/runner/previews/ (gitignored), --game renders what the game's camera sees (art/runner/gameview.py).
"""

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))

import bpy  # noqa: E402,F401

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
t0 = time.time()
# the factory scene's cube, camera and light out of the way
for _o in list(bpy.data.objects):
    bpy.data.objects.remove(_o, do_unlink=True)
if "--valley" in args:
    import valley  # noqa: E402
    print("[runner] valley", valley.build())
    if "--ao" in args:
        print("[runner] ao", valley.bake_ao())
    if "--preview" in args:
        print("[runner] preview", valley.preview())
if "--dino" in args:
    import dino  # noqa: E402
    import dino_rig  # noqa: E402
    print("[runner] dino", dino.build())
    if "--ao" in args:
        import kit  # noqa: E402
        print("[runner] dino ao", kit.bake_ao([bpy.data.objects["Dino"]], samples=32))
    print("[runner] rig", dino_rig.rig())
    print("[runner] clips", dino_rig.animate())
    if "--preview" in args:
        print("[runner] dino preview", dino_rig.preview())
if "--game" in args:
    import gameview  # noqa: E402
    print("[runner] game view", gameview.render_all())
if "--export" in args:
    if "--valley" in args:
        print("[runner] exported", valley.export())
    if "--dino" in args:
        print("[runner] exported", dino_rig.export())
print(f"[runner] done in {time.time() - t0:.1f} s")
