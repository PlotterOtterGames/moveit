"""Builds Zana!'s art headless (Blender 5.1):

    blender --background --factory-startup --python art/fairy/build.py -- [--gloom] [--props] [--heroine] [--ao] [--preview] [--export]

--gloom: the creatures' kit (art/fairy/gloom.py) -> Assets/Games/Fairy/Art/GloomKit.fbx + gloom_kit.json;
--props: the lands' props (art/fairy/props.py) -> Assets/Games/Fairy/Art/FairyProps.fbx + the palette;
--ao bakes the creatures' ambient occlusion (with --gloom), --preview renders into art/fairy/previews/ (gitignored).
--heroine: the fairies themselves (art/fairy/heroine.py: tall and slender, the chibis' rig and regions) ->
Resources/Chibi/Heroine.fbx.
"""

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
sys.path.insert(0, os.path.join(HERE, "..", "chibi"))

import bpy  # noqa: E402,F401

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
t0 = time.time()
if "--gloom" in args:
    import gloom  # noqa: E402
    print("[fairy] gloom", gloom.build())
    if "--ao" in args:
        print("[fairy] ao", gloom.bake_ao())
    if "--preview" in args:
        print("[fairy] preview", gloom.preview())
    if "--export" in args:
        print("[fairy] exported", gloom.export())
if "--heroine" in args:
    import heroine  # noqa: E402
    print("[fairy] heroine", heroine.build())
    if "--preview" in args:
        print("[fairy] preview", heroine.preview())
    if "--export" in args:
        print("[fairy] exported", heroine.export())
if "--props" in args:
    import props  # noqa: E402
    print("[fairy] props", props.build())
    if "--ao" in args:
        print("[fairy] ao", props.bake_ao())
    if "--preview" in args:
        print("[fairy] preview", props.preview())
    if "--export" in args:
        print("[fairy] exported", props.export())
print(f"[fairy] done in {time.time() - t0:.1f} s")
