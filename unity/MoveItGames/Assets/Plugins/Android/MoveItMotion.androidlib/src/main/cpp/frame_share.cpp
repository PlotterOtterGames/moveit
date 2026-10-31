// The camera picture handed to the game inside the process (Vishu!, Pikturo's camera mode): the camera thread puts
// the newest frame here, downscaled to the width the game asked for, and the game copies it straight into its
// texture (CameraFeed.cs, DllImport "tundupose"). On the box this replaced a JPEG encode (Java) + UDP + JPEG decode
// (Unity's main thread) per frame. Rows are stored bottom-up, as Unity textures want them.
#include <jni.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <vector>

namespace {

std::mutex g_mutex;
std::vector<uint8_t> g_front, g_back;   // RGBA
int g_width = 0, g_height = 0;
int64_t g_time = 0, g_number = 0;

// Bilinear downscale of an RGBA frame into RGBA w x h, flipped vertically (fixed point, like pose_npu's warp).
void Downscale(const uint8_t* src, int sw, int sh, int stride, uint8_t* dst, int w, int h) {
    const int32_t sx = (int32_t)((int64_t)sw * 65536 / w), sy = (int32_t)((int64_t)sh * 65536 / h);
    for (int y = 0; y < h; y++) {
        const int32_t fy = std::max(0, (int32_t)(((int64_t)y * sy) + sy / 2 - 32768));
        const int y0 = std::min(fy >> 16, sh - 1), y1 = std::min(y0 + 1, sh - 1), ay = (fy >> 8) & 0xFF;
        const uint8_t* r0 = src + (size_t)y0 * stride;
        const uint8_t* r1 = src + (size_t)y1 * stride;
        uint8_t* out = dst + (size_t)(h - 1 - y) * w * 4;
        int32_t fx = sx / 2 - 32768;
        for (int x = 0; x < w; x++, fx += sx, out += 4) {
            const int32_t cx = std::max(0, fx);
            const int x0 = std::min(cx >> 16, sw - 1), x1 = std::min(x0 + 1, sw - 1), ax = (cx >> 8) & 0xFF;
            const uint8_t* a = r0 + x0 * 4;
            const uint8_t* b = r0 + x1 * 4;
            const uint8_t* c = r1 + x0 * 4;
            const uint8_t* d = r1 + x1 * 4;
            for (int k = 0; k < 3; k++) {
                const int top = a[k] * (256 - ax) + b[k] * ax, bottom = c[k] * (256 - ax) + d[k] * ax;
                out[k] = (uint8_t)((top * (256 - ay) + bottom * ay + 32768) >> 16);
            }
            out[3] = 255;
        }
    }
}

}  // namespace

extern "C" {

// Java (the camera thread): the newest frame, RGBA in a direct buffer.
JNIEXPORT void JNICALL Java_al_moveit_motion_FrameShare_nativeOffer(JNIEnv* env, jclass, jobject buffer, jint width,
                                                                     jint height, jint rowStride, jint targetWidth,
                                                                     jlong captureWallMs, jlong number) {
    const uint8_t* src = (const uint8_t*)env->GetDirectBufferAddress(buffer);
    if (!src || width <= 0 || height <= 0) return;
    const int w = std::max(16, std::min((int)targetWidth, (int)width));
    const int h = std::max(16, (int)((int64_t)height * w / width));
    g_back.resize((size_t)w * h * 4);
    Downscale(src, width, height, rowStride, g_back.data(), w, h);
    std::lock_guard<std::mutex> lock(g_mutex);
    std::swap(g_front, g_back);
    g_width = w;
    g_height = h;
    g_time = captureWallMs;
    g_number = number;
}

// The game: the newest frame's number (0 = none yet), size and capture time (Unix ms).
__attribute__((visibility("default"))) int64_t tundu_frame_info(int32_t* width, int32_t* height, int64_t* time) {
    std::lock_guard<std::mutex> lock(g_mutex);
    *width = g_width;
    *height = g_height;
    *time = g_time;
    return g_number;
}

// The game: copies the newest frame (RGBA, bottom-up) into dst if it fits; returns its number, or 0.
__attribute__((visibility("default"))) int64_t tundu_frame_copy(uint8_t* dst, int32_t capacity, int32_t* width,
                                                                int32_t* height, int64_t* time) {
    std::lock_guard<std::mutex> lock(g_mutex);
    const size_t size = (size_t)g_width * g_height * 4;
    if (g_number == 0 || size == 0 || size > (size_t)capacity) return 0;
    std::memcpy(dst, g_front.data(), size);
    *width = g_width;
    *height = g_height;
    *time = g_time;
    return g_number;
}

}  // extern "C"
