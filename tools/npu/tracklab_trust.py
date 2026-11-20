"""The engine that listens to how sure the landmark model is: the lab twin of the box's pose_trust.h (the C++ is
checked against this file number for number by tools/npu/trust_check.sh; change both together).

MediaPipe's pose landmarker takes each landmark from the model's regression head, nudges it with the heatmap head
inside a 7 x 7 window, smooths it, and reports the model's "visibility", low-passed over ~0.3 s. On the box camera's
evening pictures that leaves this on the table (measured with tracklab_score.py on the recorded clips, 2026-11-10,
docs/hardware.md "Tracking that listens to the model"):

1. The heatmap is the model's opinion of its own answer. At a wrong landmark it almost always shows nothing under the
   landmark's position (lite, wrists: AUC 0.98 for "more than a quarter torso off", against 0.72 for the smoothed
   visibility the games got). Every landmark gets a trust from the heatmap's score under it, and one with nothing under
   it moves onto the heatmap's clear peak if there is one elsewhere.
2. A sample the model isn't sure of is pulled toward where the landmark was, riding on the body's own move (the more
   unsure, the more), before MediaPipe's own filter: sure samples go through exactly as before (same lag, same feel),
   a one-frame flip of a wrist doesn't get through, and a limb the camera can't see rests instead of flailing.
3. The visibility the games gate on is the model's visibility smoothed only lightly, capped by the trust of this very
   frame: a wrong frame is marked as such in that frame, and a hand that comes back counts at once (MediaPipe's low
   pass took ~6 frames each way).
4. A body that clearly faced the camera and a moment later is read as seen from behind hasn't turned: the sides are
   put back.
Plus, in tracklab.Engine: a followed person is kept through a few frames of a low pose score (COAST_FRAMES), and the
detector's input is averaged and brightened (det='mean').
"""
import math

import numpy as np

import blazepose
import tracklab
from mpfilters import LowPass
from tracklab import N, POSE

TORSO = (11, 12, 23, 24)


class Trust(tracklab.Engine):
    # the heatmap's score under a landmark's final position (the best of the 3 x 3 cells around it): under H0 nothing
    # is there, from H1 the model is sure
    H0, H1 = 0.15, 0.40
    W_MIN = 0.06            # what an entirely unsure sample still moves the landmark by (a hidden limb drifts to the
    #                         model's guess over ~0.5 s instead of freezing forever)
    # a landmark moves to the heatmap's best peak when there's nothing under its position (under RESCUE_LOW) and a clear
    # peak (from RESCUE_PEAK) further than RESCUE_FAR crop sizes away
    RESCUE_LOW, RESCUE_PEAK, RESCUE_FAR = 0.20, 0.45, 0.04
    REFINE_MIN = 0.5        # MediaPipe's min_confidence_to_refine
    VIS_ALPHA = 0.5         # the visibility's low pass (MediaPipe: 0.1)
    VIS_FLOOR = 0.25        # the visibility left when the heatmap shows nothing under the landmark
    # the left/right guard: shoulders this far apart across the body (torso lengths, + = facing the camera) before and
    # after, within SWAP_GAP seconds; the sides are put back for at most SWAP_MAX frames in a row
    SWAP_GUARD, SWAP_MAX, SWAP_WIDTH, SWAP_GAP = 1, 30, 0.45, 0.25
    COAST_FRAMES = 6        # tracklab.Engine: a followed person survives this many low pose scores in a row

    def __init__(self, run, rescue=True, weigh=True, **kw):
        self.rescue, self.weigh = rescue, weigh
        super().__init__(run, **kw)

    def reset_filters(self):
        super().reset_filters()
        self.f_vis = [LowPass(self.VIS_ALPHA) for _ in range(POSE)]
        self.prev = None            # last frame's smoothed landmarks (x, y, z in pixels), world, root
        self.ctx = None
        self.swapped = 0            # frames in a row whose sides were put back
        self.face_ref = None        # (the shoulders' facing, t) of the last output with shoulders the model was sure of

    @staticmethod
    def under_at(conf, i, xy):
        """The heatmap's best score in the 3 x 3 cells around a position (0..1 of the crop)."""
        hh, hw = conf.shape[:2]
        c, r = int(xy[0] * hw + 0.5), int(xy[1] * hh + 0.5)
        if c < 0 or c >= hw or r < 0 or r >= hh:
            return None
        return float(conf[max(0, r - 1):r + 2, max(0, c - 1):c + 2, i].max())

    # -- positions: the regression nudged by the heatmap (MediaPipe), or moved onto the heatmap's peak
    def decode(self, raw, conf, roi, t_ns):
        hh, hw = conf.shape[:2]
        reg = raw[:, :2] / blazepose.LM_SIZE
        xy = tracklab.refine(reg, conf) if self.REFINE_MIN == 0.5 else reg.copy()
        under = np.ones(N, np.float32)
        inside = np.zeros(N, bool)
        for i in range(N):
            u = self.under_at(conf, i, xy[i])
            if u is not None:
                inside[i] = True
                under[i] = u
        rescued = np.zeros(N, bool)
        if self.rescue:
            peaks = None
            for i in range(POSE):
                if not inside[i] or under[i] >= self.RESCUE_LOW:
                    continue
                if peaks is None:
                    peaks = tracklab.heat_peaks(conf[:, :, :POSE])
                if peaks[i, 2] < self.RESCUE_PEAK:
                    continue
                if math.hypot(peaks[i, 0] - xy[i, 0], peaks[i, 1] - xy[i, 1]) <= self.RESCUE_FAR:
                    continue
                xy[i] = peaks[i, :2]
                under[i] = self.under_at(conf, i, xy[i]) or peaks[i, 2]
                rescued[i] = True
        self.ctx = dict(under=under, inside=inside, rescued=rescued)
        return xy

    def trust(self, pic, width, height):
        """How much each landmark's sample counts (0..1): the heatmap's opinion of its position."""
        under, inside = self.ctx['under'][:POSE], self.ctx['inside'][:POSE]
        heat = np.clip((under - self.H0) / (self.H1 - self.H0), 0, 1)
        # a landmark outside the crop or the picture has no heatmap to speak of: it counts as the model gives it
        seen = inside & (pic[:, 0] >= 0) & (pic[:, 0] < width) & (pic[:, 1] >= 0) & (pic[:, 1] < height)
        return np.where(seen, heat, 1.0)

    @staticmethod
    def facing(xy):
        """How the body faces by its shoulders and hips, in torso lengths: the left side's distance to the right of
        the right side across the body's own axis (hips to shoulders). Positive = facing the camera (a person's left
        is on the picture's right), negative = seen from behind, near 0 = side on."""
        up = (xy[11] + xy[12]) / 2 - (xy[23] + xy[24]) / 2
        n = float(up @ up)
        if n < 1e-6:
            return 0.0, 0.0
        cross = lambda v: (up[0] * v[1] - up[1] * v[0]) / n
        return cross(xy[11] - xy[12]), cross(xy[23] - xy[24])

    def unswap(self, pic, z, vis, world, w, t_ns):
        """A body that clearly faced the camera and, a moment later, is read as clearly seen from behind (left and
        right exchanged) hasn't turned: nobody turns that far in a fifth of a second. The model flipped, and the sides
        are exchanged back, in place, for at most SWAP_MAX frames in a row. "Faced the camera" is the last output whose
        shoulders the model was sure of (a garbage frame in between can't hide the flip), and only that direction is
        undone: a body read as facing the camera again always stands, so a flip can't be locked in."""
        ref = self.face_ref
        if not self.SWAP_GUARD or ref is None or (t_ns - ref[1]) * 1e-9 > self.SWAP_GAP:
            self.swapped = 0
            return False
        if min(w[11], w[12]) < 0.5:
            return False                                 # this frame's shoulders say nothing either way
        now_s, _ = self.facing(pic)
        flipped = ref[0] >= self.SWAP_WIDTH and now_s <= -self.SWAP_WIDTH
        if not flipped or self.swapped >= self.SWAP_MAX:
            if not flipped:
                self.swapped = 0
            return False
        self.swapped += 1
        order = tracklab.SWAP[:POSE]
        pic[:] = pic[order]
        z[:] = z[order]
        vis[:] = vis[order]
        world[:] = world[order]
        w[:] = w[order]
        for key in ('under', 'inside', 'rescued'):
            self.ctx[key][:POSE] = self.ctx[key][:POSE][order]
        return True

    def smooth(self, t_ns, pic, z, vis, world, aux, width, height):
        w = self.trust(pic, width, height)
        self.raw_trust = w.astype(np.float32).copy()
        swapped = self.unswap(pic, z, vis, world, w, t_ns)
        meas = np.concatenate([pic, z[:, None]], axis=1)                    # (33, 3) pixels
        # the body's own move since the last frame: the torso's joints, each by how sure it is
        tw = np.maximum(w[list(TORSO)], 1e-3)
        root = (meas[list(TORSO), :2] * tw[:, None]).sum(0) / tw.sum()
        sure_root = w[list(TORSO)].max() >= 0.5
        move = np.zeros(3)
        if self.prev is not None and sure_root and self.prev['sure_root']:
            move[:2] = root - self.prev['root']
        used, used_world = meas.copy(), world.copy()
        if self.weigh and self.prev is not None:
            for i in range(POSE):
                k = max(self.W_MIN, float(w[i]))
                if k >= 1.0:
                    continue
                pred = self.prev['lm'][i] + move
                used[i] = pred + k * (meas[i] - pred)
                used_world[i] = self.prev['world'][i] + k * (world[i] - self.prev['world'][i])
        if swapped and self.prev is not None:
            used_world = self.prev['world'].copy()      # a body read from behind: its depths are the wrong way round
        f_xy, f_z, f_vis, f_world, f_aux = super().smooth(t_ns, used[:, :2], used[:, 2], vis, used_world, aux, width,
                                                          height)
        # the visibility the games see: the model's, lightly smoothed, capped by the heatmap's opinion of this frame
        out_vis = np.minimum(f_vis, self.VIS_FLOOR + (1 - self.VIS_FLOOR) * w)
        lm = np.concatenate([f_xy, f_z[:, None]], axis=1)
        self.prev = dict(t=t_ns, lm=lm, world=f_world.copy(), root=root, sure_root=sure_root)
        if min(w[11], w[12]) >= 0.5:
            out_s, _ = self.facing(f_xy)
            if abs(out_s) >= self.SWAP_WIDTH:
                self.face_ref = (out_s, t_ns)
        self.last_extra = dict(w=w.astype(np.float32), w_raw=self.raw_trust, under=self.ctx['under'][:POSE].copy(),
                               rescued=self.ctx['rescued'][:POSE].astype(np.float32),
                               swapped=np.float32(1.0 if swapped else 0.0))
        return f_xy, f_z, out_vis, f_world, f_aux

    def process(self, image, t_ns):
        self.last_extra = None
        res = super().process(image, t_ns)
        if res['found'] and self.last_extra is not None:
            res['extra'] = self.last_extra
        return res


def make(name, run, **kw):
    """trust = the box's engine; trust-norescue / trust-noweigh = one part off; trust:<NAME>=<value>,... sets class
    numbers (trust:SWAP_GUARD=0, trust:COAST_FRAMES=0, trust:H0=0.2,H1=0.5 ...)."""
    base, _, settings = name.partition(':')
    options = dict(rescue='norescue' not in base, weigh='noweigh' not in base)
    if not base.startswith('trust'):
        raise ValueError(f'unknown variant {name}')
    cls = type('TrustVariant', (Trust,), {})
    for item in filter(None, settings.split(',')):
        key, value = item.split('=')
        setattr(cls, key, float(value) if '.' in value else int(value))
    return cls(run, **options, **kw)
