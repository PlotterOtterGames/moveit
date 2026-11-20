// The camera picture for the game, copied and flipped: FrameShare's pixel work (frame_share.cpp), kept apart from JNI
// so pose_npu_test --selftest can check and time it on the box.
//
// It runs on the camera thread for every frame a game shows (Vishu!, Lunapark!, Pikturo's camera mode), so its time is
// the pose AI's time. The first version resampled every output pixel on its own in plain C, at the camera's own size
// too: ~35 ms a frame for Vishu!'s 1024 px picture on the hot box (2026-11-11: three quarters of the camera thread, and
// the AI saw two frames in three). Measured there (pose_npu_test --selftest, CPU time at 1.4 GHz with a game running;
// docs/hardware.md "The camera picture's cost"):
//   the camera's own size   a copy                                                ~2.1 ms a 1024 x 768 frame
//   exactly half            each pixel the average of its 2 x 2 camera pixels     ~2.0 ms
//   any other size          two pixels blended across, then two rows              ~8.5 ms for 640 x 480 (and ~24 ms in
//                           plain C): not offered any more
// So the game gets one of the two cheap sizes (SharedSize). Both go through the picture one row at a time, in order:
// the box's in-order cores stall on every cache line when a loop reads several camera rows side by side.
#pragma once

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <vector>

#if defined(__aarch64__)
#include <arm_neon.h>
#endif

namespace tundu {

// The size handed over when the game would like a picture `wanted` pixels wide: the camera's own, or exactly half of it
// when that is still at least three quarters of the wanted width (640 wanted of a 1024 px camera: 512 x 384; in Lunapark!
// that took the app from 172 % to 147 % of a core against a 640 px blend, the game's main thread from 20 to 16 ms, and
// a full-size picture only moved the camera thread's work onto the game's main thread).
inline void SharedSize(int width, int height, int wanted, int* w, int* h) {
    const bool half = width >= 32 && height >= 32 && (width / 2) * 4 >= wanted * 3;
    *w = half ? width / 2 : width;
    *h = half ? height / 2 : height;
}

class FrameScaler {
public:
    // false = the plain C path (the reference the ARM path is checked against, byte for byte).
    void Simd(bool on) { simd_ = on; }

    // An RGBA frame (sw x sh, stride bytes a row) into RGBA w x h, either its own size or half of it (SharedSize): rows
    // bottom-up as Unity textures want them, opaque. False for any other size.
    bool Run(const uint8_t* src, int sw, int sh, int stride, uint8_t* dst, int w, int h) {
        if (w < 1 || h < 1) return false;
        if (w == sw && h == sh) {
            for (int y = 0; y < h; y++) CopyRow(src + (size_t)y * stride, dst + (size_t)(h - 1 - y) * w * 4, w);
            return true;
        }
        if (w == sw / 2 && h == sh / 2) {
            sums_.resize((size_t)w * 3);
            for (int y = 0; y < h; y++) {
                HalfSums(src + (size_t)(2 * y) * stride, w);
                HalfRow(src + (size_t)(2 * y + 1) * stride, dst + (size_t)(h - 1 - y) * w * 4, w);
            }
            return true;
        }
        return false;
    }

private:
    void CopyRow(const uint8_t* src, uint8_t* dst, int w) const {
        int x = 0;
#if defined(__aarch64__)
        if (simd_) {
            const uint8x16_t opaque = vreinterpretq_u8_u32(vdupq_n_u32(0xFF000000u));
            for (; x + 4 <= w; x += 4) vst1q_u8(dst + x * 4, vorrq_u8(vld1q_u8(src + x * 4), opaque));
        }
#endif
        for (; x < w; x++) {
            dst[x * 4] = src[x * 4];
            dst[x * 4 + 1] = src[x * 4 + 1];
            dst[x * 4 + 2] = src[x * 4 + 2];
            dst[x * 4 + 3] = 255;
        }
    }

    // Half size, the upper camera row of an output row: each pixel pair's sum per colour (w reds, then greens, blues).
    void HalfSums(const uint8_t* row, int w) {
        uint16_t* sums = sums_.data();
        int x = 0;
#if defined(__aarch64__)
        if (simd_) {
            for (; x + 8 <= w; x += 8) {    // 16 camera pixels
                const uint8x16x4_t a = vld4q_u8(row + x * 8);
                vst1q_u16(sums + x, vpaddlq_u8(a.val[0]));
                vst1q_u16(sums + w + x, vpaddlq_u8(a.val[1]));
                vst1q_u16(sums + 2 * w + x, vpaddlq_u8(a.val[2]));
            }
        }
#endif
        for (; x < w; x++) {
            const uint8_t* a = row + x * 8;
            for (int k = 0; k < 3; k++) sums[k * w + x] = (uint16_t)(a[k] + a[k + 4]);
        }
    }

    // ... and the lower camera row: with the sums, the average of the four (rounded).
    void HalfRow(const uint8_t* row, uint8_t* out, int w) const {
        const uint16_t* sums = sums_.data();
        int x = 0;
#if defined(__aarch64__)
        if (simd_) {
            for (; x + 8 <= w; x += 8) {
                const uint8x16x4_t a = vld4q_u8(row + x * 8);
                uint8x8x4_t o;
                o.val[0] = vrshrn_n_u16(vaddq_u16(vld1q_u16(sums + x), vpaddlq_u8(a.val[0])), 2);
                o.val[1] = vrshrn_n_u16(vaddq_u16(vld1q_u16(sums + w + x), vpaddlq_u8(a.val[1])), 2);
                o.val[2] = vrshrn_n_u16(vaddq_u16(vld1q_u16(sums + 2 * w + x), vpaddlq_u8(a.val[2])), 2);
                o.val[3] = vdup_n_u8(255);
                vst4_u8(out + x * 4, o);
            }
        }
#endif
        for (; x < w; x++) {
            const uint8_t* a = row + x * 8;
            for (int k = 0; k < 3; k++) out[x * 4 + k] = (uint8_t)((sums[k * w + x] + a[k] + a[k + 4] + 2) >> 2);
            out[x * 4 + 3] = 255;
        }
    }

    bool simd_ = true;
    std::vector<uint16_t> sums_;
};

}  // namespace tundu
