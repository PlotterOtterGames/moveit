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

// The landmark crop of roi from the picture into dst (the NPU's input, or a staging slot). Returns its time in ms.
float CropLandmarks(const Rgba& image, const float roi[4], const Target& dst) {
    float m[6];
    CropMatrix(roi, m);
    const double w0 = NowMs();
    Warp(image, m, dst, kLmSize, kLmSize, true);
    return float(NowMs() - w0);
}

// RefineLandmarksFromHeatmap: the score-weighted centre of a 7 x 7 window of the landmark's heatmap (64 x 64). The
// heatmap as the NPU gave it (float16: only these windows get converted, not its 160 000 values) or as floats.
template <typename T>
void Refine(const T* heat, bool channelsFirst, int i, float* x, float* y) {
    const int col = int(*x * kHeat), row = int(*y * kHeat);
    if (col < 0 || col >= kHeat || row < 0 || row >= kHeat) return;
    const int off = (kHeatKernel - 1) / 2;
    const int r0 = std::max(0, row - off), r1 = std::min(kHeat, row + off + 1);
    const int c0 = std::max(0, col - off), c1 = std::min(kHeat, col + off + 1);
    float sum = 0, maxConf = 0, wc = 0, wr = 0;
    for (int r = r0; r < r1; r++)
        for (int c = c0; c < c1; c++) {
            const float v = float(channelsFirst ? heat[(i * kHeat + r) * kHeat + c] : heat[(r * kHeat + c) * kLandmarks + i]);
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
    // A large float16 output kept as float16 (the landmark heatmap, keepHalf): converting all of it to floats was most
    // of the ~1.7 ms rknn_outputs_get took per frame on the RK3566; Refine converts the few values it reads.
    bool keepHalf = false;
    std::vector<std::vector<__fp16>> half;
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
    size_t inBytes = 0;                      // what target.bytes holds
    // inputs made ahead (Prepare), copied into the input before their run: per job slot, one per person
    std::vector<uint8_t> staging[2][kMaxPeople];
    std::string inputInfo, nativeInfo, outputInfo;

    // Where an input made ahead goes: the input's own layout, in a staging slot.
    Target Staging(int slot, int run) {
        std::vector<uint8_t>& s = staging[slot][run];
        if (s.size() != inBytes) s.assign(inBytes, 0);
        Target t = target;
        t.bytes = s.data();
        return t;
    }

    // A staged input into the NPU's input memory.
    void Load(int slot, int run) { std::memcpy(target.bytes, staging[slot][run].data(), inBytes); }

    ~Net() {
        if (inMem) rknn_destroy_mem(ctx, inMem);
        if (ctx) rknn_destroy(ctx);
    }

    bool UseMem(rknn_tensor_attr* attr) {
        inMem = rknn_create_mem(ctx, attr->size_with_stride);
        if (inMem && rknn_set_io_mem(ctx, inMem, attr) == 0) {
            std::memset(inMem->virt_addr, 0, attr->size_with_stride);   // the unused channel slots stay zero
            target.bytes = (uint8_t*)inMem->virt_addr;
            inBytes = attr->size_with_stride;
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
        inBytes = inBuf.size();
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
        half.resize(outputs);
        request.resize(outputs);
        // TUNDU_NPU_OUTPUT=float: the runtime dequantizes INT8 and converts float16 to floats instead (pose_npu_test,
        // comparing them)
        const char* force = std::getenv("TUNDU_NPU_OUTPUT");
        const bool floats = force && std::string(force) == "float";
        for (uint32_t i = 0; i < outputs; i++) {
            std::memset(&attrs[i], 0, sizeof(rknn_tensor_attr));
            attrs[i].index = i;
            rknn_query(ctx, RKNN_QUERY_OUTPUT_ATTR, &attrs[i], sizeof(rknn_tensor_attr));
            if (attrs[i].type == RKNN_TENSOR_INT8 && attrs[i].qnt_type == RKNN_TENSOR_QNT_AFFINE_ASYMMETRIC && !floats)
                codes[i].resize(attrs[i].n_elems);
            if (keepHalf && attrs[i].type == RKNN_TENSOR_FLOAT16 && attrs[i].n_elems >= 4096 && !floats)
                half[i].resize(attrs[i].n_elems);
            else
                out[i].resize(attrs[i].n_elems);
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
                const bool int8 = !codes[i].empty(), f16 = !half[i].empty();
                request[i] = {};
                request[i].want_float = int8 || f16 ? 0 : 1;
                request[i].is_prealloc = 1;
                request[i].index = i;
                request[i].buf = int8 ? (void*)codes[i].data() : f16 ? (void*)half[i].data() : (void*)out[i].data();
                request[i].size = (uint32_t)(int8 ? codes[i].size()
                                             : f16 ? half[i].size() * sizeof(__fp16) : out[i].size() * sizeof(float));
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

namespace {

// While fewer people are followed than wanted, the detector looks for the others this often (it takes ~33-43 ms on the
// RK3566's NPU, more than a camera frame: every frame would starve the person already followed).
constexpr int64_t kRedetectNs = 500000000;

// Two crop boxes on the same person: hip centres closer than this share of the smaller body's size (the alignment
// distance: hips to the far end of the body, ~0.85 m for an adult). Two people side by side keep their hips ~0.45 m or
// more apart (> 0.5 of it); MediaPipe's own test (IoU of the boxes > 0.5) would merge them.
bool SamePerson(const float a[4], const float b[4]) {
    const float ra = a[2] / (2.f * kRoiScale), rb = b[2] / (2.f * kRoiScale);
    return std::hypot(a[0] - b[0], a[1] - b[1]) < 0.4f * std::min(ra, rb);
}

}  // namespace

PoseNpu::Track::Track() {
    for (int i = 0; i < kPoseLandmarks * 3; i++) {
        lm.emplace_back(0.05, 80.0, 1.0);     // MediaPipe's pose landmarks (scaled by the body's size)
        world.emplace_back(0.1, 40.0, 1.0);   // and world landmarks (no scaling)
    }
    vis.assign(kPoseLandmarks, LowPassFilter(0.1));
    for (int i = 0; i < 4; i++) aux.emplace_back(5, 10.f);
}

void PoseNpu::Track::ResetFilters() {
    for (auto& f : lm) f.Reset();
    for (auto& f : world) f.Reset();
    for (auto& f : vis) f.Reset();
    for (auto& f : aux) f.Reset();
}

PoseNpu::PoseNpu() : anchors_(MakeAnchors()) {}

PoseNpu::~PoseNpu() = default;

bool PoseNpu::Load(const void* detector, size_t detectorSize, const void* landmarks, size_t landmarksSize,
                   uint32_t initFlags) {
    error_.clear();
    det_.reset(new Net());
    lm_.reset(new Net());
    lm_->keepHalf = true;
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

void PoseNpu::SetPeople(int maxPeople, float runsPerSecond, bool twin, int keepTrack, bool bench) {
    std::lock_guard<std::mutex> lock(state_);
    maxPeople_ = std::max(1, std::min(kMaxPeople, maxPeople));
    runsPerSecond_ = std::max(0.f, runsPerSecond);
    twin_ = twin;
    if (bench != bench_)
        for (Track& t : tracks_) {   // real people and bench crops never mix
            t.active = false;
            t.bench = false;
            t.ResetFilters();
        }
    bench_ = bench;
    // fewer wanted now: the person to keep moves to the first places, the others end
    for (int i = 1; keepTrack > 0 && i < kMaxPeople; i++)
        if (tracks_[i].active && tracks_[i].id == keepTrack && !(tracks_[0].active && tracks_[0].id == keepTrack))
            std::swap(tracks_[0], tracks_[i]);
    for (int i = maxPeople_; i < kMaxPeople; i++) {
        tracks_[i].active = false;
        tracks_[i].ResetFilters();
    }
    lastDetectNs_ = 0;   // more wanted: look for them at once
}

void PoseNpu::Reset() {
    std::lock_guard<std::mutex> lock(state_);
    for (Track& t : tracks_) {
        t.active = false;
        t.ResetFilters();
    }
    credit_ = 0;
    creditNs_ = 0;
    lastDetectNs_ = 0;
}

int PoseNpu::FollowedLocked() const {
    int n = 0;
    for (const Track& t : tracks_) n += t.active ? 1 : 0;
    return n;
}

bool PoseNpu::Tracking() const { return Followed() > 0; }

void PoseNpu::FillBench(int width, int height) {
    for (int i = 0; i < maxPeople_; i++) {
        Track& t = tracks_[i];
        if (t.active) continue;
        t.active = true;
        t.bench = true;
        t.id = nextId_++;
        t.roi[0] = width * float(i + 1) / float(maxPeople_ + 1);
        t.roi[1] = height * 0.55f;
        t.roi[2] = height * 0.9f;
        t.roi[3] = 0.f;
    }
}

int PoseNpu::Followed() const {
    std::lock_guard<std::mutex> lock(state_);
    return FollowedLocked();
}

void PoseNpu::DetectorInput(const Rgba& image, uint8_t* dst, float letterbox[3]) {
    // the whole picture fitted into 224 x 224, black bars
    const float s = float(kDetSize) / float(std::max(image.width, image.height));
    const float padX = (kDetSize - image.width * s) * 0.5f, padY = (kDetSize - image.height * s) * 0.5f;
    const float m[6] = {1.f / s, 0.f, -padX / s, 0.f, 1.f / s, -padY / s};
    Target t = det_->target;
    t.bytes = dst;
    const double w0 = NowMs();
    Warp(image, m, t, kDetSize, kDetSize, false);
    warpMs_[0] = float(NowMs() - w0);
    letterbox[0] = s;
    letterbox[1] = padX;
    letterbox[2] = padY;
}

bool PoseNpu::RunDetector(const float letterbox[3], Detection* found, int* count) {
    *count = 0;
    if (!det_->Run(kDetSize, &error_, "detector")) return false;
    const float* boxes = det_->out[det_->Find(kAnchors * 12)].data();
    const float* scores = det_->out[det_->Find(kAnchors)].data();
    auto prob = [&](int i) { return Sigmoid(std::min(std::max(scores[i], -100.f), 100.f)); };
    auto rect = [&](int i, float r[4]) {
        const float* b = boxes + i * 12;
        const float cx = b[0] / kDetSize + anchors_[2 * i], cy = b[1] / kDetSize + anchors_[2 * i + 1];
        const float hw = b[2] / kDetSize * 0.5f, hh = b[3] / kDetSize * 0.5f;
        r[0] = cx - hw;
        r[1] = cy - hh;
        r[2] = cx + hw;
        r[3] = cy + hh;
    };
    // the anchors that see someone, best first (the logit orders them like the probability)
    std::vector<int> candidates;
    for (int i = 0; i < kAnchors; i++)
        if (prob(i) >= kDetMinScore) candidates.push_back(i);
    std::sort(candidates.begin(), candidates.end(), [&](int a, int b) { return scores[a] > scores[b]; });
    // weighted non-maximum suppression, as MediaPipe's: the best anchor left and everything overlapping it is one
    // person, its keypoints averaged by score; then the next best anchor left
    const float s = letterbox[0], padX = letterbox[1], padY = letterbox[2];
    std::vector<bool> used(candidates.size(), false);
    for (size_t c = 0; c < candidates.size() && *count < kMaxDetections; c++) {
        if (used[c]) continue;
        const int best = candidates[c];
        float r0[4];
        rect(best, r0);
        const float area0 = (r0[2] - r0[0]) * (r0[3] - r0[1]);
        double total = 0, kp[4] = {0, 0, 0, 0};
        for (size_t k = c; k < candidates.size(); k++) {
            if (used[k]) continue;
            const int i = candidates[k];
            float r[4];
            rect(i, r);
            const float ix = std::max(0.f, std::min(r[2], r0[2]) - std::max(r[0], r0[0]));
            const float iy = std::max(0.f, std::min(r[3], r0[3]) - std::max(r[1], r0[1]));
            const float inter = ix * iy, area = (r[2] - r[0]) * (r[3] - r[1]);
            if (k != c && inter / std::max(area + area0 - inter, 1e-9f) <= kNmsIou) continue;
            used[k] = true;
            const float p = prob(i);
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
        Detection& d = found[(*count)++];
        RoiFromPoints(float((kp[0] * kDetSize - padX) / s), float((kp[1] * kDetSize - padY) / s),
                      float((kp[2] * kDetSize - padX) / s), float((kp[3] * kDetSize - padY) / s), kRoiScale, d.roi);
        d.score = prob(best);
    }
    return true;
}

void PoseNpu::StartTracks(const Detection* found, int count) {
    for (int k = 0; k < count; k++) {
        bool followed = false;
        for (const Track& t : tracks_)
            if (t.active && SamePerson(t.roi, found[k].roi)) followed = true;
        if (followed) continue;
        for (int i = 0; i < maxPeople_; i++) {
            Track& t = tracks_[i];
            if (t.active) continue;
            t.active = true;
            t.id = nextId_++;
            std::copy(found[k].roi, found[k].roi + 4, t.roi);
            t.ResetFilters();
            break;
        }
    }
}

void PoseNpu::DropDuplicates() {
    for (int i = 0; i < kMaxPeople; i++)
        for (int j = i + 1; j < kMaxPeople; j++) {
            Track &a = tracks_[i], &b = tracks_[j];
            if (!a.active || !b.active || !SamePerson(a.roi, b.roi)) continue;
            Track& newer = a.id > b.id ? a : b;   // the one followed longer keeps the person (and its id)
            newer.active = false;
            newer.ResetFilters();
        }
}

int PoseNpu::Schedule(int64_t timestampNs, int runs[kMaxPeople]) {
    int active[kMaxPeople], n = 0;
    for (int i = 0; i < kMaxPeople; i++)
        if (tracks_[i].active) active[n++] = i;
    const bool twin = twin_ && n == 1 && maxPeople_ >= 2;
    const int people = n + (twin ? 1 : 0);
    if (people == 0) return 0;
    int count = people;
    if (people > 1 && runsPerSecond_ > 0) {
        // the budget: runs saved up since the last frame, at least one a frame (one person never waits)
        double dt = creditNs_ > 0 ? double(timestampNs - creditNs_) * 1e-9 : 1.0 / 30.0;
        dt = std::min(std::max(dt, 0.0), 0.1);
        credit_ = std::min(credit_ + runsPerSecond_ * dt, double(people));
        count = std::max(1, std::min(people, int(credit_)));
        credit_ = std::max(0.0, credit_ - count);
    } else {
        credit_ = 0;
    }
    creditNs_ = timestampNs;
    // taking turns: this frame starts with whoever's turn it is
    for (int k = 0; k < count; k++) {
        const int pick = (nextTurn_ + k) % people;
        runs[k] = pick < n ? active[pick] : -1;
    }
    nextTurn_ = (nextTurn_ + count) % people;
    return count;
}

bool PoseNpu::RunLandmarks(const float roi[4], int width, int height, Pose* out, float aux[2][2]) {
    if (!lm_->Run(kLmSize, &error_, "landmarks")) return false;
    out->npuMs = float(lm_->npuMs);
    float m[6];
    CropMatrix(roi, m);
    const float* raw = lm_->out[lm_->Find(kLandmarks * 5)].data();
    const float* world = lm_->out[lm_->Find(kLandmarks * 3)].data();
    const int hi = lm_->Find(kHeat * kHeat * kLandmarks);
    const bool channelsFirst = hi >= 0 && lm_->attrs[hi].n_dims == 4 && lm_->attrs[hi].dims[1] == kLandmarks;
    const __fp16* heat16 = hi >= 0 && !lm_->half[hi].empty() ? lm_->half[hi].data() : nullptr;
    const float* heat = hi >= 0 && !heat16 ? lm_->out[hi].data() : nullptr;
    out->score = lm_->out[lm_->Find(1)][0];
    const float c = std::cos(roi[3]), s = std::sin(roi[3]);
    for (int i = 0; i < kLandmarks; i++) {
        const float* r = raw + i * 5;
        float x = r[0] / kLmSize, y = r[1] / kLmSize;
        if (heat16) Refine(heat16, channelsFirst, i, &x, &y);
        else if (heat) Refine(heat, channelsFirst, i, &x, &y);
        const float X = m[0] * x * kLmSize + m[1] * y * kLmSize + m[2];
        const float Y = m[3] * x * kLmSize + m[4] * y * kLmSize + m[5];
        if (i == 33 || i == 34) {
            aux[i - 33][0] = X;
            aux[i - 33][1] = Y;
        }
        if (i >= kPoseLandmarks) continue;
        out->lm[i][0] = X / width;
        out->lm[i][1] = Y / height;
        out->lm[i][2] = r[2] / kLmSize * roi[2] / width;
        out->lm[i][3] = Sigmoid(r[3]);
        out->lm[i][4] = Sigmoid(r[4]);
        const float* w = world + i * 3;
        out->world[i][0] = c * w[0] - s * w[1];
        out->world[i][1] = s * w[0] + c * w[1];
        out->world[i][2] = w[2];
    }
    return true;
}

bool PoseNpu::Follow(int index, int id, int width, int height, int64_t timestampNs, Pose* pose, float aux[2][2]) {
    Track& t = tracks_[index];
    pose->track = id;
    if (t.bench) {   // a bench crop: its run is all it's for
        pose->found = false;
        return true;
    }
    pose->found = pose->score >= kMinPresence;
    if (!pose->found) {  // lost: the detector looks again (at once if nobody is left)
        std::lock_guard<std::mutex> lock(state_);
        if (t.active && t.id == id) {
            t.active = false;
            t.ResetFilters();
        }
        return false;
    }
    // MediaPipe's pose filtering: visibility low-passed, landmarks One Euro in picture pixels with speeds relative to
    // the body's size (the alignment points' box), world landmarks One Euro in metres; the alignment points the next
    // crop comes from go through the relative velocity filter.
    const float box = 2.f * std::hypot(aux[1][0] - aux[0][0], aux[1][1] - aux[0][1]);
    const double scale = box > 1e-6f ? 1.0 / box : 0.0;
    const float size[3] = {(float)width, (float)height, (float)width};
    for (int i = 0; i < kPoseLandmarks; i++) {
        pose->lm[i][3] = (float)t.vis[i].Apply(pose->lm[i][3]);
        if (scale > 0)
            for (int k = 0; k < 3; k++)
                pose->lm[i][k] = float(t.lm[i * 3 + k].Apply(timestampNs, scale, pose->lm[i][k] * size[k]) / size[k]);
        for (int k = 0; k < 3; k++)
            pose->world[i][k] = float(t.world[i * 3 + k].Apply(timestampNs, 1.0, pose->world[i][k]));
    }
    if (scale > 0)
        for (int j = 0; j < 2; j++)
            for (int k = 0; k < 2; k++) aux[j][k] = t.aux[j * 2 + k].Apply(timestampNs, (float)scale, aux[j][k]);
    float next[4];
    RoiFromPoints(aux[0][0], aux[0][1], aux[1][0], aux[1][1], kRoiScale, next);
    std::lock_guard<std::mutex> lock(state_);
    if (!t.active || t.id != id) return true;
    // Two people close together: this track's crop held both, and the landmark model followed the other one, whom
    // another track already follows. The track that was there keeps that person; this one ends (its pose isn't sent),
    // and the detector finds its own person again, a new track the game matches to the body by position. Before
    // (2026-11-05) the newer of the two tracks ended instead, often the one still on its own person: the players'
    // identities swapped after they stood close together (2026-11-06).
    for (const Track& o : tracks_)
        if (&o != &t && o.active && !o.bench && SamePerson(next, o.roi)) {
            t.active = false;
            t.ResetFilters();
            pose->found = false;
            return false;
        }
    std::copy(next, next + 4, t.roi);
    return true;
}

bool PoseNpu::LandmarksAt(const Rgba& image, const float roi[4], Pose* out) {
    error_.clear();
    out->detected = false;
    out->detectMs = 0;
    float aux[2][2];
    const double l0 = NowMs();
    out->cropMs = warpMs_[1] = CropLandmarks(image, roi, lm_->target);
    if (!RunLandmarks(roi, image.width, image.height, out, aux)) return false;
    out->landmarkMs = out->totalMs = float(NowMs() - l0);
    std::copy(roi, roi + 4, out->roi);
    out->found = out->score >= kMinPresence;
    return true;
}

bool PoseNpu::Process(const Rgba& image, int64_t timestampNs, Frame* out) {
    const double t0 = NowMs();
    error_.clear();
    *out = Frame();
    // the detector: nobody followed (every frame), or fewer than wanted (once per redetect interval)
    bool detect;
    {
        std::lock_guard<std::mutex> lock(state_);
        if (bench_) FillBench(image.width, image.height);
        const int n = FollowedLocked();
        detect = !bench_ && (n == 0 || (!twin_ && n < maxPeople_ && timestampNs - lastDetectNs_ >= kRedetectNs));
        if (detect) lastDetectNs_ = timestampNs;
    }
    if (detect) {
        out->detected = true;
        const double d0 = NowMs();
        float letterbox[3];
        DetectorInput(image, det_->target.bytes, letterbox);
        Detection found[kMaxDetections];
        int count = 0;
        const bool ok = RunDetector(letterbox, found, &count);
        out->detectMs = float(NowMs() - d0);
        if (!ok) return false;
        std::lock_guard<std::mutex> lock(state_);
        StartTracks(found, count);
    }
    // the landmark model: right after a detection everyone followed (MediaPipe runs it on the detector's crop in the same
    // frame), else those due this frame
    int runs[kMaxPeople], ids[kMaxPeople], count;
    float rois[kMaxPeople][4];
    {
        std::lock_guard<std::mutex> lock(state_);
        if (detect) {
            count = 0;
            for (int i = 0; i < kMaxPeople; i++)
                if (tracks_[i].active) runs[count++] = i;
        } else {
            count = Schedule(timestampNs, runs);
        }
        int single = -1;
        for (int i = 0; i < kMaxPeople; i++)
            if (tracks_[i].active) single = i;
        for (int k = 0; k < count; k++) {
            const Track& t = tracks_[runs[k] >= 0 ? runs[k] : single];
            ids[k] = t.id;
            std::copy(t.roi, t.roi + 4, rois[k]);
        }
    }
    const double l0 = NowMs();
    for (int k = 0; k < count; k++) {
        Pose pose;
        float aux[2][2];
        pose.cropMs = warpMs_[1] = CropLandmarks(image, rois[k], lm_->target);
        out->cropMs += pose.cropMs;
        if (!RunLandmarks(rois[k], image.width, image.height, &pose, aux)) {
            Reset();
            return false;
        }
        out->npuMs += pose.npuMs;
        if (runs[k] < 0) continue;   // the twin: only its cost counts
        std::copy(rois[k], rois[k] + 4, pose.roi);
        pose.detected = out->detected;
        if (!Follow(runs[k], ids[k], image.width, image.height, timestampNs, &pose, aux)) out->lost++;
        out->people[out->count++] = pose;
    }
    out->landmarkMs = float(NowMs() - l0);
    {
        std::lock_guard<std::mutex> lock(state_);
        DropDuplicates();
        out->followed = FollowedLocked();
    }
    out->totalMs = float(NowMs() - t0);
    for (int k = 0; k < out->count; k++) {
        Pose& p = out->people[k];
        p.detectMs = out->detectMs;
        p.landmarkMs = out->landmarkMs;
        p.totalMs = out->totalMs;
    }
    return true;
}

bool PoseNpu::Process(const Rgba& image, int64_t timestampNs, Pose* out) {
    Frame frame;
    const bool ok = Process(image, timestampNs, &frame);
    if (frame.count > 0) *out = frame.people[0];
    else *out = Pose();
    out->detected = frame.detected;
    out->detectMs = frame.detectMs;
    out->landmarkMs = frame.landmarkMs;
    out->totalMs = frame.totalMs;
    out->cropMs = frame.cropMs;
    out->npuMs = frame.npuMs;
    return ok;
}

int PoseNpu::Prepare(const Rgba& image, int64_t timestampNs) {
    int runs[kMaxPeople], ids[kMaxPeople], count;
    float rois[kMaxPeople][4];
    bool detect;
    {
        std::lock_guard<std::mutex> lock(state_);
        if (bench_) FillBench(image.width, image.height);
        const int n = FollowedLocked();
        if (n == 0) return -1;
        count = Schedule(timestampNs, runs);
        int single = -1;
        for (int i = 0; i < kMaxPeople; i++)
            if (tracks_[i].active) single = i;
        for (int k = 0; k < count; k++) {
            const Track& t = tracks_[runs[k] >= 0 ? runs[k] : single];
            ids[k] = t.id;
            std::copy(t.roi, t.roi + 4, rois[k]);
        }
        detect = !twin_ && !bench_ && n < maxPeople_ && timestampNs - lastDetectNs_ >= kRedetectNs;
        if (detect) lastDetectNs_ = timestampNs;
    }
    const int slot = nextSlot_;
    nextSlot_ ^= 1;
    Job& job = jobs_[slot];
    job.count = count;
    job.width = image.width;
    job.height = image.height;
    job.timestampNs = timestampNs;
    job.cropMs = 0;
    for (int k = 0; k < count; k++) {
        job.track[k] = runs[k];
        job.id[k] = ids[k];
        std::copy(rois[k], rois[k] + 4, job.roi[k]);
        job.cropMs += warpMs_[1] = CropLandmarks(image, rois[k], lm_->Staging(slot, k));
    }
    job.detect = detect;
    if (detect) DetectorInput(image, det_->Staging(slot, 0).bytes, job.letterbox);
    return slot;
}

bool PoseNpu::RunJob(int slot, Frame* out) {
    const double t0 = NowMs();
    error_.clear();
    *out = Frame();
    const Job& job = jobs_[slot];
    out->cropMs = job.cropMs;
    for (int k = 0; k < job.count; k++) {
        Pose pose;
        float aux[2][2];
        lm_->Load(slot, k);
        if (!RunLandmarks(job.roi[k], job.width, job.height, &pose, aux)) {
            Reset();
            return false;
        }
        out->npuMs += pose.npuMs;
        if (job.track[k] < 0) continue;   // the twin: only its cost counts
        std::copy(job.roi[k], job.roi[k] + 4, pose.roi);
        pose.cropMs = job.cropMs / job.count;
        if (!Follow(job.track[k], job.id[k], job.width, job.height, job.timestampNs, &pose, aux)) out->lost++;
        out->people[out->count++] = pose;
    }
    out->landmarkMs = float(NowMs() - t0);
    if (job.detect) {
        // the detector after the landmark runs (their poses are this frame's either way): new people start being followed
        out->detected = true;
        const double d0 = NowMs();
        det_->Load(slot, 0);
        Detection found[kMaxDetections];
        int count = 0;
        const bool ok = RunDetector(job.letterbox, found, &count);
        out->detectMs = float(NowMs() - d0);
        if (!ok) {
            Reset();
            return false;
        }
        std::lock_guard<std::mutex> lock(state_);
        StartTracks(found, count);
    }
    {
        std::lock_guard<std::mutex> lock(state_);
        DropDuplicates();
        out->followed = FollowedLocked();
    }
    out->totalMs = float(NowMs() - t0);
    for (int k = 0; k < out->count; k++) {
        Pose& p = out->people[k];
        p.detected = out->detected;
        p.detectMs = out->detectMs;
        p.landmarkMs = out->landmarkMs;
        p.totalMs = out->totalMs;
    }
    return true;
}

bool PoseNpu::RunJob(int slot, Pose* out) {
    Frame frame;
    const bool ok = RunJob(slot, &frame);
    if (frame.count > 0) *out = frame.people[0];
    else *out = Pose();
    out->detected = frame.detected;
    out->detectMs = frame.detectMs;
    out->landmarkMs = frame.landmarkMs;
    out->totalMs = frame.totalMs;
    out->cropMs = frame.cropMs;
    out->npuMs = frame.npuMs;
    return ok;
}

}  // namespace tundu
