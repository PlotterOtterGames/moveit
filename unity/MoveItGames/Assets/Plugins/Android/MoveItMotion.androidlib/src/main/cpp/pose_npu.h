// The pose AI on Rockchip's NPU: MediaPipe's pose landmarker (BlazePose) run step by step around its two models,
// converted to RKNN by tools/npu/convert.py. The steps mirror tools/npu/blazepose.py (checked against MediaPipe by
// tools/npu/compare.py): the person detector when nobody is tracked, then a crop around the body for the landmark
// model, the next crop from the landmarks' two alignment points, MediaPipe's smoothing on top.
//
// Up to two people (2026-11-05, two-player games): each followed person is a track with its own crop box and filters,
// like MediaPipe's pose landmarker with num_poses = 2. The detector finds the people not followed yet (once per
// redetect interval while fewer than wanted are followed: it costs more than a camera frame on the RK3566's NPU), and
// the landmark model runs per person. Its budget is shared: runs per second for everyone (30 = the two take turns,
// each 15 times a second; 0 = everyone every frame), since the RK3566's single NPU core can't run it twice in every
// 30 fps frame while a game draws (docs/hardware.md "Two players").
//
// Beyond MediaPipe (2026-11-10, pose_trust.h; docs/hardware.md "Tracking that listens to the model"): every landmark
// carries a trust from the model's own heatmap, unsure samples are held on the body instead of followed, the visibility
// the games gate on reacts within a frame, a sudden left/right flip is put back, a followed person survives a few
// frames of a low pose score (MediaPipe drops them at the first: then the detector has to find them again, which took
// seconds in a dark room), and the detector looks at averaged, brightened pictures.
#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#include "filters.h"
#include "pose_trust.h"

namespace tundu {

constexpr int kLandmarks = kModelLandmarks;      // 33 pose landmarks + 2 alignment points (33 hips, 34 size and turn) + 4 unused
constexpr int kPoseLandmarks = kBodyLandmarks;
constexpr int kMaxPeople = 2;       // people followed at once

struct Pose {
    bool found = false;
    bool rejected = false;          // a new track that didn't look like someone standing or sitting (the upright check)
    bool coasting = false;          // the pose score dipped: no pose from this frame, the person is still followed
    bool swapped = false;           // the model read the body as seen from behind: this frame's sides were put back
    bool detected = false;          // the person detector ran for this frame
    float trust[kPoseLandmarks];    // how far the model's heatmap backs each landmark's position (0..1, pose_trust.h)
    float score = 0;                // the landmark model's "a pose is there"
    int track = 0;                  // the person's track: the same id while followed, a new one after a loss (0 = none)
    // x, y (0..1 of the picture), z (x's scale), visibility (the model's, lightly smoothed, capped by the trust: what
    // the games gate on), presence
    float lm[kPoseLandmarks][5];
    float world[kPoseLandmarks][3]; // metres, hips at 0
    float roi[4];                   // the crop: centre x, y and size in pixels, rotation in radians
    float detectMs = 0, landmarkMs = 0, totalMs = 0;
    float cropMs = 0;               // the landmark crop (part of landmarkMs, or made ahead by Prepare)
    float npuMs = 0;                // the landmark model's run: the NPU's own time (the busy part of RunJob)
};

// One camera frame: the people the landmark model ran on (with two followed and a budget, not both every frame).
struct Frame {
    int count = 0;                  // poses in people[]
    Pose people[kMaxPeople];
    bool detected = false;          // the person detector ran on this frame
    int followed = 0;               // people followed after this frame
    int lost = 0;                   // people the landmark model stopped seeing on this frame
    int coasting = 0;               // people whose pose score dipped on this frame (no pose from it, still followed)
    int rejected = 0;               // new tracks ended by the upright check on this frame (ghosts: a guitar on a couch)
    bool backingOff = false;        // nobody followed and tracks kept ending young: the detector looks a few times a second
    float detectMs = 0, landmarkMs = 0, totalMs = 0;
    float cropMs = 0, npuMs = 0;    // all of the frame's crops, all of its landmark runs' NPU time
    int Found() const {
        int n = 0;
        for (int i = 0; i < count; i++) n += people[i].found ? 1 : 0;
        return n;
    }
};

class PoseNpu {
public:
    PoseNpu();
    ~PoseNpu();
    // The .rknn files' bytes (copied). False = see Error(). initFlags go to rknn_init (RKNN_FLAG_COLLECT_PERF_MASK for
    // PerfDetail, slower).
    bool Load(const void* detector, size_t detectorSize, const void* landmarks, size_t landmarksSize,
              uint32_t initFlags = 0);
    // How many people to follow (1..kMaxPeople), the landmark model's runs per second shared by them while more than one
    // is followed (0 = each one every frame; one person always runs every frame), and twin: with one person followed and
    // room for two, run the landmark model a second time on their crop and drop that result (a second person's NPU and
    // CPU cost without a second person: PerfLab's twin=1). Fewer than followed: keepTrack (a track id) stays followed,
    // the others end. bench: no real people; maxPeople fixed crops side by side run through the landmark model as if
    // they were followed (results dropped: never found), the NPU and CPU load of that many people with nobody in front
    // of the camera (PerfLab's npubench=1). Call while no RunJob runs (like Process).
    void SetPeople(int maxPeople, float runsPerSecond, bool twin, int keepTrack = 0, bool bench = false);
    int MaxPeople() const { return maxPeople_; }
    // While the budget can't run everyone every frame: the person whose track is trackId gets share of the runs (0.75 =
    // three frames in four), the others the rest, taking turns (2026-11-12: a two-player game's player who has to move
    // now, e.g. Tenis!'s player the ball is coming to, ~22 poses a second instead of 15: their swings are seen sooner and
    // more often). trackId 0 (or a track no longer followed) = everyone takes turns evenly. Any thread.
    void SetFocus(int trackId, float share);
    // One camera frame: the detector (nobody followed: every frame; fewer than wanted: once per redetect interval), then
    // the landmark model on the people due this frame. False = the NPU failed (see Error()).
    bool Process(const Rgba& image, int64_t timestampNs, Frame* out);
    // A frame the detector won't look at itself, for its running average (pose_trust.h DetectorEye): the frames just
    // before a look while nobody is followed and the service only looks a few times a second. Cheap (no NPU). On the
    // thread that calls Process / Prepare.
    void Watch(const Rgba& image, int64_t timestampNs);
    // The same for the first person only (pose_npu_test): found = false = nobody there.
    bool Process(const Rgba& image, int64_t timestampNs, Pose* out);
    // Tracking pipelined over two threads, so a frame's crops and the NPU runs of the frame before overlap (the full model
    // needs both inside a 30 fps frame). Prepare: the landmark crops of the people due this frame, made with the newest
    // crop boxes the tracking has, into a staging slot (and the detector's input, when it is due); returns the slot, or
    // -1 while nobody is followed (then Process() the frame: the detector). It doesn't touch the NPU. The crop boxes come
    // from the last frame RunJob finished: the frame before (MediaPipe's own chain) when the NPU was done in time, else
    // the one before that. RunJob: that slot's crops through the landmark model, smoothing, the next crop boxes, then the
    // detector if it was due (new people start being followed); false = the NPU failed. One RunJob at a time; Process(),
    // Reset() and SetPeople() only while no RunJob runs; Prepare may run beside a RunJob.
    int Prepare(const Rgba& image, int64_t timestampNs);
    bool RunJob(int slot, Frame* out);
    bool RunJob(int slot, Pose* out);   // the first person (pose_npu_test)
    bool Tracking() const;              // someone is followed
    int Followed() const;               // how many
    // The landmark model alone on the crop roi (centre x, y, size, rotation): no detector, no smoothing, the outputs
    // whatever the presence score (pose_npu_test --crop: a model checked on exactly the crop another runtime saw).
    bool LandmarksAt(const Rgba& image, const float roi[4], Pose* out);
    // Forget everyone followed (the next frame runs the detector).
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
    struct Track {               // one followed person: its crop box and its landmarks' smoothing
        bool active = false;
        bool bench = false;      // a fixed crop for measuring (SetPeople's bench)
        int id = 0;
        int frames = 0;          // landmark runs that found the person since the track started (the upright check)
        int low = 0;             // landmark runs in a row with a pose score under the minimum (coasting)
        float roi[4] = {0, 0, 0, 0};
        TrackSmoother smoother;  // MediaPipe's filters behind the trust, the hold and the left/right guard
        void ResetFilters();
    };
    struct Detection {           // a person the detector found: the landmark crop box from its alignment points
        float roi[4];
        float score;
    };
    static constexpr int kMaxDetections = 4;
    struct Job {                 // a frame's landmark crops made ahead of their runs (Prepare → RunJob)
        int count = 0;
        int track[kMaxPeople];   // the track each crop is for, -1 = the twin run
        int id[kMaxPeople];
        float roi[kMaxPeople][4];
        int width = 0, height = 0;
        int64_t timestampNs = 0;
        float cropMs = 0;
        bool detect = false;     // the detector's input is staged too
        float letterbox[3];      // its scale and padding (DetectorInput)
    };
    // The detector's input: the whole picture fitted into its square, black bars; letterbox = scale, pad x, pad y.
    void DetectorInput(const Rgba& image, int64_t timestampNs, uint8_t* dst, float letterbox[3]);
    // The detector on what its input holds: up to kMaxDetections people, best first.
    bool RunDetector(const float letterbox[3], Detection* found, int* count);
    // The landmark model on what its input holds, the crop roi of a width x height picture.
    bool RunLandmarks(const float roi[4], int width, int height, Pose* out, float aux[2][2]);
    // After RunLandmarks: the person is still there (smoothed, with the next crop box), or lost (the track ends).
    bool Follow(int track, int id, int width, int height, int64_t timestampNs, Pose* pose, float aux[2][2]);
    // Which followed people run this frame (track indices, -1 = the twin), within the budget. Under state_.
    int Schedule(int64_t timestampNs, int runs[kMaxPeople]);
    // New tracks for the detections nobody follows yet (as many as there's room for). Under state_.
    void StartTracks(const Detection* found, int count);
    // Two tracks that ended up on one person: the newer one ends. Under state_.
    void DropDuplicates();
    int FollowedLocked() const;
    // A track ended within its first kUprightFrames runs (lost or rejected); BackingOff: kYoungEnds of those lately (the
    // detector then looks only a few times a second while nobody is followed). Under state_.
    void YoungEnd(int64_t timestampNs);
    bool BackingOff(int64_t timestampNs) const;
    // Bench: the fixed crops, once the picture's size is known. Under state_.
    void FillBench(int width, int height);

    std::unique_ptr<Net> det_, lm_;
    std::vector<float> anchors_;
    mutable std::mutex state_;   // the tracks' active, id and roi: RunJob writes them while Prepare reads them
    Track tracks_[kMaxPeople];
    int maxPeople_ = 1;
    float runsPerSecond_ = 0;
    bool twin_ = false;
    bool bench_ = false;
    double credit_ = 0;          // landmark runs saved up (the budget)
    int64_t creditNs_ = 0;
    int nextTurn_ = 0;
    int focusId_ = 0;            // SetFocus: the person who gets more of the runs, and their share
    float focusShare_ = 0.5f;
    double turns_[kMaxPeople] = {0, 0};   // each track's runs owed while someone has the focus (weighted turns)
    int nextId_ = 1;
    int64_t lastDetectNs_ = 0;
    int64_t youngEndNs_[3] = {0, 0, 0};   // the last young ends (kYoungEnds), a ring
    int youngEnds_ = 0;                   // young ends since a track last lived past kUprightFrames
    Job jobs_[2];
    int nextSlot_ = 0;
    float warpMs_[2] = {0, 0};   // detector, landmarks
    // The detector's input (pose_npu.cpp DetectorInput; TUNDU_NPU_BOOST=0: MediaPipe's plain one, =clahe: the local
    // contrast of 2026-11-09), the upright check on new tracks (TUNDU_NPU_UPRIGHT=0), and pose_trust.h's parts:
    // TUNDU_NPU_TRUST=0 (the heatmap's trust, the hold, the quick visibility), TUNDU_NPU_SWAP=0 (the left/right guard),
    // TUNDU_NPU_COAST=0 (a low pose score ends the track at once, as MediaPipe does), TUNDU_NPU_BACKOFF=0 (the detector
    // looks at every frame while nobody is followed, however often tracks end young). All on unless switched off in the
    // environment (pose_npu_test, comparing).
    enum DetInput { kDetPlain, kDetClahe, kDetAveraged };
    DetInput detInput_ = kDetAveraged;
    bool upright_ = true, trust_ = true, swap_ = true, coast_ = true, backoff_ = true;
    // TUNDU_NPU_TRUST's three parts on their own (comparing): TUNDU_NPU_HOLD=0 (unsure samples followed, not held),
    // TUNDU_NPU_CAP=0 (the visibility not capped by the trust), TUNDU_NPU_RESCUE=0 (no landmark moved onto the heatmap's
    // peak elsewhere)
    bool hold_ = true, cap_ = true, rescue_ = true;
    DetectorEye eye_;
    std::vector<uint8_t> detRgb_;   // the detector's letterboxed picture as RGB, before its local contrast (kDetClahe)
    std::string error_;
};

}  // namespace tundu
