"""MediaPipe's landmark filters in Python, the same formulas as the box engine's filters.h (low pass, One Euro with a
value scale, the relative velocity filter): what MediaPipe's pose landmarker applies in VIDEO mode. For tracklab.py,
which mirrors the box's NPU engine (pose_npu.cpp) on recorded clips.
"""
import math
from collections import deque


class LowPass:
    def __init__(self, alpha=1.0):
        self.alpha = alpha
        self.initialized = False
        self.raw = 0.0
        self.stored = 0.0

    def apply(self, value, alpha=None):
        a = self.alpha if alpha is None else alpha
        result = a * value + (1.0 - a) * self.stored if self.initialized else value
        self.initialized = True
        self.raw = value
        self.stored = result
        return result

    def reset(self):
        self.initialized = False


class OneEuro:
    """value_scale scales the speed (MediaPipe passes 1 / the body's size in pixels), so beta is body-relative."""

    def __init__(self, min_cutoff, beta, derivate_cutoff):
        self.min_cutoff, self.beta, self.derivate_cutoff = min_cutoff, beta, derivate_cutoff
        self.frequency = 30.0
        self.last_ns = 0
        self.x, self.dx = LowPass(), LowPass()

    def _alpha(self, cutoff):
        te, tau = 1.0 / self.frequency, 1.0 / (2.0 * math.pi * cutoff)
        return 1.0 / (1.0 + tau / te)

    def apply(self, t_ns, value_scale, value):
        if self.last_ns >= t_ns:
            return value
        if self.last_ns != 0 and t_ns != 0:
            self.frequency = 1e9 / float(t_ns - self.last_ns)
        self.last_ns = t_ns
        dvalue = (value - self.x.raw) * value_scale * self.frequency if self.x.initialized else 0.0
        edvalue = self.dx.apply(dvalue, self._alpha(self.derivate_cutoff))
        cutoff = self.min_cutoff + self.beta * abs(edvalue)
        return self.x.apply(value, self._alpha(cutoff))

    def reset(self):
        self.x.reset()
        self.dx.reset()
        self.last_ns = 0
        self.frequency = 30.0


class RelativeVelocity:
    """MediaPipe's RelativeVelocityFilter (legacy distance mode): the two alignment points the next crop comes from."""

    def __init__(self, window_size=5, velocity_scale=10.0):
        self.window_size, self.velocity_scale = window_size, velocity_scale
        self.reset()

    def reset(self):
        self.window = deque()
        self.low = LowPass()
        self.last_ns = -1
        self.last_value = 0.0
        self.last_scale = 1.0

    def apply(self, t_ns, value_scale, value):
        if self.last_ns >= t_ns:
            return value
        if self.last_ns == -1:
            alpha = 1.0
        else:
            distance = value * value_scale - self.last_value * self.last_scale
            duration = t_ns - self.last_ns
            cumulative_distance, cumulative_duration = distance, duration
            max_cumulative = (1 + len(self.window)) * (1000000000 // 30)
            for d, dur in self.window:
                if cumulative_duration + dur > max_cumulative:
                    break
                cumulative_distance += d
                cumulative_duration += dur
            velocity = cumulative_distance / (cumulative_duration * 1e-9)
            alpha = 1.0 - 1.0 / (1.0 + self.velocity_scale * abs(velocity))
            self.window.appendleft((distance, duration))
            if len(self.window) > self.window_size:
                self.window.pop()
        self.last_value = value
        self.last_scale = value_scale
        self.last_ns = t_ns
        return self.low.apply(value, alpha)
