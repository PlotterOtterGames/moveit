"""Builds Tundu's chibi characters headless:

    blender --background --factory-startup --python art/chibi/build.py -- [--preview] [--ao] [--export] [--low]

--preview renders line-ups into art/chibi/previews/ (gitignored), --ao bakes ambient occlusion into every piece
(Cycles, seconds), --export writes Chibi.fbx into Assets/Games/Common/Characters/Resources/Chibi, the generated
ChibiData.cs next to the runtime and art/chibi/chibi.json. Run --ao with --export (the game reads the occlusion).
--low builds everything at kit.LOW_DETAIL (about a third of the triangles): --export then writes Chibi_Low.fbx and
chibi_low.json, --preview the line-ups as lineup_low*.png. Run it as well after changing a piece.
"""

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))

import bpy  # noqa: E402

import chibi  # noqa: E402
import kit  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
low = "--low" in args
if low:
    kit.DETAIL = kit.LOW_DETAIL
t0 = time.time()
print("[chibi] build", "low" if low else "full", chibi.build())
if "--ao" in args:
    print("[chibi] ao", chibi.bake_ao())
if "--preview" in args:
    print("[chibi] preview", chibi.preview("lineup_low" if low else "lineup"))
if "--export" in args:
    print("[chibi] exported", chibi.export(low))
if "--save" in args:
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "chibi.blend"))
print(f"[chibi] done in {time.time() - t0:.1f} s")
