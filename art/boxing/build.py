"""Builds Superboks!'s boxing kit and arenas headless:

    blender --background --factory-startup --python art/boxing/build.py -- [--kit] [--arena] [--ao] [--preview] [--export]

--kit: the gloves, belt, bag and lamp (boxing.py: Boxing.fbx; the old ring is still in it); --arena: the four arenas and
their rings (arena.py: Arena.fbx and the crowd's seats). --ao bakes ambient occlusion (Cycles), --preview renders into
art/boxing/previews/ (gitignored): the kit's sheet, each arena from the fight camera, the side and the front;
--export writes the FBX. ARENA_STAGES=gym,roof builds only some arenas (a quick look; don't export that).
"""

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
sys.path.insert(0, os.path.join(HERE, "..", "chibi"))
sys.path.insert(0, os.path.join(HERE, "..", "runner"))

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
t0 = time.time()
if "--kit" in args or "--arena" not in args:
    import boxing  # noqa: E402
    print("[boxing] build", boxing.build())
    if "--ao" in args:
        print("[boxing] ao", boxing.bake_ao())
    if "--preview" in args:
        print("[boxing] preview", boxing.preview())
    if "--export" in args:
        print("[boxing] exported", boxing.export())
if "--arena" in args:
    import arena  # noqa: E402
    only = os.environ.get("ARENA_STAGES")
    stages = tuple(only.split(",")) if only else arena.STAGES
    print("[arena] build", arena.build(stages))
    print(f"[arena] built in {time.time() - t0:.1f} s")
    if "--ao" in args:
        print("[arena] ao", arena.bake_ao())
        print(f"[arena] baked by {time.time() - t0:.1f} s")
    if "--preview" in args:
        print("[arena] preview", arena.preview(stages))
    if "--export" in args:
        print("[arena] exported", arena.export())
print(f"[boxing] done in {time.time() - t0:.1f} s")
