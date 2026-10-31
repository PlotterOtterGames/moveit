#include "pose_npu.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <thread>

#include "rknn_api.h"

namespace tundu {
namespace {

constexpr int kDetSize = 224;
constexpr int kLmSize = 256;
constexpr int kAnchors = 2254;
constexpr int kHeat = 64;
constexpr int kHeatKernel = 7;
constexpr float kDetMinScore = 0.5f;
constexpr float kNmsIou = 0.3f;
constexpr float kRoiScale = 1.25f;
constexpr float kMinPresence = 0.5f;
constexpr float kPi = 3.14159265358979f;

double NowMs() {
    return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now().time_since_epoch()).count();
}

float Sigmoid(float x) { return 1.f / (1.f + std::exp(-x)); }

// SsdAnchorsCalculator with the pose detector's options: 5 layers, strides 8 16 32 32 32, 2 anchors per layer and
// cell (aspect ratio 1 + the interpolated scale), fixed size; layers with the same stride share a grid. 2254 anchors.
std::vector<float> MakeAnchors() {
    static const int strides[] = {8, 16, 32, 32, 32};
    std::vector<float> a;
    a.reserve(kAnchors * 2);
    int layer = 0;
    while (layer < 5) {
        int perCell = 0, last = layer;
        while (last < 5 && strides[last] == strides[layer]) {
            perCell += 2;
            last++;
        }
        const int cells = (kDetSize + strides[layer] - 1) / strides[layer];
        for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
                for (int k = 0; k < perCell; k++) {
                    a.push_back((x + 0.5f) / cells);
                    a.push_back((y + 0.5f) / cells);
                }
        layer = last;
    }
    return a;
}

// Bilinear warp of an RGBA picture into an RGB tensor. m takes a tensor pixel (u, v) to a picture position. Like
// MediaPipe's ImageToTensor (the ROI's corners mapped onto the tensor's corners with OpenCV), coordinates are used as
// they are, with no half-pixel shifts: the models learned on crops made that way (blazepose.py matches MediaPipe to
// 0.03 px this way, ~2 px with shifts). Outside the picture: black (the detector's letterbox) or the nearest edge pixel
// (the landmark crop), as MediaPipe does.
// Where the warp writes: RGB bytes (NHWC), or what the NPU reads, the channels normalized and laid out NC1HWC2 (each
// pixel c2 slots, the 3 colours in the first): float16 for a float16 model, the 8-bit codes for an INT8 one. Then the
// runtime has nothing left to convert (it did that on the CPU: ~7 ms for the 256 x 256 crop in float16).
struct Target {
    enum Kind { kRgb, kFloat16, kQuantized };
    Kind kind = kRgb;
    uint8_t* bytes = nullptr;
    int rowPixels = 0;      // pixels per row, padding included
    int c2 = 0;
    float mean = 0, scale = 1;
    uint8_t code[256];      // kQuantized: each colour value's code ((value - mean) * scale quantized; int8 as its byte)

    inline void Store(int u, int v, const uint8_t rgb[3]) const {
        if (kind == kRgb) {
            uint8_t* p = bytes + ((size_t)v * rowPixels + u) * 3;
            p[0] = rgb[0];
            p[1] = rgb[1];
            p[2] = rgb[2];
        } else if (kind == kQuantized) {
            uint8_t* p = bytes + ((size_t)v * rowPixels + u) * c2;
            p[0] = code[rgb[0]];
            p[1] = code[rgb[1]];
            p[2] = code[rgb[2]];
        } else {
            __fp16* p = reinterpret_cast<__fp16*>(bytes) + ((size_t)v * rowPixels + u) * c2;
            p[0] = (__fp16)((rgb[0] - mean) * scale);
            p[1] = (__fp16)((rgb[1] - mean) * scale);
            p[2] = (__fp16)((rgb[2] - mean) * scale);
        }
    }
};

// Rows v0..v1 of the warp. Fixed point (16.16 positions, 8-bit weights): the float version took ~11 ms for a
// 256 x 256 crop on the box's Cortex-A55.
void WarpRows(const Rgba& src, const float m[6], const Target& dst, int dw, int v0, int v1, bool replicate) {
    const int w = src.width, h = src.height;
    const int32_t dxdu = (int32_t)std::lround(m[0] * 65536.0), dydu = (int32_t)std::lround(m[3] * 65536.0);
    uint8_t out[3];
    for (int v = v0; v < v1; v++) {
        int32_t fx = (int32_t)std::lround((m[1] * v + m[2]) * 65536.0);
        int32_t fy = (int32_t)std::lround((m[4] * v + m[5]) * 65536.0);
        for (int u = 0; u < dw; u++, fx += dxdu, fy += dydu) {
            const int x0 = fx >> 16, y0 = fy >> 16;   // floor, also below zero
            const int ax = (fx >> 8) & 0xFF, ay = (fy >> 8) & 0xFF;
            if (x0 >= 0 && y0 >= 0 && x0 + 1 < w && y0 + 1 < h) {
                const uint8_t* p0 = src.pixels + (size_t)y0 * src.stride + x0 * 4;
                const uint8_t* p1 = p0 + src.stride;
                for (int c = 0; c < 3; c++) {
                    const int top = p0[c] * (256 - ax) + p0[c + 4] * ax;
                    const int bottom = p1[c] * (256 - ax) + p1[c + 4] * ax;
                    out[c] = (uint8_t)((top * (256 - ay) + bottom * ay + 32768) >> 16);
                }
                dst.Store(u, v, out);
                continue;
            }
            // at or beyond the picture's edge: repeat the edge pixels, or count the outside as black
            int xs[2] = {x0, x0 + 1}, ys[2] = {y0, y0 + 1};
            int wx[2] = {256 - ax, ax}, wy[2] = {256 - ay, ay};
            for (int i = 0; i < 2; i++) {
                if (xs[i] < 0 || xs[i] >= w) {
                    if (replicate) xs[i] = xs[i] < 0 ? 0 : w - 1;
                    else { wx[i] = 0; xs[i] = 0; }
                }
                if (ys[i] < 0 || ys[i] >= h) {
                    if (replicate) ys[i] = ys[i] < 0 ? 0 : h - 1;
                    else { wy[i] = 0; ys[i] = 0; }
                }
            }
            const uint8_t* r0 = src.pixels + (size_t)ys[0] * src.stride;
            const uint8_t* r1 = src.pixels + (size_t)ys[1] * src.stride;
            const int a = xs[0] * 4, b = xs[1] * 4;
            for (int c = 0; c < 3; c++) {
                const int value = wy[0] * (wx[0] * r0[a + c] + wx[1] * r0[b + c]) +
                                  wy[1] * (wx[0] * r1[a + c] + wx[1] * r1[b + c]);
                out[c] = (uint8_t)((value + 32768) >> 16);
            }
            dst.Store(u, v, out);
        }
    }
}

// The warp on two cores (the second half of the rows on a helper thread): ~6-8 ms on one A55 core for 256 x 256.
void Warp(const Rgba& src, const float m[6], const Target& dst, int dw, int dh, bool replicate) {
    const int half = dh / 2;
    std::thread helper([&] { WarpRows(src, m, dst, dw, half, dh, replicate); });
    WarpRows(src, m, dst, dw, 0, half, replicate);
    helper.join();
}

// AlignmentPointsRects (the body upright, the box twice the distance between the points) + RectTransformation
// (scaled, square): centre x, y and size in pixels, rotation in radians within [-pi, pi).
void RoiFromPoints(float cx, float cy, float px, float py, float scale, float roi[4]) {
    const float dx = px - cx, dy = py - cy;
    roi[0] = cx;
    roi[1] = cy;
    roi[2] = 2.f * std::hypot(dx, dy) * scale;
    const float r = kPi / 2 - std::atan2(-dy, dx);
    roi[3] = r - 2 * kPi * std::floor((r + kPi) / (2 * kPi));
}

// Takes a point of the 256 x 256 crop to the picture: the rotated square ROI (also projects the landmarks back).
void CropMatrix(const float roi[4], float m[6]) {
    const float k = roi[2] / kLmSize, c = std::cos(roi[3]) * k, s = std::sin(roi[3]) * k, h = kLmSize * 0.5f;
    m[0] = c;
    m[1] = -s;
    m[2] = roi[0] - c * h + s * h;
    m[3] = s;
    m[4] = c;
    m[5] = roi[1] - s * h - c * h;
}

// RefineLandmarksFromHeatmap: the score-weighted centre of a 7 x 7 window of the landmark's heatmap (64 x 64).
void Refine(const float* heat, bool channelsFirst, int i, float* x, float* y) {
    const int col = int(*x * kHeat), row = int(*y * kHeat);
    if (col < 0 || col >= kHeat || row < 0 || row >= kHeat) return;
    const int off = (kHeatKernel - 1) / 2;
    const int r0 = std::max(0, row - off), r1 = std::min(kHeat, row + off + 1);
    const int c0 = std::max(0, col - off), c1 = std::min(kHeat, col + off + 1);
    float sum = 0, maxConf = 0, wc = 0, wr = 0;
    for (int r = r0; r < r1; r++)
        for (int c = c0; c < c1; c++) {
            const float v = channelsFirst ? heat[(i * kHeat + r) * kHeat + c] : heat[(r * kHeat + c) * kLandmarks + i];
            const float conf = Sigmoid(v);
            sum += conf;
            maxConf = std::max(maxConf, conf);
            wc += c * conf;
            wr += r * conf;
        }
    if (maxConf >= 0.5f && sum > 0) {
        *x = wc / kHeat / sum;
        *y = wr / kHeat / sum;
    }
}

}  // namespace

struct PoseNpu::Net {
    rknn_context ctx = 0;
    std::vector<uint8_t> model;
    uint32_t outputs = 0;
    std::vector<rknn_tensor_attr> attrs;
    std::vector<std::vector<float>> out;
    std::vector<std::vector<int8_t>> codes;   // an INT8 output as the NPU gave it (dequantized into out by us)
    std::vector<rknn_output> request;
    std::string version;
    double setMs = 0, runMs = 0, getMs = 0, npuMs = 0;   // the last run
    // The input, best first: RKNN's own memory in the NPU's native layout (float16, or the 8-bit codes of an INT8
    // model), written by our warp (nothing left to convert); RKNN's memory as uint8 RGB (the runtime converts it on the
    // CPU inside rknn_run); our buffer through rknn_inputs_set. TUNDU_NPU_INPUT=uint8 / buffer forces the others
    // (pose_npu_test, comparing them).
    rknn_tensor_mem* inMem = nullptr;
    std::vector<uint8_t> inBuf;
    Target target;
    std::string inputInfo, nativeInfo, outputInfo;

    ~Net() {
        if (inMem) rknn_destroy_mem(ctx, inMem);
        if (ctx) rknn_destroy(ctx);
    }

    bool UseMem(rknn_tensor_attr* attr) {
        inMem = rknn_create_mem(ctx, attr->size_with_stride);
        if (inMem && rknn_set_io_mem(ctx, inMem, attr) == 0) {
            std::memset(inMem->virt_addr, 0, attr->size_with_stride);   // the unused channel slots stay zero
            target.bytes = (uint8_t*)inMem->virt_addr;
            return true;
        }
        if (inMem) rknn_destroy_mem(ctx, inMem);
        inMem = nullptr;
        return false;
    }

    // mean / scale: the model's normalization ((x - mean) * scale), applied by us when we write float16.
    void InitInput(int size, float mean, float scale) {
        const char* force = std::getenv("TUNDU_NPU_INPUT");
        const std::string mode = force ? force : "";
        rknn_tensor_attr attr{};
        attr.index = 0;
        const bool queried = rknn_query(ctx, RKNN_QUERY_NATIVE_INPUT_ATTR, &attr, sizeof(attr)) == 0;
        char native[200];
        std::snprintf(native, sizeof(native), "native %s %s dims %u:%u,%u,%u,%u,%u w_stride %u size %u zp %d scale %g",
                      get_format_string(attr.fmt), get_type_string(attr.type), attr.n_dims, attr.dims[0], attr.dims[1],
                      attr.dims[2], attr.dims[3], attr.dims[4], attr.w_stride, attr.size_with_stride, attr.zp,
                      attr.scale);
        nativeInfo = queried ? native : "native: query failed";
        // the RK3566 takes a 3-channel float16 input as NHWC [1, h, w, 3]; other chips NC1HWC2 [1, 1, h, w, c2]
        const bool nhwc = attr.fmt == RKNN_TENSOR_NHWC && attr.n_dims == 4 && attr.dims[3] == 3;
        const bool nc1hwc2 = attr.fmt == RKNN_TENSOR_NC1HWC2 && attr.n_dims == 5 && attr.dims[1] == 1 && attr.dims[4] >= 3;
        const bool int8 = attr.type == RKNN_TENSOR_INT8 && attr.qnt_type == RKNN_TENSOR_QNT_AFFINE_ASYMMETRIC &&
                          attr.scale > 0;
        if (mode.empty() && queried && (attr.type == RKNN_TENSOR_FLOAT16 || int8) && (nhwc || nc1hwc2)) {
            attr.pass_through = 1;
            if (UseMem(&attr)) {
                target.kind = int8 ? Target::kQuantized : Target::kFloat16;
                target.c2 = nhwc ? 3 : (int)attr.dims[4];
                target.rowPixels = int(attr.w_stride > 0 ? attr.w_stride : (uint32_t)size);
                target.mean = mean;
                target.scale = scale;
                // the nearest code; halfway between two (the detector: (x - 127.5) / 127.5 in steps of 1 / 127.5) the
                // lower one, so the 256 colour values keep 256 different codes
                for (int value = 0; int8 && value < 256; value++) {
                    const long q = (long)std::floor((value - mean) * scale / attr.scale + 0.499f) + attr.zp;
                    target.code[value] = (uint8_t)(int8_t)std::min(127L, std::max(-128L, q));
                }
                inputInfo = std::string(int8 ? "int8 " : "float16 ") + (nhwc ? "NHWC" : "NC1HWC2");
                return;
            }
        }
        std::memset(&attr, 0, sizeof(attr));
        attr.index = 0;
        if (mode != "buffer" && rknn_query(ctx, RKNN_QUERY_INPUT_ATTR, &attr, sizeof(attr)) == 0) {
            attr.type = RKNN_TENSOR_UINT8;
            attr.fmt = RKNN_TENSOR_NHWC;
            if (UseMem(&attr)) {
                target.rowPixels = int(attr.w_stride > 0 ? attr.w_stride : (uint32_t)size);
                inputInfo = "uint8 NHWC";
                return;
            }
        }
        inBuf.resize((size_t)size * size * 3);
        target.bytes = inBuf.data();
        target.rowPixels = size;
        inputInfo = "uint8 through rknn_inputs_set";
    }

    bool Init(const void* data, size_t size, uint32_t flags, std::string* error, const char* what) {
        model.assign((const uint8_t*)data, (const uint8_t*)data + size);
        int ret = rknn_init(&ctx, model.data(), (uint32_t)model.size(), flags, nullptr);
        if (ret < 0) {
            ctx = 0;
            *error = std::string(what) + ": rknn_init failed (" + std::to_string(ret) + ")";
            return false;
        }
        rknn_input_output_num io{};
        if (rknn_query(ctx, RKNN_QUERY_IN_OUT_NUM, &io, sizeof(io)) < 0) {
            *error = std::string(what) + ": rknn_query failed";
            return false;
        }
        outputs = io.n_output;
        attrs.resize(outputs);
        out.resize(outputs);
        codes.resize(outputs);
        request.resize(outputs);
        for (uint32_t i = 0; i < outputs; i++) {
            std::memset(&attrs[i], 0, sizeof(rknn_tensor_attr));
            attrs[i].index = i;
            rknn_query(ctx, RKNN_QUERY_OUTPUT_ATTR, &attrs[i], sizeof(rknn_tensor_attr));
            out[i].resize(attrs[i].n_elems);
            // TUNDU_NPU_OUTPUT=float: the runtime dequantizes instead (pose_npu_test, comparing them)
            const char* force = std::getenv("TUNDU_NPU_OUTPUT");
            if (attrs[i].type == RKNN_TENSOR_INT8 && attrs[i].qnt_type == RKNN_TENSOR_QNT_AFFINE_ASYMMETRIC &&
                !(force && std::string(force) == "float"))
                codes[i].resize(attrs[i].n_elems);
            outputInfo += std::string(i ? ", " : "outputs ") + get_type_string(attrs[i].type);
        }
        rknn_sdk_version v{};
        if (rknn_query(ctx, RKNN_QUERY_SDK_VERSION, &v, sizeof(v)) == 0)
            version = std::string("runtime ") + v.api_version + ", driver " + v.drv_version;
        return true;
    }

    // The models' outputs told apart by their sizes.
    int Find(uint32_t elems) const {
        for (uint32_t i = 0; i < outputs; i++)
            if (attrs[i].n_elems == elems) return (int)i;
        return -1;
    }

    // Runs the model on what Input() holds.
    bool Run(int size, std::string* error, const char* what) {
        double t0 = NowMs(), t1 = t0, t2 = t0;
        int ret = 0;
        if (!inMem) {
            rknn_input in{};
            in.index = 0;
            in.buf = inBuf.data();
            in.size = (uint32_t)(size * size * 3);
            in.pass_through = 0;
            in.type = RKNN_TENSOR_UINT8;
            in.fmt = RKNN_TENSOR_NHWC;
            ret = rknn_inputs_set(ctx, 1, &in);
        }
        if (ret >= 0) {
            t1 = NowMs();
            ret = rknn_run(ctx, nullptr);
            t2 = NowMs();
        }
        if (ret >= 0) {
            for (uint32_t i = 0; i < outputs; i++) {
                const bool int8 = !codes[i].empty();
                request[i] = {};
                request[i].want_float = int8 ? 0 : 1;
                request[i].is_prealloc = 1;
                request[i].index = i;
                request[i].buf = int8 ? (void*)codes[i].data() : (void*)out[i].data();
                request[i].size = (uint32_t)(int8 ? codes[i].size() : out[i].size() * sizeof(float));
            }
            ret = rknn_outputs_get(ctx, outputs, request.data(), nullptr);
            if (ret >= 0) rknn_outputs_release(ctx, outputs, request.data());
            // INT8 outputs: value = (code - zero point) * scale
            for (uint32_t i = 0; ret >= 0 && i < outputs; i++) {
                if (codes[i].empty()) continue;
                const int8_t* q = codes[i].data();
                float* f = out[i].data();
                const float scale = attrs[i].scale;
                const int32_t zp = attrs[i].zp;
                for (size_t k = 0, n = codes[i].size(); k < n; k++) f[k] = float(q[k] - zp) * scale;
            }
        }
        setMs = t1 - t0;
        runMs = t2 - t1;
        getMs = NowMs() - t2;
        rknn_perf_run perf{};
        npuMs = rknn_query(ctx, RKNN_QUERY_PERF_RUN, &perf, sizeof(perf)) == 0 ? perf.run_duration / 1000.0 : -1;
        if (ret < 0) {
            *error = std::string(what) + ": NPU run failed (" + std::to_string(ret) + ")";
            return false;
        }
        return true;
    }
};

PoseNpu::PoseNpu() : anchors_(MakeAnchors()) {
    for (int i = 0; i < kPoseLandmarks * 3; i++) {
        lmFilters_.emplace_back(0.05, 80.0, 1.0);     // MediaPipe's pose landmarks (scaled by the body's size)
        worldFilters_.emplace_back(0.1, 40.0, 1.0);   // and world landmarks (no scaling)
    }
    visFilters_.assign(kPoseLandmarks, LowPassFilter(0.1));
    for (int i = 0; i < 4; i++) auxFilters_.emplace_back(5, 10.f);
}

PoseNpu::~PoseNpu() = default;

bool PoseNpu::Load(const void* detector, size_t detectorSize, const void* landmarks, size_t landmarksSize,
                   uint32_t initFlags) {
    error_.clear();
    det_.reset(new Net());
    lm_.reset(new Net());
    if (!det_->Init(detector, detectorSize, initFlags, &error_, "detector")) return false;
    if (!lm_->Init(landmarks, landmarksSize, initFlags, &error_, "landmarks")) return false;
    if (det_->Find(kAnchors * 12) < 0 || det_->Find(kAnchors) < 0) {
        error_ = "detector: unexpected outputs";
        return false;
    }
    if (lm_->Find(kLandmarks * 5) < 0 || lm_->Find(1) < 0 || lm_->Find(kLandmarks * 3) < 0) {
        error_ = "landmarks: unexpected outputs";
        return false;
    }
    det_->InitInput(kDetSize, 127.5f, 1.f / 127.5f);   // tools/npu/convert.py: (x - 127.5) / 127.5
    lm_->InitInput(kLmSize, 0.f, 1.f / 255.f);         // x / 255
    Reset();
    return true;
}

std::string PoseNpu::Versions() const {
    if (!det_ || !lm_) return std::string();
    return det_->version + "; detector: input " + det_->inputInfo + " (" + det_->nativeInfo + "), " + det_->outputInfo +
           "; landmarks: input " + lm_->inputInfo + " (" + lm_->nativeInfo + "), " + lm_->outputInfo;
}

std::string PoseNpu::Profile() const {
    if (!det_ || !lm_) return std::string();
    char s[400];
    std::snprintf(s, sizeof(s),
                  "detector: warp %.1f, input %.1f, run %.1f (NPU %.1f), outputs %.1f ms | "
                  "landmarks: warp %.1f, input %.1f, run %.1f (NPU %.1f), outputs %.1f ms",
                  warpMs_[0], det_->setMs, det_->runMs, det_->npuMs, det_->getMs, warpMs_[1], lm_->setMs, lm_->runMs,
                  lm_->npuMs, lm_->getMs);
    return s;
}

std::string PoseNpu::PerfDetail() const {
    std::string s;
    for (const Net* net : {det_.get(), lm_.get()}) {
        if (!net) continue;
        rknn_perf_detail detail{};
        if (rknn_query(net->ctx, RKNN_QUERY_PERF_DETAIL, &detail, sizeof(detail)) == 0 && detail.perf_data)
            s.append(detail.perf_data, detail.data_len).append("\n");
    }
    return s;
}

void PoseNpu::Reset() {
    tracking_ = false;
    ResetFilters();
}

void PoseNpu::ResetFilters() {
    for (auto& f : lmFilters_) f.Reset();
    for (auto& f : worldFilters_) f.Reset();
    for (auto& f : visFilters_) f.Reset();
    for (auto& f : auxFilters_) f.Reset();
}

bool PoseNpu::Detect(const Rgba& image, float roi[4], bool* found) {
    *found = false;
    // the whole picture fitted into 224 x 224, black bars
    const float s = float(kDetSize) / float(std::max(image.width, image.height));
    const float padX = (kDetSize - image.width * s) * 0.5f, padY = (kDetSize - image.height * s) * 0.5f;
    const float m[6] = {1.f / s, 0.f, -padX / s, 0.f, 1.f / s, -padY / s};
    const double w0 = NowMs();
    Warp(image, m, det_->target, kDetSize, kDetSize, false);
    warpMs_[0] = float(NowMs() - w0);
    if (!det_->Run(kDetSize, &error_, "detector")) return false;
    const float* boxes = det_->out[det_->Find(kAnchors * 12)].data();
    const float* scores = det_->out[det_->Find(kAnchors)].data();
    auto prob = [&](int i) { return Sigmoid(std::min(std::max(scores[i], -100.f), 100.f)); };
    int best = 0;
    for (int i = 1; i < kAnchors; i++)
        if (scores[i] > scores[best]) best = i;
    if (prob(best) < kDetMinScore) return true;  // nobody
    auto rect = [&](int i, float r[4]) {
        const float* b = boxes + i * 12;
        const float cx = b[0] / kDetSize + anchors_[2 * i], cy = b[1] / kDetSize + anchors_[2 * i + 1];
        const float hw = b[2] / kDetSize * 0.5f, hh = b[3] / kDetSize * 0.5f;
        r[0] = cx - hw;
        r[1] = cy - hh;
        r[2] = cx + hw;
        r[3] = cy + hh;
    };
    // weighted non-maximum suppression: the best detection's cluster, keypoints averaged by score
    float r0[4];
    rect(best, r0);
    const float area0 = (r0[2] - r0[0]) * (r0[3] - r0[1]);
    double total = 0, kp[4] = {0, 0, 0, 0};
    for (int i = 0; i < kAnchors; i++) {
        const float p = prob(i);
        if (p < kDetMinScore) continue;
        float r[4];
        rect(i, r);
        const float ix = std::max(0.f, std::min(r[2], r0[2]) - std::max(r[0], r0[0]));
        const float iy = std::max(0.f, std::min(r[3], r0[3]) - std::max(r[1], r0[1]));
        const float inter = ix * iy, area = (r[2] - r[0]) * (r[3] - r[1]);
        if (inter / std::max(area + area0 - inter, 1e-9f) <= kNmsIou) continue;
        const float* b = boxes + i * 12;
        const float ax = anchors_[2 * i], ay = anchors_[2 * i + 1];
        kp[0] += p * (b[4] / kDetSize + ax);
        kp[1] += p * (b[5] / kDetSize + ay);
        kp[2] += p * (b[6] / kDetSize + ax);
        kp[3] += p * (b[7] / kDetSize + ay);
        total += p;
    }
    for (double& v : kp) v /= total;
    // keypoint 0 = the hips' centre, 1 = the point giving the body's size and turn; back to picture pixels
    RoiFromPoints(float((kp[0] * kDetSize - padX) / s), float((kp[1] * kDetSize - padY) / s),
                  float((kp[2] * kDetSize - padX) / s), float((kp[3] * kDetSize - padY) / s), kRoiScale, roi);
    *found = true;
    return true;
}

bool PoseNpu::Landmarks(const Rgba& image, const float roi[4], Pose* out, float aux[2][2]) {
    float m[6];
    CropMatrix(roi, m);
    const double w0 = NowMs();
    Warp(image, m, lm_->target, kLmSize, kLmSize, true);
    warpMs_[1] = float(NowMs() - w0);
    if (!lm_->Run(kLmSize, &error_, "landmarks")) return false;
    const float* raw = lm_->out[lm_->Find(kLandmarks * 5)].data();
    const float* world = lm_->out[lm_->Find(kLandmarks * 3)].data();
    const int hi = lm_->Find(kHeat * kHeat * kLandmarks);
    const float* heat = hi >= 0 ? lm_->out[hi].data() : nullptr;
    const bool channelsFirst = hi >= 0 && lm_->attrs[hi].n_dims == 4 && lm_->attrs[hi].dims[1] == kLandmarks;
    out->score = lm_->out[lm_->Find(1)][0];
    const float c = std::cos(roi[3]), s = std::sin(roi[3]);
    for (int i = 0; i < kLandmarks; i++) {
        const float* r = raw + i * 5;
        float x = r[0] / kLmSize, y = r[1] / kLmSize;
        if (heat) Refine(heat, channelsFirst, i, &x, &y);
        const float X = m[0] * x * kLmSize + m[1] * y * kLmSize + m[2];
        const float Y = m[3] * x * kLmSize + m[4] * y * kLmSize + m[5];
        if (i == 33 || i == 34) {
            aux[i - 33][0] = X;
            aux[i - 33][1] = Y;
        }
        if (i >= kPoseLandmarks) continue;
        out->lm[i][0] = X / image.width;
        out->lm[i][1] = Y / image.height;
        out->lm[i][2] = r[2] / kLmSize * roi[2] / image.width;
        out->lm[i][3] = Sigmoid(r[3]);
        out->lm[i][4] = Sigmoid(r[4]);
        const float* w = world + i * 3;
        out->world[i][0] = c * w[0] - s * w[1];
        out->world[i][1] = s * w[0] + c * w[1];
        out->world[i][2] = w[2];
    }
    return true;
}

void PoseNpu::Smooth(const Rgba& image, int64_t timestampNs, Pose* pose, float aux[2][2]) {
    // MediaPipe's pose filtering: visibility low-passed, landmarks One Euro in picture pixels with speeds relative to
    // the body's size (the alignment points' box), world landmarks One Euro in metres; the alignment points the next
    // crop comes from go through the relative velocity filter.
    const float box = 2.f * std::hypot(aux[1][0] - aux[0][0], aux[1][1] - aux[0][1]);
    const double scale = box > 1e-6f ? 1.0 / box : 0.0;
    const float size[3] = {(float)image.width, (float)image.height, (float)image.width};
    for (int i = 0; i < kPoseLandmarks; i++) {
        pose->lm[i][3] = (float)visFilters_[i].Apply(pose->lm[i][3]);
        if (scale > 0)
            for (int k = 0; k < 3; k++)
                pose->lm[i][k] = float(lmFilters_[i * 3 + k].Apply(timestampNs, scale, pose->lm[i][k] * size[k]) / size[k]);
        for (int k = 0; k < 3; k++)
            pose->world[i][k] = float(worldFilters_[i * 3 + k].Apply(timestampNs, 1.0, pose->world[i][k]));
    }
    if (scale > 0)
        for (int j = 0; j < 2; j++)
            for (int k = 0; k < 2; k++) aux[j][k] = auxFilters_[j * 2 + k].Apply(timestampNs, (float)scale, aux[j][k]);
    RoiFromPoints(aux[0][0], aux[0][1], aux[1][0], aux[1][1], kRoiScale, roi_);
}

bool PoseNpu::LandmarksAt(const Rgba& image, const float roi[4], Pose* out) {
    error_.clear();
    out->detected = false;
    out->detectMs = 0;
    float aux[2][2];
    const double l0 = NowMs();
    if (!Landmarks(image, roi, out, aux)) return false;
    out->landmarkMs = out->totalMs = float(NowMs() - l0);
    std::copy(roi, roi + 4, out->roi);
    out->found = out->score >= kMinPresence;
    return true;
}

bool PoseNpu::Process(const Rgba& image, int64_t timestampNs, Pose* out) {
    const double t0 = NowMs();
    error_.clear();
    out->found = false;
    out->detected = false;
    out->detectMs = out->landmarkMs = 0;
    if (!tracking_) {
        out->detected = true;
        bool found = false;
        const double d0 = NowMs();
        const bool ok = Detect(image, roi_, &found);
        out->detectMs = float(NowMs() - d0);
        if (!ok || !found) {
            ResetFilters();
            out->totalMs = float(NowMs() - t0);
            return ok;
        }
    }
    float aux[2][2];
    const double l0 = NowMs();
    if (!Landmarks(image, roi_, out, aux)) {
        tracking_ = false;
        return false;
    }
    out->landmarkMs = float(NowMs() - l0);
    std::copy(roi_, roi_ + 4, out->roi);
    if (out->score < kMinPresence) {  // lost: the detector looks again on the next frame
        tracking_ = false;
        ResetFilters();
        out->totalMs = float(NowMs() - t0);
        return true;
    }
    Smooth(image, timestampNs, out, aux);
    tracking_ = true;
    out->found = true;
    out->totalMs = float(NowMs() - t0);
    return true;
}

}  // namespace tundu
