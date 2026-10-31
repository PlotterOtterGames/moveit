"""Builds Sporte!'s venues headless: blender --background --python art/sports/venues_build.py -- football [track]
[beach] [--preview]"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import venue_kit as vk  # noqa: E402
import venues  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
t0 = time.time()
VIEWS = {
    "football": [((0, 2.4, 27), (0, 1.5, 52)), ((0, 2.6, -60), (0, 1.0, -30)), ((-30, 30, -30), (0, 0, 10))],
    "track": [((-1, 2.2, -6), (0, 1, 30)), ((-7.5, 1.7, 50), (3, 1.1, 51.5)), ((-40, 45, -40), (-30, 0, 60))],
    "beach": [((0, 4.6, -15), (0, 1.2, 0)), ((-6, 1.5, -10), (4, 2, 10)), ((30, 30, -40), (0, 0, 10))],
}
for name in [a for a in args if not a.startswith("--")]:
    info = getattr(venues, name)()
    print(f"[venues] {name}", info)
    if "--preview" in args:
        for i, (cam, look) in enumerate(VIEWS.get(name, [])):
            print("[venues] preview", vk.preview(os.path.join(HERE, "previews", f"venue_{name}_{i}.png"), cam, look, lens=22))
print(f"[venues] done in {time.time() - t0:.1f} s")
