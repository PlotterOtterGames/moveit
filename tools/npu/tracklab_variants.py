"""Engine variants for tracklab.py: 'stock' is the box's engine as it is; 'teacher' is the reference the others are
scored against (tracklab_score.py); the rest are experiments."""
import math

import numpy as np

import blazepose
import tracklab
from tracklab import N, POSE


class Teacher(tracklab.Engine):
    """The reference: the landmark model run several times a frame, on the tracking crop, its mirror image and a few
    crops nudged in size and place, each answer brought back into the picture; a landmark's position is the median of
    the answers, and how far they spread says how sure the reference itself is (extra 'spread', pixels). Meant for the
    heavy model: far too slow for the box, but a steadier judge than any single run."""

    # (scale, shift x, shift y in crop sizes, mirrored)
    CROPS = ((1.0, 0.0, 0.0, True), (1.08, 0.02, -0.015, False), (0.94, -0.02, 0.015, False),
             (1.04, -0.015, -0.02, True), (0.97, 0.015, 0.02, True))

    def process(self, image, t_ns):
        res = super().process(image, t_ns)
        if not res['found']:
            return res
        roi = res['roi']
        answers = [res['xy'][:POSE]]
        c, s = math.cos(roi[3]), math.sin(roi[3])
        for scale, dx, dy, mirror in self.CROPS:
            # the shift is along the crop's own axes
            ox, oy = dx * roi[2], dy * roi[2]
            other = (roi[0] + c * ox - s * oy, roi[1] + s * ox + c * oy, roi[2] * scale, roi[3])
            raw, conf, score, _ = self.landmarks_at(image, other, mirror=mirror)
            xy = tracklab.refine(raw[:, :2] / blazepose.LM_SIZE, conf)
            m = blazepose.crop_matrix(other)
            answers.append(((xy * blazepose.LM_SIZE) @ m[:, :2].T + m[:, 2])[:POSE])
        answers = np.array(answers)                      # (runs, 33, 2)
        centre = np.median(answers, axis=0)
        spread = np.linalg.norm(answers - centre, axis=2)
        res['extra'] = dict(tta_xy=centre, tta_spread=np.median(spread, axis=0), tta_max=spread.max(axis=0))
        return res


def make(name, run, **kw):
    if name.split(':')[0] == 'stock':       # stock:COAST_FRAMES=6,... sets class numbers
        cls = type('StockVariant', (tracklab.Engine,), {})
        for item in filter(None, name.partition(':')[2].split(',')):
            key, value = item.split('=')
            setattr(cls, key, float(value) if '.' in value else int(value))
        return cls(run, **kw)
    if name == 'teacher':
        return Teacher(run, **kw)
    import tracklab_trust
    return tracklab_trust.make(name, run, **kw)
