// The NPU pose engine from the box's shell, without the app (built by tools/npu/build_native.py):
//   pose_npu_test <detector.rknn> <landmarks.rknn> <label> <out_dir> <name.WxH.rgba>... [--loops N] [--frames F]
//                 [--crop]
// Each picture (raw RGBA, its size in the name) as F frames in a row (1: the detector, 2: then the crop from the
// landmarks, smoothed): the last one into <out_dir>/<name>.<label>.json for tools/npu/compare.py --box. Then timings
// on the first picture: the detector + landmarks every frame, and tracking (landmarks only), N frames each.
// --crop: each picture is a landmark model crop (compare.py --write-crops): the landmark model alone on it as it is,
// landmarks in the crop's pixels, for compare.py --crops (no timings).
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
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

int main(int argc, char** argv) {
    if (argc < 6) {
        std::fprintf(stderr, "usage: %s <detector.rknn> <landmarks.rknn> <label> <out_dir> <name.WxH.rgba>... [--loops N]\n", argv[0]);
        return 2;
    }
    int loops = 50, frames = 1;
    bool perf = false, crop = false;
    std::vector<Picture> pictures;
    for (int i = 5; i < argc; i++) {
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
