// What the box's pose engine does beyond MediaPipe's own steps (2026-11-10), in plain code with no NPU in it, so the
// same lines run on the box (pose_npu.cpp) and on the PC (tools/npu/trust_check.cpp, against tools/npu/tracklab_trust.py,
// the lab it was measured in on the recorded box-camera clips; docs/hardware.md "Tracking that listens to the model").
//
// 1. The landmark model answers twice: a regression head (where each landmark is) and a heatmap head (a score for every
//    cell of a 64 x 64 grid, per landmark). MediaPipe only lets the heatmap nudge the regression inside a 7 x 7 window.
//    But the heatmap is also the model's opinion of its own answer: at a wrong landmark it almost always shows nothing
//    under it (lite, wrists: AUC 0.98 for "more than a quarter torso off"; the smoothed visibility the games used to
//    gate on: 0.72). So every landmark gets a trust (0..1) from the heatmap's score under its final position, and a
//    landmark with nothing under it moves onto the heatmap's clear peak if there is one elsewhere.
// 2. A sample the model isn't sure of is pulled toward where the landmark was, riding on the body's own move, before
//    MediaPipe's smoothing: sure samples go through exactly as before (same lag), a wrist that flips away for a frame or
//    two doesn't get through, a limb the camera can't see rests instead of flailing.
// 3. The visibility the games gate on is the model's, smoothed lightly (MediaPipe's low pass took ~7 frames each way: a
//    hand that came back counted a quarter second late) and capped by the trust of this very frame.
// 4. A body that clearly faced the camera and a moment later is read as seen from behind (left and right exchanged)
//    hasn't turned: the model flipped (it does when someone walks up close). The sides are put back.
// 5. The person detector's input: the picture shrunk by averaging whole blocks of camera pixels (MediaPipe samples 4 of
//    the ~21 pixels behind each detector pixel: the grain of a dark room stays in), the last few frames averaged on
//    every other look, brightened when dark. In the user's dark room the detector found the player in 63 % of the
//    frames instead of 0.3 %, in normal evening light in 79-86 % instead of 59-66 %, and no ghosts on the empty couch.
#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>

#if defined(__aarch64__)
#include <arm_neon.h>
#endif

#include "filters.h"

namespace tundu {

struct Rgba {
    const uint8_t* pixels;          // RGBA 8888 (an Android Bitmap)
    int width, height, stride;      // stride in bytes
};

constexpr int kModelLandmarks = 39;     // 33 pose landmarks + 2 alignment points + 4 unused
constexpr int kBodyLandmarks = 33;
constexpr int kHeatSize = 64;           // the heatmap's grid
constexpr int kHeatWindow = 7;          // MediaPipe's refinement window
constexpr float kLandmarkInput = 256.f; // the landmark model's crop, pixels

// The heatmap's score under a landmark: under kTrustNone nothing is there, from kTrustSure the model is sure.
constexpr float kTrustNone = 0.15f, kTrustSure = 0.40f;
// What an entirely unsure sample still moves its landmark by (a hidden limb drifts to the model's guess over ~0.5 s
// instead of freezing for good).
constexpr float kTrustFloor = 0.06f;
// A landmark with nothing under it (under kRescueUnder) moves to the heatmap's best peak when that is clear (from
// kRescuePeak) and further than kRescueFar crop sizes away.
constexpr float kRescueUnder = 0.20f, kRescuePeak = 0.45f, kRescueFar = 0.04f;
constexpr float kVisibilityAlpha = 0.5f;        // the visibility's low pass (MediaPipe: 0.1)
constexpr float kMediaPipeVisibilityAlpha = 0.1f;
constexpr float kVisibilityFloor = 0.25f;       // the visibility left when the heatmap shows nothing under a landmark
// The left/right guard: shoulders this far apart across the body (torso lengths, signed: + = facing the camera) both
// before and after, within kSwapGapNs, for at most kSwapMax frames in a row.
constexpr float kSwapWidth = 0.45f;
constexpr int64_t kSwapGapNs = 250000000;
constexpr int kSwapMax = 30;

inline float Sigmoidf(float x) { return 1.f / (1.f + std::exp(-x)); }

// A landmark's index in the mirrored body (left and right exchanged).
inline int MirrorIndex(int i) {
    static const int kPairs[16][2] = {{1, 4}, {2, 5}, {3, 6}, {7, 8}, {9, 10}, {11, 12}, {13, 14}, {15, 16},
                                      {17, 18}, {19, 20}, {21, 22}, {23, 24}, {25, 26}, {27, 28}, {29, 30}, {31, 32}};
    for (const auto& p : kPairs) {
        if (p[0] == i) return p[1];
        if (p[1] == i) return p[0];
    }
    return i;
}

// ---------------------------------------------------------------------------------------------------- the heatmap
// A cell of landmark i's heatmap (logits), as the model lays it out: channels first [39][64][64] (the NPU builds) or
// last [64][64][39] (MediaPipe's own files).
template <typename T>
inline float HeatCell(const T* heat, bool channelsFirst, int i, int row, int col) {
    return float(channelsFirst ? heat[(i * kHeatSize + row) * kHeatSize + col]
                               : heat[(row * kHeatSize + col) * kModelLandmarks + i]);
}

// The score-weighted centre of the 7 x 7 window around a cell (0..1 of the crop), and the window's best score.
template <typename T>
inline bool HeatCentre(const T* heat, bool channelsFirst, int i, int row, int col, float* x, float* y, float* best) {
    const int off = (kHeatWindow - 1) / 2;
    const int r0 = std::max(0, row - off), r1 = std::min(kHeatSize, row + off + 1);
    const int c0 = std::max(0, col - off), c1 = std::min(kHeatSize, col + off + 1);
    float sum = 0, maxConf = 0, wc = 0, wr = 0;
    for (int r = r0; r < r1; r++)
        for (int c = c0; c < c1; c++) {
            const float conf = Sigmoidf(HeatCell(heat, channelsFirst, i, r, c));
            sum += conf;
            maxConf = std::max(maxConf, conf);
            wc += c * conf;
            wr += r * conf;
        }
    *best = maxConf;
    if (sum <= 0) return false;
    *x = wc / kHeatSize / sum;
    *y = wr / kHeatSize / sum;
    return true;
}

// MediaPipe's RefineLandmarksFromHeatmap: the landmark moves to the score-weighted centre of the 7 x 7 window around
// its cell when the window's best score is at least 0.5.
template <typename T>
inline void HeatRefine(const T* heat, bool channelsFirst, int i, float* x, float* y) {
    const int col = int(*x * kHeatSize), row = int(*y * kHeatSize);
    if (col < 0 || col >= kHeatSize || row < 0 || row >= kHeatSize) return;
    float cx, cy, best;
    if (HeatCentre(heat, channelsFirst, i, row, col, &cx, &cy, &best) && best >= 0.5f) {
        *x = cx;
        *y = cy;
    }
}

// The heatmap's best score in the 3 x 3 cells around a position (0..1 of the crop); -1 outside the heatmap.
template <typename T>
inline float HeatUnder(const T* heat, bool channelsFirst, int i, float x, float y) {
    const int c = int(x * kHeatSize + 0.5f), r = int(y * kHeatSize + 0.5f);
    if (c < 0 || c >= kHeatSize || r < 0 || r >= kHeatSize) return -1.f;
    float best = -1e30f;
    for (int rr = std::max(0, r - 1); rr <= std::min(kHeatSize - 1, r + 1); rr++)
        for (int cc = std::max(0, c - 1); cc <= std::min(kHeatSize - 1, c + 1); cc++)
            best = std::max(best, HeatCell(heat, channelsFirst, i, rr, cc));
    return Sigmoidf(best);
}

// The heatmap's best cell for landmark i: the centre of the window around it and its score.
template <typename T>
inline bool HeatPeak(const T* heat, bool channelsFirst, int i, float* x, float* y, float* score) {
    int bestRow = 0, bestCol = 0;
    float best = -1e30f;
    for (int r = 0; r < kHeatSize; r++)
        for (int c = 0; c < kHeatSize; c++) {
            const float v = HeatCell(heat, channelsFirst, i, r, c);
            if (v > best) {
                best = v;
                bestRow = r;
                bestCol = c;
            }
        }
    float unused;
    *score = Sigmoidf(best);
    return HeatCentre(heat, channelsFirst, i, bestRow, bestCol, x, y, &unused);
}

// One landmark out of the model's two answers: (x, y) in 0..1 of the crop. In: the regression's answer. Out: nudged
// by the heatmap (MediaPipe), or moved onto the heatmap's peak (rescue, body landmarks only). Returns the heatmap's
// score under the final position (-1 = outside the heatmap, or no heatmap: nothing to say); *rescued = it moved.
template <typename T>
inline float DecodeLandmark(const T* heat, bool channelsFirst, int i, bool rescue, float* x, float* y, bool* rescued) {
    *rescued = false;
    if (!heat) return -1.f;
    HeatRefine(heat, channelsFirst, i, x, y);
    float under = HeatUnder(heat, channelsFirst, i, *x, *y);
    if (!rescue || i >= kBodyLandmarks || under < 0 || under >= kRescueUnder) return under;
    float px, py, score;
    if (!HeatPeak(heat, channelsFirst, i, &px, &py, &score) || score < kRescuePeak) return under;
    if (std::hypot(px - *x, py - *y) <= kRescueFar) return under;
    *x = px;
    *y = py;
    *rescued = true;
    const float there = HeatUnder(heat, channelsFirst, i, px, py);
    return there > 0 ? there : score;
}

// The trust of a landmark (0..1): the heatmap's score under it; a landmark outside the crop or the picture has no
// heatmap to speak of and counts as the model gives it.
inline float TrustOf(float under, float pictureX, float pictureY, int width, int height) {
    if (under < 0 || pictureX < 0 || pictureX >= width || pictureY < 0 || pictureY >= height) return 1.f;
    return std::min(1.f, std::max(0.f, (under - kTrustNone) / (kTrustSure - kTrustNone)));
}

// Takes a point of the 256 x 256 crop to the picture: the rotated square crop box roi (centre x, y and size in pixels,
// rotation in radians). Also the landmarks' way back.
inline void CropMatrix(const float roi[4], float m[6]) {
    const float k = roi[2] / kLandmarkInput, c = std::cos(roi[3]) * k, s = std::sin(roi[3]) * k, h = kLandmarkInput * 0.5f;
    m[0] = c;
    m[1] = -s;
    m[2] = roi[0] - c * h + s * h;
    m[3] = s;
    m[4] = c;
    m[5] = roi[1] - s * h - c * h;
}

// The landmark model's answers for the crop roi of a width x height picture, brought into the picture. In: raw
// (39 x 5: x, y, z in crop pixels, visibility and presence as logits), worldRaw (39 x 3, metres, in the crop's frame),
// heat (the heatmap's logits, or null). Out, for the 33 body landmarks: lm (x, y in 0..1 of the picture, z in x's
// scale, visibility, presence), world (turned by the crop's rotation), trust; and aux, the two alignment points the
// next crop comes from, in pixels. useTrust false = MediaPipe's own decoding (no rescue, every trust 1).
template <typename T>
inline void LandmarksFromModel(const float* raw, const float* worldRaw, const T* heat, bool channelsFirst,
                               const float roi[4], int width, int height, bool useTrust, float lm[][5],
                               float world[][3], float* trust, float aux[2][2], bool* rescued = nullptr,
                               bool rescue = true) {
    float m[6];
    CropMatrix(roi, m);
    const float c = std::cos(roi[3]), s = std::sin(roi[3]);
    for (int i = 0; i < kModelLandmarks; i++) {
        const float* r = raw + i * 5;
        float x = r[0] / kLandmarkInput, y = r[1] / kLandmarkInput;
        bool moved = false;
        const float under = DecodeLandmark(heat, channelsFirst, i, useTrust && rescue, &x, &y, &moved);
        const float X = m[0] * x * kLandmarkInput + m[1] * y * kLandmarkInput + m[2];
        const float Y = m[3] * x * kLandmarkInput + m[4] * y * kLandmarkInput + m[5];
        if (i == 33 || i == 34) {
            aux[i - 33][0] = X;
            aux[i - 33][1] = Y;
        }
        if (i >= kBodyLandmarks) continue;
        if (rescued) rescued[i] = moved;
        trust[i] = useTrust ? TrustOf(under, X, Y, width, height) : 1.f;
        lm[i][0] = X / width;
        lm[i][1] = Y / height;
        lm[i][2] = r[2] / kLandmarkInput * roi[2] / width;
        lm[i][3] = Sigmoidf(r[3]);
        lm[i][4] = Sigmoidf(r[4]);
        const float* w = worldRaw + i * 3;
        world[i][0] = c * w[0] - s * w[1];
        world[i][1] = s * w[0] + c * w[1];
        world[i][2] = w[2];
    }
}

// ------------------------------------------------------------------------------------------------ a followed person
// One followed person's landmarks from frame to frame: MediaPipe's pose filtering (visibility low-passed, landmarks One
// Euro in picture pixels with speeds relative to the body's size, world landmarks One Euro in metres, the alignment
// points through the relative velocity filter), with the trust, the body-riding hold and the left/right guard in front.
class TrackSmoother {
public:
    TrackSmoother() {
        for (int i = 0; i < kBodyLandmarks * 3; i++) {
            lm_.emplace_back(0.05, 80.0, 1.0);     // MediaPipe's pose landmarks (scaled by the body's size)
            world_.emplace_back(0.1, 40.0, 1.0);   // and world landmarks (no scaling)
        }
        vis_.assign(kBodyLandmarks, LowPassFilter(1.0));
        for (int i = 0; i < 4; i++) aux_.emplace_back(5, 10.f);
    }

    void Reset() {
        for (auto& f : lm_) f.Reset();
        for (auto& f : world_) f.Reset();
        for (auto& f : vis_) f.Reset();
        for (auto& f : aux_) f.Reset();
        hasPrev_ = hasFace_ = false;
        swapped_ = 0;
    }

    // How the body faces by its shoulders: the left shoulder's distance to the right of the right one across the
    // body's own axis (hips to shoulders), in torso lengths. + = facing the camera (a person's left is on the picture's
    // right), - = seen from behind, near 0 = side on. xy: landmark positions, any unit.
    template <typename P>
    static double Facing(const P& xy) {
        const double ux = (xy[11][0] + xy[12][0]) * 0.5 - (xy[23][0] + xy[24][0]) * 0.5;
        const double uy = (xy[11][1] + xy[12][1]) * 0.5 - (xy[23][1] + xy[24][1]) * 0.5;
        const double n = ux * ux + uy * uy;
        if (n < 1e-6) return 0.0;
        return (ux * (xy[11][1] - xy[12][1]) - uy * (xy[11][0] - xy[12][0])) / n;
    }

    // One frame. lm: x, y (0..1 of the picture), z (x's scale), visibility, presence per landmark, as the model gave
    // them; world: metres; trust: per landmark (TrustOf); aux: the two alignment points in pixels. All smoothed in place
    // (lm's visibility becomes what the games see). useTrust false = MediaPipe's filtering alone; useSwap: the
    // left/right guard. Returns true when this frame's sides were put back.
    bool Apply(int64_t timestampNs, int width, int height, float lm[][5], float world[][3], float* trust,
               float aux[2][2], bool useTrust, bool useSwap, bool hold = true, bool cap = true) {
        const double size[3] = {double(width), double(height), double(width)};
        double meas[kBodyLandmarks][3];
        for (int i = 0; i < kBodyLandmarks; i++)
            for (int k = 0; k < 3; k++) meas[i][k] = double(lm[i][k]) * size[k];
        float one[kBodyLandmarks];
        if (!useTrust) {
            std::fill(one, one + kBodyLandmarks, 1.f);
            trust = one;
        }
        const bool swapped = useTrust && useSwap && Unswap(timestampNs, meas, lm, world, trust);

        // the body's own move since the last frame: the torso's joints, each by how sure it is
        static const int kTorso[4] = {11, 12, 23, 24};
        double root[2] = {0, 0}, total = 0;
        float surest = 0;
        for (int j : kTorso) {
            const double w = std::max(double(trust[j]), 1e-3);
            root[0] += meas[j][0] * w;
            root[1] += meas[j][1] * w;
            total += w;
            surest = std::max(surest, trust[j]);
        }
        root[0] /= total;
        root[1] /= total;
        const bool sureRoot = surest >= 0.5f;
        if (useTrust && hasPrev_) {
            double move[2] = {0, 0};
            if (sureRoot && sureRoot_) {
                move[0] = root[0] - root_[0];
                move[1] = root[1] - root_[1];
            }
            for (int i = 0; i < kBodyLandmarks && hold; i++) {
                const double k = std::max(kTrustFloor, trust[i]);
                if (k >= 1.0) continue;
                for (int c = 0; c < 3; c++) {
                    const double pred = prevLm_[i][c] + (c < 2 ? move[c] : 0.0);
                    meas[i][c] = pred + k * (meas[i][c] - pred);
                    world[i][c] = float(prevWorld_[i][c] + k * (double(world[i][c]) - prevWorld_[i][c]));
                }
            }
            // a body read from behind: its depths are the wrong way round
            if (swapped)
                for (int i = 0; i < kBodyLandmarks; i++)
                    for (int c = 0; c < 3; c++) world[i][c] = float(prevWorld_[i][c]);
        }

        // MediaPipe's filtering
        const float box = 2.f * std::hypot(aux[1][0] - aux[0][0], aux[1][1] - aux[0][1]);
        const double scale = box > 1e-6f ? 1.0 / box : 0.0;
        const double visAlpha = useTrust ? kVisibilityAlpha : kMediaPipeVisibilityAlpha;
        for (int i = 0; i < kBodyLandmarks; i++) {
            float visibility = float(vis_[i].ApplyWithAlpha(lm[i][3], visAlpha));
            if (useTrust && cap) visibility = std::min(visibility, kVisibilityFloor + (1.f - kVisibilityFloor) * trust[i]);
            lm[i][3] = visibility;
            for (int k = 0; k < 3; k++) {
                if (scale > 0) meas[i][k] = lm_[i * 3 + k].Apply(timestampNs, scale, meas[i][k]);
                lm[i][k] = float(meas[i][k] / size[k]);
                world[i][k] = float(world_[i * 3 + k].Apply(timestampNs, 1.0, world[i][k]));
            }
        }
        if (scale > 0)
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 2; k++) aux[j][k] = aux_[j * 2 + k].Apply(timestampNs, float(scale), aux[j][k]);

        // what the next frame holds on to
        for (int i = 0; i < kBodyLandmarks; i++)
            for (int k = 0; k < 3; k++) {
                prevLm_[i][k] = meas[i][k];
                prevWorld_[i][k] = world[i][k];
            }
        root_[0] = root[0];
        root_[1] = root[1];
        sureRoot_ = sureRoot;
        hasPrev_ = true;
        if (std::min(trust[11], trust[12]) >= 0.5f) {
            const double facing = Facing(prevLm_);
            if (std::fabs(facing) >= kSwapWidth) {
                faceRef_ = facing;
                faceNs_ = timestampNs;
                hasFace_ = true;
            }
        }
        return swapped;
    }

private:
    // The left/right guard: the body clearly faced the camera (the last output whose shoulders the model was sure of: a
    // garbage frame in between can't hide the flip), and now it's read as clearly seen from behind, within a quarter
    // second: nobody turns that far that fast. Only that direction is undone (a body read as facing the camera again
    // always stands, so a flip can't be locked in), for at most kSwapMax frames in a row.
    bool Unswap(int64_t timestampNs, double meas[][3], float lm[][5], float world[][3], float* trust) {
        if (!hasFace_ || timestampNs - faceNs_ > kSwapGapNs) {
            swapped_ = 0;
            return false;
        }
        if (std::min(trust[11], trust[12]) < 0.5f) return false;   // this frame's shoulders say nothing either way
        const bool flipped = faceRef_ >= kSwapWidth && Facing(meas) <= -kSwapWidth;
        if (!flipped || swapped_ >= kSwapMax) {
            if (!flipped) swapped_ = 0;
            return false;
        }
        swapped_++;
        for (int i = 0; i < kBodyLandmarks; i++) {
            const int j = MirrorIndex(i);
            if (j <= i) continue;
            for (int k = 0; k < 3; k++) {
                std::swap(meas[i][k], meas[j][k]);
                std::swap(lm[i][k], lm[j][k]);
                std::swap(world[i][k], world[j][k]);
            }
            std::swap(lm[i][3], lm[j][3]);
            std::swap(lm[i][4], lm[j][4]);
            std::swap(trust[i], trust[j]);
        }
        return true;
    }

    std::vector<OneEuroFilter> lm_, world_;
    std::vector<LowPassFilter> vis_;
    std::vector<RelativeVelocityFilter> aux_;
    bool hasPrev_ = false, sureRoot_ = false, hasFace_ = false;
    double prevLm_[kBodyLandmarks][3] = {}, prevWorld_[kBodyLandmarks][3] = {}, root_[2] = {0, 0};
    double faceRef_ = 0;
    int64_t faceNs_ = 0;
    int swapped_ = 0;
};

// ------------------------------------------------------------------------------------------- the detector's input
// The person detector's input from the camera's pictures: each shrunk into the detector's square by averaging whole
// blocks of camera pixels, the last few kept; a look takes the newest alone or the average of those of the last 0.15 s
// (the looks take turns: someone walking in is sharpest in one frame, a dark room's grain goes in the average), and
// brightens it toward a middle grey when it's darker.
class DetectorEye {
public:
    static constexpr int kFrames = 4;
    static constexpr int64_t kWindowNs = 150000000;
    static constexpr double kTargetMean = 0.45, kStrongestGamma = 0.5;

    explicit DetectorEye(int size) : size_(size) {
        for (auto& f : ring_) f.assign((size_t)size * size * 3, 0);
        mean_.assign((size_t)size * size * 3, 0);
    }

    // A frame for the average (the frames just before a look, while nobody is followed).
    void Watch(const Rgba& image, int64_t timestampNs) { Shrink(image, timestampNs); }

    // This frame's look: the detector's input as RGB (size x size x 3, black bars), and the letterbox (scale, pad x,
    // pad y: a picture position p is at p * scale + pad in the input).
    const uint8_t* Look(const Rgba& image, int64_t timestampNs, float letterbox[3]) {
        const int newest = Shrink(image, timestampNs);
        letterbox[0] = float(scale_);
        letterbox[1] = float(padX_);
        letterbox[2] = float(padY_);
        looks_++;
        const size_t n = mean_.size();
        int count = 0;
        if (looks_ % 2 == 1) {
            std::memcpy(mean_.data(), ring_[newest].data(), n);
            count = 1;
        } else {
            total_.assign(n, 0);
            uint16_t* sum = total_.data();
            for (int k = 0; k < kFrames; k++) {
                if (!used_[k] || timestampNs - ns_[k] > kWindowNs || ns_[k] > timestampNs) continue;
                const uint8_t* f = ring_[k].data();
                for (size_t i = 0; i < n; i++) sum[i] = uint16_t(sum[i] + f[i]);
                count++;
            }
            for (size_t i = 0; i < n; i++) mean_[i] = uint8_t(sum[i] / count);
        }
        averaged_ = count;
        // brighter when dark: a gamma that takes the picture's mean to the target, at most the strongest
        double total = 0;
        for (int v = rect_[1]; v < rect_[3]; v++) {
            const uint8_t* p = mean_.data() + ((size_t)v * size_ + rect_[0]) * 3;
            for (int i = 0, m = (rect_[2] - rect_[0]) * 3; i < m; i++) total += p[i];
        }
        const double pixels = double(rect_[2] - rect_[0]) * (rect_[3] - rect_[1]) * 3;
        const double mean = pixels > 0 ? total / pixels / 255.0 : 1.0;
        gamma_ = 1.0;
        if (mean > 1e-3 && mean < kTargetMean) {
            gamma_ = std::max(kStrongestGamma, std::log(kTargetMean) / std::log(mean));
            uint8_t lut[256];
            for (int i = 0; i < 256; i++) lut[i] = uint8_t(255.0 * std::pow(i / 255.0, gamma_));
            for (size_t i = 0; i < n; i++) mean_[i] = lut[mean_[i]];
        }
        return mean_.data();
    }

    int Averaged() const { return averaged_; }     // frames in the last look
    double Gamma() const { return gamma_; }         // its brightening (1 = none)
    const int* Rect() const { return rect_; }       // the picture's area in the input: x0, y0, x1, y1
    void Simd(bool on) { simd_ = on; }              // false = the plain C sums (what the ARM ones are checked against)

private:
    // The picture into a slot of the ring (the one with this timestamp, else the oldest); returns the slot.
    int Shrink(const Rgba& image, int64_t timestampNs) {
        const int w = image.width, h = image.height;
        if (w != width_ || h != height_) {   // another camera size: the kept frames no longer fit
            width_ = w;
            height_ = h;
            for (bool& u : used_) u = false;
            scale_ = double(size_) / std::max(w, h);
            padX_ = (size_ - w * scale_) / 2;
            padY_ = (size_ - h * scale_) / 2;
            rect_[0] = (int)std::ceil(padX_);
            rect_[1] = (int)std::ceil(padY_);
            rect_[2] = std::min(size_, (int)std::floor(padX_ + w * scale_));
            rect_[3] = std::min(size_, (int)std::floor(padY_ + h * scale_));
        }
        int slot = -1;
        for (int k = 0; k < kFrames; k++)
            if (used_[k] && ns_[k] == timestampNs) slot = k;
        if (slot < 0) {
            slot = 0;
            for (int k = 0; k < kFrames; k++) {
                if (!used_[k]) {
                    slot = k;
                    break;
                }
                if (ns_[k] < ns_[slot]) slot = k;
            }
        }
        used_[slot] = true;
        ns_[slot] = timestampNs;
        uint8_t* out = ring_[slot].data();
        std::memset(out, 0, ring_[slot].size());
        // each input pixel inside the picture's area: the average of the b x b camera pixels around its centre, b =
        // the whole pixels it covers (4 of 4.57 for a 1024 px wide picture). The camera rows are read one at a time,
        // in order, into a line of sums per input pixel: with the b rows of a block read side by side, the box's
        // in-order cores stalled on every cache line (pose_npu_test --selftest: ~10-13 ms of CPU a frame at 1.4 GHz
        // with a game running, whatever the arithmetic cost).
        const int b = std::max(1, int(1.0 / scale_));
        const int area = b * b, half = area / 2;
        const int x0 = rect_[0], x1 = rect_[2], n = x1 - x0;
        if (n <= 0) return slot;
        cx_.resize((size_t)size_);
        for (int u = x0; u < x1; u++)
            cx_[(size_t)u] = std::min(std::max(int(std::floor((u + 0.5 - padX_) / scale_ - b / 2.0 + 0.5)), 0), w - b) * 4;
        const int* cx = cx_.data() + x0;        // byte offsets in a camera row
        sums_.resize((size_t)n * 4);
        for (int v = rect_[1]; v < rect_[3]; v++) {
            const int cy = std::min(std::max(int(std::floor((v + 0.5 - padY_) / scale_ - b / 2.0 + 0.5)), 0), h - b);
            uint8_t* o = out + ((size_t)v * size_ + x0) * 3;
#if defined(__aarch64__)
            // 4 x 4 blocks (the box camera's 1024 px picture): a block's four pixels of a row in one load
            if (b == 4 && simd_) {
                uint16_t* sums = sums_.data();      // R, G, B, A per input pixel: at most 16 x 255
                for (int y = 0; y < 4; y++) {
                    const uint8_t* row = image.pixels + (size_t)(cy + y) * image.stride;
                    uint16_t* q = sums;
                    if (y == 0) {
                        for (int i = 0; i < n; i++, q += 4) {
                            const uint8x16_t a = vld1q_u8(row + cx[i]);
                            const uint16x8_t s = vaddl_u8(vget_low_u8(a), vget_high_u8(a));  // pixels 0 + 2, 1 + 3
                            vst1_u16(q, vadd_u16(vget_low_u16(s), vget_high_u16(s)));
                        }
                    } else {
                        for (int i = 0; i < n; i++, q += 4) {
                            const uint8x16_t a = vld1q_u8(row + cx[i]);
                            const uint16x8_t s = vaddl_u8(vget_low_u8(a), vget_high_u8(a));
                            vst1_u16(q, vadd_u16(vld1_u16(q), vadd_u16(vget_low_u16(s), vget_high_u16(s))));
                        }
                    }
                }
                const uint16_t* q = sums;
                for (int i = 0; i < n; i++, q += 4, o += 3) {
                    o[0] = uint8_t((q[0] + 8) >> 4);
                    o[1] = uint8_t((q[1] + 8) >> 4);
                    o[2] = uint8_t((q[2] + 8) >> 4);
                }
                continue;
            }
#endif
            wide_.assign((size_t)n * 3, 0);
            uint32_t* sums = wide_.data();
            for (int y = 0; y < b; y++) {
                const uint8_t* row = image.pixels + (size_t)(cy + y) * image.stride;
                uint32_t* q = sums;
                for (int i = 0; i < n; i++, q += 3) {
                    const uint8_t* p = row + cx[i];
                    for (int x = 0; x < b; x++, p += 4) {
                        q[0] += p[0];
                        q[1] += p[1];
                        q[2] += p[2];
                    }
                }
            }
            for (int i = 0; i < n * 3; i++) o[i] = uint8_t((sums[i] + (uint32_t)half) / (uint32_t)area);
        }
        return slot;
    }

    int size_;
    int width_ = 0, height_ = 0;
    double scale_ = 1, padX_ = 0, padY_ = 0;
    int rect_[4] = {0, 0, 0, 0};
    std::vector<uint8_t> ring_[kFrames], mean_;
    std::vector<int> cx_;                   // Shrink's and Look's working lines, kept between frames
    std::vector<uint16_t> sums_, total_;
    std::vector<uint32_t> wide_;
    int64_t ns_[kFrames] = {0, 0, 0, 0};
    bool used_[kFrames] = {false, false, false, false};
    int looks_ = 0, averaged_ = 0;
    double gamma_ = 1.0;
    bool simd_ = true;
};

}  // namespace tundu
