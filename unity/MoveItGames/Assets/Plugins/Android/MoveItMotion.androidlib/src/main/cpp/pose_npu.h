// The pose AI on Rockchip's NPU: MediaPipe's pose landmarker (BlazePose) run step by step around its two models,
// converted to RKNN by tools/npu/convert.py. The steps mirror tools/npu/blazepose.py (checked against MediaPipe by
// tools/npu/compare.py): the person detector when nobody is tracked, then a crop around the body for the landmark
// model, the next crop from the landmarks' two alignment points, MediaPipe's smoothing on top.
#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

#include "filters.h"

namespace tundu {

constexpr int kLandmarks = 39;      // 33 pose landmarks + 2 alignment points (33 hips, 34 size and turn) + 4 unused
constexpr int kPoseLandmarks = 33;

struct Rgba {
    const uint8_t* pixels;          // RGBA 8888 (an Android Bitmap)
    int width, height, stride;      // stride in bytes
};

struct Pose {
    bool found = false;
    bool detected = false;          // the person detector ran for this frame
    float score = 0;                // the landmark model's "a pose is there"
    float lm[kPoseLandmarks][5];    // x, y (0..1 of the picture), z (x's scale), visibility, presence
    float world[kPoseLandmarks][3]; // metres, hips at 0
    float roi[4];                   // the crop: centre x, y and size in pixels, rotation in radians
    float detectMs = 0, landmarkMs = 0, totalMs = 0;
};

class PoseNpu {
public:
    PoseNpu();
    ~PoseNpu();
    // The .rknn files' bytes (copied). False = see Error(). initFlags go to rknn_init (RKNN_FLAG_COLLECT_PERF_MASK for
    // PerfDetail, slower).
    bool Load(const void* detector, size_t detectorSize, const void* landmarks, size_t landmarksSize,
              uint32_t initFlags = 0);
    // One camera frame. False = the NPU failed (see Error()); true with found = false = nobody there.
    bool Process(const Rgba& image, int64_t timestampNs, Pose* out);
    // The landmark model alone on the crop roi (centre x, y, size, rotation): no detector, no smoothing, the outputs
    // whatever the presence score (pose_npu_test --crop: a model checked on exactly the crop another runtime saw).
    bool LandmarksAt(const Rgba& image, const float roi[4], Pose* out);
    // Forget the tracked person (the next frame runs the detector).
    void Reset();
    const std::string& Error() const { return error_; }
    std::string Versions() const;
    // Where the last frame's time went: the picture warps and, per model, setting the input, the run (and the NPU's
    // own part of it), reading the outputs.
    std::string Profile() const;
    // RKNN's per-layer report of the last runs (needs RKNN_FLAG_COLLECT_PERF_MASK).
    std::string PerfDetail() const;

private:
    struct Net;
    bool Detect(const Rgba& image, float roi[4], bool* found);
    bool Landmarks(const Rgba& image, const float roi[4], Pose* out, float aux[2][2]);
    void Smooth(const Rgba& image, int64_t timestampNs, Pose* pose, float aux[2][2]);
    void ResetFilters();

    std::unique_ptr<Net> det_, lm_;
    std::vector<float> anchors_;
    bool tracking_ = false;
    float roi_[4] = {0, 0, 0, 0};
    float warpMs_[2] = {0, 0};   // detector, landmarks
    std::string error_;
    std::vector<OneEuroFilter> lmFilters_, worldFilters_;   // x, y, z per landmark
    std::vector<LowPassFilter> visFilters_;
    std::vector<RelativeVelocityFilter> auxFilters_;        // x, y of the two alignment points
};

}  // namespace tundu
