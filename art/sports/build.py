"""Builds Sporte!'s athletes headless:

    blender --background --python art/sports/build.py -- [--ao] [--preview] [--anim] [--export] [--save]

--ao bakes ambient occlusion (Cycles, a minute or two), --anim keys every clip, --export writes Athlete.fbx and
athlete.json into Assets/Games/Sports/Art, --save keeps art/sports/athletes.blend, --preview renders line-ups.
"""

import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))

import bpy  # noqa: E402

import athlete  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
t0 = time.time()
print("[athlete] build", athlete.build())
if "--anim" in args or "--export" in args:
    import athlete_anim
    print("[athlete] clips", athlete_anim.animate())
if "--ao" in args:
    print("[athlete] ao", athlete.bake_ao())
if "--preview" in args:
    print("[athlete] preview", athlete.preview())
if "--clips" in args:
    import athlete_anim
    print("[athlete] clip sheets", athlete_anim.contact_sheets())
if "--crowd" in args:
    import crowd
    if "--anim" not in args and "--export" not in args:
        import athlete_anim
        athlete_anim.animate(only=["Clap", "Cheer"])
    print("[athlete] crowd cells", len(crowd.render_atlas()))
if "--export" in args:
    import tundu_rig as tr
    arm = bpy.data.objects["AthleteRig"]
    for o in athlete.pieces():   # the bake's and the previews' colours are not needed in the game
        for name in ("Col", "AO"):
            attr = o.data.color_attributes.get(name)
            if attr:
                o.data.color_attributes.remove(attr)
    path = tr.export_fbx(os.path.join(athlete.UNITY_ART, "Athlete.fbx"), arm, athlete.pieces())
    athlete.write_manifest()
    print("[athlete] exported", path)
if "--save" in args and "--export" not in args:
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "athletes.blend"))
print(f"[athlete] done in {time.time() - t0:.1f} s")
