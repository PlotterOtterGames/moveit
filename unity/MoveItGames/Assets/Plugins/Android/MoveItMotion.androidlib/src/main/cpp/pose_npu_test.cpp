// The NPU pose engine from the box's shell, without the app (built by tools/npu/build_native.py):
//   pose_npu_test <detector.rknn> <landmarks.rknn> <label> <out_dir> <name.WxH.rgba>... [--loops N] [--frames F]
//                 [--crop]
// Each picture (raw RGBA, its size in the name) as F frames in a row (1: the detector, 2: then the crop from the
// landmarks, smoothed): the last one into <out_dir>/<name>.<label>.json for tools/npu/compare.py --box. Then timings
// on the first picture: the detector + landmarks every frame, and tracking (landmarks only), N frames each.
// --crop: each picture is a landmark model crop (compare.py --write-crops): the landmark model alone on it as it is,
// landmarks in the crop's pixels, for compare.py --crops (no timings).
//   pose_npu_test <detector.rknn> <landmarks.rknn> <label> <out_dir> --clip <clip.mjpg> [--order serial|pipelined|threads]
// A raw camera clip (tools/motion/box_clip.py; capture times from the <clip>.log beside it) frame by frame, every pose
// into <out_dir>/<clip>.<label>.jsonl in the recorder's format (tools/motion/model_errors.py, shake_report.py).
// serial: MediaPipe's own order (each crop from the frame before); pipelined: each crop made before the frame before
// has run (always one frame older: what the app does when the NPU isn't done in time, the worst case); threads: the
// app's two threads for real, as fast as the frames decode (checks the engine's locking; crops as they come).
#include <android/bitmap.h>
#include <android/imagedecoder.h>

#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include "pose_npu.h"
#include "rknn_api.h"

using tundu::Pose;
using tundu::PoseNpu;

static std::vector<uint8_t> ReadFile(const std::string& path) {
    std::vector<uint8_t> data;
    FILE* f = std::fopen(path.c_str(), "rb");
    if (!f) return data;
    std::fseek(f, 0, SEEK_END);
    data.resize((size_t)std::ftell(f));
    std::fseek(f, 0, SEEK_SET);
    if (std::fread(data.data(), 1, data.size(), f) != data.size()) data.clear();
    std::fclose(f);
    return data;
}

struct Picture {
    std::string name;
    int width = 0, height = 0;
    std::vector<uint8_t> pixels;
};

static bool LoadPicture(const std::string& path, Picture* p) {
    std::string base = path.substr(path.find_last_of('/') + 1);
    size_t ext = base.rfind(".rgba"), dot = base.rfind('.', ext - 1);
    if (ext == std::string::npos || dot == std::string::npos) return false;
    if (std::sscanf(base.c_str() + dot + 1, "%dx%d", &p->width, &p->height) != 2) return false;
    p->name = base.substr(0, dot);
    p->pixels = ReadFile(path);
    return p->pixels.size() == (size_t)p->width * p->height * 4;
}

static void WriteJson(const std::string& path, const Pose& pose, const Picture& pic) {
    FILE* f = std::fopen(path.c_str(), "w");
    if (!f) return;
    std::fprintf(f, "{\"found\": %s, \"score\": %.5f, \"detectMs\": %.2f, \"landmarkMs\": %.2f,\n \"xy\": [",
                 pose.found ? "true" : "false", pose.score, pose.detectMs, pose.landmarkMs);
    for (int i = 0; i < tundu::kPoseLandmarks; i++)
        std::fprintf(f, "%s[%.3f, %.3f]", i ? ", " : "", pose.lm[i][0] * pic.width, pose.lm[i][1] * pic.height);
    std::fprintf(f, "],\n \"world\": [");
    for (int i = 0; i < tundu::kPoseLandmarks; i++)
        std::fprintf(f, "%s[%.5f, %.5f, %.5f]", i ? ", " : "", pose.world[i][0], pose.world[i][1], pose.world[i][2]);
    std::fprintf(f, "],\n \"visibility\": [");
    for (int i = 0; i < tundu::kPoseLandmarks; i++) std::fprintf(f, "%s%.4f", i ? ", " : "", pose.lm[i][3]);
    std::fprintf(f, "],\n \"roi\": [%.2f, %.2f, %.2f, %.4f]}\n", pose.roi[0], pose.roi[1], pose.roi[2], pose.roi[3]);
    std::fclose(f);
}

struct Stat {
    double sum = 0, min = 1e9, max = 0;
    int n = 0;
    void Add(double v) {
        sum += v;
        n++;
        if (v < min) min = v;
        if (v > max) max = v;
    }
    void Print(const char* what) const {
        if (n) std::printf("  %-26s %6.2f ms mean, %6.2f min, %6.2f max (%d frames)\n", what, sum / n, min, max, n);
    }
};

// ---------------------------------------------------------------- clips
static double Ms() {
    return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

// The JPEG frames of an MJPEG stream: (offset, length) from each start-of-image to the next.
static std::vector<std::pair<size_t, size_t>> SplitJpegs(const std::vector<uint8_t>& data) {
    std::vector<size_t> starts;
    for (size_t i = 0; i + 2 < data.size(); i++)
        if (data[i] == 0xFF && data[i + 1] == 0xD8 && data[i + 2] == 0xFF) starts.push_back(i);
    std::vector<std::pair<size_t, size_t>> frames;
    for (size_t k = 0; k < starts.size(); k++)
        frames.push_back({starts[k], (k + 1 < starts.size() ? starts[k + 1] : data.size()) - starts[k]});
    return frames;
}

// v4l2-ctl --verbose: "ts: 1922.690083" per frame (seconds).
static std::vector<double> CaptureTimes(const std::string& logPath) {
    std::vector<double> times;
    FILE* f = std::fopen(logPath.c_str(), "r");
    if (!f) return times;
    char line[512];
    while (std::fgets(line, sizeof(line), f)) {
        const char* p = std::strstr(line, "ts: ");
        if (p) times.push_back(std::atof(p + 4));
    }
    std::fclose(f);
    return times;
}

struct Frame {
    std::vector<uint8_t> pixels;
    int width = 0, height = 0, stride = 0;
};

static bool Decode(const uint8_t* jpeg, size_t size, Frame* out) {
    AImageDecoder* dec = nullptr;
    if (AImageDecoder_createFromBuffer(jpeg, size, &dec) != ANDROID_IMAGE_DECODER_SUCCESS) return false;
    const AImageDecoderHeaderInfo* info = AImageDecoder_getHeaderInfo(dec);
    out->width = AImageDecoderHeaderInfo_getWidth(info);
    out->height = AImageDecoderHeaderInfo_getHeight(info);
    AImageDecoder_setAndroidBitmapFormat(dec, ANDROID_BITMAP_FORMAT_RGBA_8888);
    out->stride = (int)AImageDecoder_getMinimumStride(dec);
    out->pixels.resize((size_t)out->stride * out->height);
    const int ret = AImageDecoder_decodeImage(dec, out->pixels.data(), out->stride, out->pixels.size());
    AImageDecoder_delete(dec);
    return ret == ANDROID_IMAGE_DECODER_SUCCESS;
}

// One line of the recorder's format (tools/motion: 17 COCO joints x, y, z, visibility and world joints).
static void WriteLine(FILE* f, const std::string& label, int n, double seconds, int width, int height,
                      const Pose& pose) {
    static const int kCoco[17] = {0, 2, 5, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28};
    std::fprintf(f, "{\"v\":1,\"src\":\"box-npu-%s\",\"t\":%lld,\"f\":%d,\"w\":%d,\"h\":%d,\"inf\":%.2f,\"crop\":%.2f,"
                    "\"npu\":%.2f,\"det\":%d,\"people\":[",
                 label.c_str(), 1800000000000LL + (long long)std::llround(seconds * 1000), n, width, height,
                 pose.totalMs, pose.cropMs, pose.npuMs, pose.detected ? 1 : 0);
    if (pose.found) {
        std::fprintf(f, "{\"id\":0,\"score\":%.3f,\"k\":[", pose.score);
        for (int j = 0; j < 17; j++) {
            const float* l = pose.lm[kCoco[j]];
            std::fprintf(f, "%s%.5f,%.5f,%.5f,%.4f", j ? "," : "", l[0], l[1], l[2], l[3]);
        }
        std::fprintf(f, "],\"wk\":[");
        for (int j = 0; j < 17; j++) {
            const float* w = pose.world[kCoco[j]];
            std::fprintf(f, "%s%.5f,%.5f,%.5f", j ? "," : "", w[0], w[1], w[2]);
        }
        std::fprintf(f, "]}");
    }
    std::fprintf(f, "]}\n");
}

static int RunClip(PoseNpu& npu, const std::string& label, const std::string& outDir, const std::string& clipPath,
                   const std::string& order) {
    const std::vector<uint8_t> data = ReadFile(clipPath);
    const auto jpegs = SplitJpegs(data);
    std::string log = clipPath.substr(0, clipPath.rfind('.')) + ".log";
    std::vector<double> times = CaptureTimes(log);
    if (times.size() < jpegs.size()) {
        times.clear();
        for (size_t i = 0; i < jpegs.size(); i++) times.push_back(i / 30.0);
    }
    const std::string base = clipPath.substr(clipPath.find_last_of('/') + 1);
    const std::string outPath = outDir + "/" + base.substr(0, base.rfind('.')) + "." + label + ".jsonl";
    FILE* out = std::fopen(outPath.c_str(), "w");
    if (!out || jpegs.empty()) {
        std::fprintf(stderr, "can't read %s or write %s\n", clipPath.c_str(), outPath.c_str());
        return 2;
    }
    auto seconds = [&](size_t n) { return times[n] - times[0]; };
    auto ns = [&](size_t n) { return (int64_t)std::llround(seconds(n) * 1e9) + 1; };
    Stat decode, crop, run, frame;
    int found = 0, detections = 0;
    const double started = Ms();
    Frame fr;
    Pose pose;
    if (!Decode(data.data() + jpegs[0].first, jpegs[0].second, &fr)) {
        std::fprintf(stderr, "can't decode the first frame of %s\n", clipPath.c_str());
        return 2;
    }
    const int W = fr.width, H = fr.height;
    if (order == "threads") {
        // the app's shape: this thread decodes and crops, a worker runs the NPU; one job queued at most
        std::mutex m;
        std::condition_variable cv;
        std::deque<std::pair<int, size_t>> jobs;   // slot, frame
        bool busy = false, done = false;
        std::thread worker([&] {
            Pose p;
            for (;;) {
                std::pair<int, size_t> job;
                {
                    std::unique_lock<std::mutex> lock(m);
                    cv.wait(lock, [&] { return done || !jobs.empty(); });
                    if (jobs.empty()) return;
                    job = jobs.front();
                    jobs.pop_front();
                    busy = true;
                }
                npu.RunJob(job.first, &p);
                crop.Add(p.cropMs);
                run.Add(p.totalMs);
                WriteLine(out, label, (int)job.second, seconds(job.second), W, H, p);
                std::lock_guard<std::mutex> lock(m);
                found += p.found;
                busy = false;
                cv.notify_all();
            }
        });
        for (size_t n = 0; n < jpegs.size(); n++) {
            const double d0 = Ms();
            if (!Decode(data.data() + jpegs[n].first, jpegs[n].second, &fr)) continue;
            decode.Add(Ms() - d0);
            const tundu::Rgba img{fr.pixels.data(), fr.width, fr.height, fr.stride};
            const int slot = npu.Prepare(img, ns(n));
            std::unique_lock<std::mutex> lock(m);
            cv.wait(lock, [&] { return !busy && jobs.empty(); });
            if (slot >= 0 && npu.Tracking()) {
                jobs.push_back({slot, n});
                cv.notify_all();
                continue;
            }
            lock.unlock();   // the worker is idle: no job queued or running until the next push
            npu.Process(img, ns(n), &pose);   // nobody followed: the detector, here
            detections += pose.detected;
            found += pose.found;
            WriteLine(out, label, (int)n, seconds(n), W, H, pose);
        }
        {
            std::unique_lock<std::mutex> lock(m);
            cv.wait(lock, [&] { return !busy && jobs.empty(); });
            done = true;
            cv.notify_all();
        }
        worker.join();
    } else {
        int pending = -1;
        size_t pendingFrame = 0;
        for (size_t n = 0; n < jpegs.size(); n++) {
            const double d0 = Ms();
            if (!Decode(data.data() + jpegs[n].first, jpegs[n].second, &fr)) continue;
            decode.Add(Ms() - d0);
            const tundu::Rgba img{fr.pixels.data(), fr.width, fr.height, fr.stride};
            const double f0 = Ms();
            if (order == "pipelined") {
                // the crop before the frame before has run: its crop box is one frame older
                const int slot = npu.Prepare(img, ns(n));
                if (pending >= 0) {
                    npu.RunJob(pending, &pose);
                    crop.Add(pose.cropMs);
                    run.Add(pose.totalMs);
                    found += pose.found;
                    WriteLine(out, label, (int)pendingFrame, seconds(pendingFrame), W, H, pose);
                    pending = -1;
                }
                if (slot >= 0 && npu.Tracking()) {
                    pending = slot;
                    pendingFrame = n;
                    frame.Add(Ms() - f0);
                    continue;
                }
            }
            npu.Process(img, ns(n), &pose);
            detections += pose.detected;
            if (!pose.detected) {
                crop.Add(pose.cropMs);
                run.Add(pose.totalMs - pose.cropMs);
            }
            found += pose.found;
            WriteLine(out, label, (int)n, seconds(n), W, H, pose);
            frame.Add(Ms() - f0);
        }
        if (pending >= 0) {
            npu.RunJob(pending, &pose);
            found += pose.found;
            WriteLine(out, label, (int)pendingFrame, seconds(pendingFrame), W, H, pose);
        }
    }
    std::fclose(out);
    std::printf("%s %s: %zu frames in %.1f s, a person in %d, %d detector runs -> %s\n", label.c_str(), order.c_str(),
                jpegs.size(), (Ms() - started) / 1000, found, detections, outPath.c_str());
    decode.Print("decode (JPEG)");
    crop.Print("landmark crop");
    run.Print("landmark run (NPU stage)");
    frame.Print("frame (no decode)");
    return 0;
}

// --people 2 [--rate R] [--twin]: the two-person engine (pose_npu.h) on the first picture: who it follows after the
// given frames, then the app's two threads (crops here, NPU runs on a worker, like PoseEngine.submit) as fast as they
// go for N frames: the frame period with two people followed is what a 30 fps camera has to fit into.
static int RunPeople(PoseNpu& npu, const Picture& p, int frames, int loops) {
    tundu::Rgba img{p.pixels.data(), p.width, p.height, p.width * 4};
    tundu::Frame fr;
    npu.Reset();
    int64_t t = 1;
    for (int f = 0; f < std::max(1, frames); f++)
        if (!npu.Process(img, t += 33333333, &fr)) {
            std::fprintf(stderr, "%s: %s\n", p.name.c_str(), npu.Error().c_str());
            return 1;
        }
    std::printf("%s: %d followed after %d frames (max %d), detector %s\n", p.name.c_str(), fr.followed, frames,
                npu.MaxPeople(), fr.detected ? "ran" : "not run");
    for (int k = 0; k < fr.count; k++) {
        const tundu::Pose& q = fr.people[k];
        std::printf("  track %d: found %d, score %.3f, hips (%.0f, %.0f) px, crop %.0f px, nose (%.0f, %.0f)\n", q.track,
                    q.found, q.score, q.roi[0], q.roi[1], q.roi[2], q.lm[0][0] * p.width, q.lm[0][1] * p.height);
    }
    // the app's pipeline: Prepare here, RunJob on the worker, as fast as possible
    Stat period, npuPart, runs;
    std::thread worker;
    tundu::Frame done;
    double last = Ms();
    int jobs = 0, people = 0;
    for (int k = 0; k < loops; k++) {
        const int slot = npu.Prepare(img, t += 33333333);
        if (worker.joinable()) {
            worker.join();
            npuPart.Add(done.totalMs);
            runs.Add(done.count);
            people += done.Found();
            jobs++;
        }
        const double now = Ms();
        if (k > 0) period.Add(now - last);
        last = now;
        if (slot < 0) {
            npu.Process(img, t, &fr);
            continue;
        }
        worker = std::thread([&npu, slot, &done] { npu.RunJob(slot, &done); });
    }
    if (worker.joinable()) worker.join();
    std::printf("  pipelined, %d frames: %d jobs, %.2f people per job\n", loops, jobs, jobs ? double(people) / jobs : 0.0);
    period.Print("frame period");
    npuPart.Print("NPU thread per frame");
    runs.Print("landmark runs per frame");
    return 0;
}

int main(int argc, char** argv) {
    if (argc < 6) {
        std::fprintf(stderr, "usage: %s <detector.rknn> <landmarks.rknn> <label> <out_dir> <name.WxH.rgba>... [--loops N]\n", argv[0]);
        return 2;
    }
    int loops = 50, frames = 1, people = 1;
    float rate = 0;
    bool perf = false, crop = false, twin = false;
    std::string clip, order = "serial";
    std::vector<Picture> pictures;
    for (int i = 5; i < argc; i++) {
        if (!std::strcmp(argv[i], "--people") && i + 1 < argc) {
            people = std::atoi(argv[++i]);
            continue;
        }
        if (!std::strcmp(argv[i], "--rate") && i + 1 < argc) {
            rate = (float)std::atof(argv[++i]);
            continue;
        }
        if (!std::strcmp(argv[i], "--twin")) {
            twin = true;
            continue;
        }
        if (!std::strcmp(argv[i], "--clip") && i + 1 < argc) {
            clip = argv[++i];
            continue;
        }
        if (!std::strcmp(argv[i], "--order") && i + 1 < argc) {
            order = argv[++i];
            continue;
        }
        if (!std::strcmp(argv[i], "--loops") && i + 1 < argc) {
            loops = std::atoi(argv[++i]);
            continue;
        }
        if (!std::strcmp(argv[i], "--crop")) {
            crop = true;
            continue;
        }
        if (!std::strcmp(argv[i], "--perf")) {  // RKNN's per-layer report into <out_dir>/<label>.perf.txt (slower runs)
            perf = true;
            continue;
        }
        if (!std::strcmp(argv[i], "--frames") && i + 1 < argc) {
            frames = std::atoi(argv[++i]);
            continue;
        }
        Picture p;
        if (!LoadPicture(argv[i], &p)) {
            std::fprintf(stderr, "can't read %s\n", argv[i]);
            return 2;
        }
        pictures.push_back(std::move(p));
    }
    std::vector<uint8_t> det = ReadFile(argv[1]), lm = ReadFile(argv[2]);
    PoseNpu npu;
    if (det.empty() || lm.empty() ||
        !npu.Load(det.data(), det.size(), lm.data(), lm.size(), perf ? RKNN_FLAG_COLLECT_PERF_MASK : 0)) {
        std::fprintf(stderr, "load failed: %s\n", npu.Error().c_str());
        return 1;
    }
    std::printf("%s\n", npu.Versions().c_str());
    if (!clip.empty()) return RunClip(npu, argv[3], argv[4], clip, order);
    if (people > 1 || twin) {
        npu.SetPeople(people, rate, twin);
        int status = 0;
        for (const Picture& p : pictures) status |= RunPeople(npu, p, frames, loops);
        return status;
    }
    const std::string label = argv[3], outDir = argv[4];
    Pose pose;
    for (const Picture& p : pictures) {
        npu.Reset();
        tundu::Rgba img{p.pixels.data(), p.width, p.height, p.width * 4};
        if (crop) {
            const float whole[4] = {p.width * 0.5f, p.height * 0.5f, (float)p.width, 0.f};
            if (!npu.LandmarksAt(img, whole, &pose)) {
                std::fprintf(stderr, "%s: %s\n", p.name.c_str(), npu.Error().c_str());
                return 1;
            }
            WriteJson(outDir + "/" + p.name + "." + label + ".json", pose, p);
            continue;
        }
        for (int f = 0; f < frames; f++)
            if (!npu.Process(img, 1 + f * 33333333LL, &pose)) {
                std::fprintf(stderr, "%s: %s\n", p.name.c_str(), npu.Error().c_str());
                return 1;
            }
        std::printf("%s: found %d, score %.3f, detector %.1f ms, landmarks %.1f ms\n", p.name.c_str(), pose.found,
                    pose.score, pose.detectMs, pose.landmarkMs);
        WriteJson(outDir + "/" + p.name + "." + label + ".json", pose, p);
    }
    if (pictures.empty() || loops <= 0 || crop) return 0;
    const Picture& p = pictures[0];
    tundu::Rgba img{p.pixels.data(), p.width, p.height, p.width * 4};
    Stat det1, lm1, total1, lm2, total2;
    int64_t t = 1;
    for (int k = 0; k < loops; k++) {  // nobody tracked: the detector, then the landmarks, every frame
        npu.Reset();
        npu.Process(img, t += 33333333, &pose);
        det1.Add(pose.detectMs);
        lm1.Add(pose.landmarkMs);
        total1.Add(pose.totalMs);
    }
    npu.Reset();
    npu.Process(img, t += 33333333, &pose);
    for (int k = 0; k < loops; k++) {  // tracking: the landmarks only
        npu.Process(img, t += 33333333, &pose);
        lm2.Add(pose.landmarkMs);
        total2.Add(pose.totalMs);
    }
    std::printf("%s on %s (%dx%d):\n", label.c_str(), p.name.c_str(), p.width, p.height);
    det1.Print("detector");
    lm1.Print("landmarks");
    total1.Print("detector + landmarks");
    lm2.Print("tracking: landmarks");
    total2.Print("tracking: frame total");
    npu.Reset();
    npu.Process(img, t += 33333333, &pose);  // a frame with both models, for the breakdown
    std::printf("  last frame: %s\n", npu.Profile().c_str());
    if (perf) {
        FILE* f = std::fopen((outDir + "/" + label + ".perf.txt").c_str(), "w");
        if (f) {
            std::string detail = npu.PerfDetail();
            std::fwrite(detail.data(), 1, detail.size(), f);
            std::fclose(f);
        }
    }
    return 0;
}
