// Checks the box's C++ (MoveItMotion.androidlib/src/main/cpp/pose_trust.h) against the lab it was measured in
// (tools/npu/tracklab_trust.py), on the PC, number for number:
//
//   trust_check landmarks <dump>     a tracklab --dump: each frame's model answers through LandmarksFromModel and
//                                    TrackSmoother, against the lab's positions, trust, visibility and world joints
//                                    (with the heatmap as MediaPipe lays it out and as the NPU does)
//   trust_check detector <dump>      a detector_dump.py file: camera frames through DetectorEye, against the lab's
//                                    detector inputs
//
// Build and run in WSL: tools/npu/trust_check.sh (g++, with MoveItMotion.androidlib/src/main/cpp on the include path).
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>

#include "pose_trust.h"

using namespace tundu;

static bool ReadAll(FILE* f, void* into, size_t bytes) { return std::fread(into, 1, bytes, f) == bytes; }

struct Worst {
    double value = 0;
    int frame = -1, index = -1;
    void Add(double v, int f, int i) {
        if (v > value) {
            value = v;
            frame = f;
            index = i;
        }
    }
};

static int Landmarks(const char* path) {
    FILE* f = std::fopen(path, "rb");
    if (!f) {
        std::fprintf(stderr, "can't open %s\n", path);
        return 2;
    }
    char magic[8];
    int32_t head[3];
    if (!ReadAll(f, magic, 8) || std::memcmp(magic, "TRUSTCHK", 8) != 0 || !ReadAll(f, head, sizeof(head))) {
        std::fprintf(stderr, "%s: not a tracklab dump\n", path);
        return 2;
    }
    const int frames = head[0], width = head[1], height = head[2];
    const size_t heatCells = (size_t)kHeatSize * kHeatSize * kModelLandmarks;
    std::vector<float> heat(heatCells), heatFirst(heatCells);
    TrackSmoother smoother[2];      // the heatmap channels last (MediaPipe's layout) and first (the NPU's)
    Worst cropXy, xy, vis, world, trust, layouts;
    int runs = 0, found = 0, swaps = 0, swapMismatch = 0, rescues = 0, rescueMismatch = 0;
    for (int n = 0; n < frames; n++) {
        int64_t t;
        int32_t flags;
        float score, roi[4], raw[kModelLandmarks * 5], worldRaw[kModelLandmarks * 3];
        float eCrop[kModelLandmarks * 2], eUnder[kBodyLandmarks], eRescued[kBodyLandmarks], eXy[kBodyLandmarks * 2];
        float eVis[kBodyLandmarks], eWorld[kBodyLandmarks * 3], eTrust[kBodyLandmarks];
        if (!ReadAll(f, &t, 8) || !ReadAll(f, &flags, 4) || !ReadAll(f, &score, 4) || !ReadAll(f, roi, sizeof(roi)) ||
            !ReadAll(f, raw, sizeof(raw)) || !ReadAll(f, worldRaw, sizeof(worldRaw)) ||
            !ReadAll(f, heat.data(), heatCells * 4) || !ReadAll(f, eCrop, sizeof(eCrop)) ||
            !ReadAll(f, eUnder, sizeof(eUnder)) || !ReadAll(f, eRescued, sizeof(eRescued)) ||
            !ReadAll(f, eXy, sizeof(eXy)) || !ReadAll(f, eVis, sizeof(eVis)) || !ReadAll(f, eWorld, sizeof(eWorld)) ||
            !ReadAll(f, eTrust, sizeof(eTrust))) {
            std::fprintf(stderr, "%s: cut short at frame %d\n", path, n);
            return 2;
        }
        const bool wasFound = flags & 1, ran = flags & 2, wasSwapped = flags & 4, coasting = flags & 8;
        if (flags & 16)   // the detector ran: whoever is followed from here starts fresh
            for (auto& s : smoother) s.Reset();
        if (!ran) continue;
        runs++;
        if (!wasFound) {
            // a dip in the pose score that the lab coasted through keeps the smoothing; lost or rejected ends it
            if (!coasting)
                for (auto& s : smoother) s.Reset();
            continue;
        }
        found++;
        for (int i = 0; i < kModelLandmarks; i++)
            for (int r = 0; r < kHeatSize; r++)
                for (int c = 0; c < kHeatSize; c++)
                    heatFirst[((size_t)i * kHeatSize + r) * kHeatSize + c] = heat[((size_t)r * kHeatSize + c) * kModelLandmarks + i];
        float out[2][kBodyLandmarks][5], outWorld[2][kBodyLandmarks][3], outTrust[2][kBodyLandmarks];
        bool outSwapped[2];
        for (int layout = 0; layout < 2; layout++) {
            float aux[2][2];
            bool rescued[kBodyLandmarks];
            LandmarksFromModel(raw, worldRaw, layout ? heatFirst.data() : heat.data(), layout == 1, roi, width, height,
                               true, out[layout], outWorld[layout], outTrust[layout], aux, rescued);
            if (layout == 0) {
                // the decoding alone: crop positions and the heatmap under them, before any smoothing
                float m[6];
                CropMatrix(roi, m);
                for (int i = 0; i < kBodyLandmarks; i++) {
                    // the lab's crop position (0..1) into the picture, like the C++ did with its own
                    const float X = m[0] * eCrop[i * 2] * kLandmarkInput + m[1] * eCrop[i * 2 + 1] * kLandmarkInput + m[2];
                    const float Y = m[3] * eCrop[i * 2] * kLandmarkInput + m[4] * eCrop[i * 2 + 1] * kLandmarkInput + m[5];
                    cropXy.Add(std::hypot(out[0][i][0] * width - X, out[0][i][1] * height - Y), n, i);
                    // the lab notes the moved landmarks after putting the sides back
                    const bool r = eRescued[wasSwapped ? MirrorIndex(i) : i] > 0.5f;
                    rescues += r;
                    rescueMismatch += r != rescued[i];
                }
            }
            outSwapped[layout] = smoother[layout].Apply(t, width, height, out[layout], outWorld[layout], outTrust[layout],
                                                        aux, true, true);
        }
        swaps += wasSwapped;
        swapMismatch += wasSwapped != outSwapped[0];
        for (int i = 0; i < kBodyLandmarks; i++) {
            xy.Add(std::hypot(out[0][i][0] * width - eXy[i * 2], out[0][i][1] * height - eXy[i * 2 + 1]), n, i);
            vis.Add(std::fabs(out[0][i][3] - eVis[i]), n, i);
            trust.Add(std::fabs(outTrust[0][i] - eTrust[i]), n, i);
            for (int k = 0; k < 3; k++) world.Add(std::fabs(outWorld[0][i][k] - eWorld[i * 3 + k]), n, i);
            for (int k = 0; k < 4; k++) layouts.Add(std::fabs(out[0][i][k] - out[1][i][k]), n, i);
            layouts.Add(outSwapped[0] != outSwapped[1] ? 1.0 : 0.0, n, i);
        }
    }
    std::fclose(f);
    std::printf("%s: %d frames, %d landmark runs, %d with a pose, %d sides put back, %d landmarks moved onto a peak\n",
                path, frames, runs, found, swaps, rescues);
    std::printf("  worst difference from the lab: decoded position %.4f px (frame %d, landmark %d), smoothed position "
                "%.4f px (frame %d, landmark %d), visibility %.5f, trust %.5f, world %.6f m\n",
                cropXy.value, cropXy.frame, cropXy.index, xy.value, xy.frame, xy.index, vis.value, trust.value,
                world.value);
    std::printf("  sides put back differently: %d frames; peaks taken differently: %d landmarks; heatmap layouts apart "
                "by %.6f\n", swapMismatch, rescueMismatch, layouts.value);
    const bool ok = cropXy.value < 0.02 && xy.value < 0.05 && vis.value < 1e-3 && trust.value < 1e-3 &&
                    world.value < 1e-4 && swapMismatch == 0 && rescueMismatch == 0 && layouts.value < 1e-5;
    std::printf("  %s\n", ok ? "OK: the C++ and the lab agree" : "MISMATCH");
    return ok ? 0 : 1;
}

static int Detector(const char* path) {
    FILE* f = std::fopen(path, "rb");
    if (!f) {
        std::fprintf(stderr, "can't open %s\n", path);
        return 2;
    }
    char magic[8];
    int32_t head[4];   // frames, width, height, the detector's input size
    if (!ReadAll(f, magic, 8) || std::memcmp(magic, "EYECHECK", 8) != 0 || !ReadAll(f, head, sizeof(head))) {
        std::fprintf(stderr, "%s: not a detector dump\n", path);
        return 2;
    }
    const int frames = head[0], width = head[1], height = head[2], size = head[3];
    DetectorEye eye(size);
    std::vector<uint8_t> rgb((size_t)width * height * 3), rgba((size_t)width * height * 4), expected((size_t)size * size * 3);
    long worst = 0, differing = 0, total = 0;
    for (int n = 0; n < frames; n++) {
        int64_t t;
        int32_t look;     // 1 = the lab looked at this frame (else it only watched it)
        if (!ReadAll(f, &t, 8) || !ReadAll(f, &look, 4) || !ReadAll(f, rgb.data(), rgb.size()) ||
            !ReadAll(f, expected.data(), expected.size())) {
            std::fprintf(stderr, "%s: cut short at frame %d\n", path, n);
            return 2;
        }
        for (size_t i = 0, m = (size_t)width * height; i < m; i++) {
            rgba[i * 4] = rgb[i * 3];
            rgba[i * 4 + 1] = rgb[i * 3 + 1];
            rgba[i * 4 + 2] = rgb[i * 3 + 2];
            rgba[i * 4 + 3] = 255;
        }
        const Rgba image{rgba.data(), width, height, width * 4};
        if (!look) {
            eye.Watch(image, t);
            continue;
        }
        float letterbox[3];
        const uint8_t* got = eye.Look(image, t, letterbox);
        long frameWorst = 0;
        for (size_t i = 0; i < expected.size(); i++) {
            const long d = std::labs(long(got[i]) - long(expected[i]));
            frameWorst = std::max(frameWorst, d);
            differing += d != 0;
        }
        total += (long)expected.size();
        worst = std::max(worst, frameWorst);
        std::printf("  frame %d: %d frame%s averaged, gamma %.3f, worst difference %ld levels\n", n, eye.Averaged(),
                    eye.Averaged() == 1 ? "" : "s", eye.Gamma(), frameWorst);
    }
    std::fclose(f);
    std::printf("%s: worst difference %ld of 255 levels, %ld of %ld values differ (%.4f %%)\n", path, worst, differing,
                total, total ? 100.0 * differing / total : 0.0);
    // the gamma table can round a level the other way (pow in float against Python's)
    const bool ok = worst <= 1 && differing * 1000 < total;
    std::printf("  %s\n", ok ? "OK: the C++ and the lab agree" : "MISMATCH");
    return ok ? 0 : 1;
}

int main(int argc, char** argv) {
    if (argc == 3 && !std::strcmp(argv[1], "landmarks")) return Landmarks(argv[2]);
    if (argc == 3 && !std::strcmp(argv[1], "detector")) return Detector(argv[2]);
    std::fprintf(stderr, "usage: %s landmarks|detector <dump>\n", argv[0]);
    return 2;
}
