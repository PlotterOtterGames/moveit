// The camera picture handed to the game inside the process (Vishu!, Lunapark!, Pikturo's camera mode): the camera
// thread puts each frame here, at the camera's own size or half of it, whichever is nearer the width the game asked
// for (frame_scale.h), and keeps the last few (a game that draws on the player shows an older one, waiting for its
// poses: CameraFeed.History / ShowAt). On the box this replaced a JPEG encode (Java) + UDP + JPEG decode (Unity's main
// thread) per frame. Rows are stored bottom-up, as Unity textures want them.
//
// Two ways into the game's texture:
// - tundu_frame_pick + tundu_frame_request + the render event (GL.IssuePluginEvent): Unity's render thread uploads the
//   chosen frame straight from here into the texture (glTexSubImage2D). Nothing is copied on the game's main thread.
//   Since 2026-11-12: before, the game copied each 3 MB frame twice there (into its own history, then into the
//   texture's memory), ~18 % of Vishu!'s main thread on the box.
// - tundu_frame_copy: the newest frame copied into the game's memory (the fallback; OpenGL ES only for the first).
#include <GLES3/gl3.h>
#include <jni.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <vector>

#include "frame_scale.h"

namespace {

constexpr int kMaxKeep = 8;             // frames a game may keep (CameraFeed's MaxHistory)
constexpr int kRequests = 4;            // uploads that may wait for the render thread at once (event ids 0..3)
constexpr int kSlots = kMaxKeep + 1 + kRequests;   // + the one being written

struct Slot {
    std::vector<uint8_t> data;          // RGBA, bottom-up
    int width = 0, height = 0;
    int64_t time = 0, number = 0;       // number 0 = nothing (yet), or being written
    int pins = 0;                       // uploads waiting for this frame: the camera thread leaves it alone
    bool writing = false;
};

struct Request {
    int slot = -1;
    int64_t number = 0;
    uint32_t texture = 0;
    int width = 0, height = 0;
};

std::mutex g_mutex;
Slot g_slots[kSlots];
Request g_requests[kRequests];
int g_keep = 1;
int g_newest = -1;                      // the slot of the newest frame
tundu::FrameScaler g_scaler;            // the camera thread's only

// Which slots hold the newest g_keep frames (by number). Locked.
int KeptSlots(int* kept) {
    int n = 0;
    for (int k = 0; k < g_keep; k++) {
        int best = -1;
        for (int i = 0; i < kSlots; i++) {
            const Slot& s = g_slots[i];
            if (s.number == 0 || s.writing) continue;
            bool taken = false;
            for (int j = 0; j < n; j++) taken |= kept[j] == i;
            if (!taken && (best < 0 || s.number > g_slots[best].number)) best = i;
        }
        if (best < 0) break;
        kept[n++] = best;
    }
    return n;
}

bool IsIn(int slot, const int* list, int n) {
    for (int j = 0; j < n; j++)
        if (list[j] == slot) return true;
    return false;
}

// A slot the camera thread may write: not pinned, not among the newest g_keep frames; the oldest that already has its
// memory (so only as many 3 MB frames exist as are kept, plus the one written and any waiting for an upload), else an
// unused one. Locked.
int FreeSlot() {
    int kept[kMaxKeep];
    int nKept = KeptSlots(kept);
    int free = -1;
    for (int i = 0; i < kSlots; i++) {
        const Slot& s = g_slots[i];
        if (s.writing || s.pins > 0 || IsIn(i, kept, nKept)) continue;
        if (free < 0) {
            free = i;
            continue;
        }
        const Slot& f = g_slots[free];
        bool used = !s.data.empty(), freeUsed = !f.data.empty();
        if (used != freeUsed ? used : s.number < f.number) free = i;
    }
    return free;
}

// The render thread: the frame into the game's texture. Unity's GL state is put back as it was.
void Upload(const Slot& s, const Request& r) {
    GLint bound = 0, align = 4, rowLength = 0, unpackBuffer = 0;
    glGetIntegerv(GL_TEXTURE_BINDING_2D, &bound);
    glGetIntegerv(GL_UNPACK_ALIGNMENT, &align);
    glGetIntegerv(GL_UNPACK_ROW_LENGTH, &rowLength);
    glGetIntegerv(GL_PIXEL_UNPACK_BUFFER_BINDING, &unpackBuffer);
    if (unpackBuffer != 0) glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
    if (align != 4) glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
    if (rowLength != 0) glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
    glBindTexture(GL_TEXTURE_2D, r.texture);
    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, s.width, s.height, GL_RGBA, GL_UNSIGNED_BYTE, s.data.data());
    glBindTexture(GL_TEXTURE_2D, (GLuint)bound);
    if (rowLength != 0) glPixelStorei(GL_UNPACK_ROW_LENGTH, rowLength);
    if (align != 4) glPixelStorei(GL_UNPACK_ALIGNMENT, align);
    if (unpackBuffer != 0) glBindBuffer(GL_PIXEL_UNPACK_BUFFER, (GLuint)unpackBuffer);
}

// GL.IssuePluginEvent's callback, on Unity's render thread with its GL context current.
void OnRenderEvent(int id) {
    if (id < 0 || id >= kRequests) return;
    Request r;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        r = g_requests[id];
        g_requests[id] = Request{};
    }
    if (r.slot < 0) return;
    // pinned: the camera thread doesn't touch this slot until the pin is released
    const Slot& s = g_slots[r.slot];
    if (s.number == r.number && s.width == r.width && s.height == r.height && r.texture != 0) Upload(s, r);
    std::lock_guard<std::mutex> lock(g_mutex);
    g_slots[r.slot].pins--;
}

}  // namespace

extern "C" {

// Java (the camera thread): a frame, RGBA in a direct buffer.
JNIEXPORT void JNICALL Java_al_moveit_motion_FrameShare_nativeOffer(JNIEnv* env, jclass, jobject buffer, jint width,
                                                                     jint height, jint rowStride, jint targetWidth,
                                                                     jlong captureWallMs, jlong number) {
    const uint8_t* src = (const uint8_t*)env->GetDirectBufferAddress(buffer);
    if (!src || width < 2 || height < 2 || rowStride < width * 4) return;
    // the camera's own size, or half of it when the game wants less (frame_scale.h: the two sizes that cost ~2 ms)
    int w, h;
    tundu::SharedSize(width, height, targetWidth, &w, &h);
    int slot;
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        slot = FreeSlot();
        if (slot < 0) return;
        g_slots[slot].writing = true;
        g_slots[slot].number = 0;
    }
    Slot& s = g_slots[slot];
    s.data.resize((size_t)w * h * 4);
    bool ok = g_scaler.Run(src, width, height, rowStride, s.data.data(), w, h);
    std::lock_guard<std::mutex> lock(g_mutex);
    s.writing = false;
    if (!ok) return;
    s.width = w;
    s.height = h;
    s.time = captureWallMs;
    s.number = number;
    if (g_newest < 0 || g_slots[g_newest].number < number) g_newest = slot;
}

// The game: the newest frame's number (0 = none yet), size and capture time (Unix ms).
__attribute__((visibility("default"))) int64_t tundu_frame_info(int32_t* width, int32_t* height, int64_t* time) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_newest < 0 || g_slots[g_newest].number == 0) return 0;
    const Slot& s = g_slots[g_newest];
    *width = s.width;
    *height = s.height;
    *time = s.time;
    return s.number;
}

// The game: copies the newest frame (RGBA, bottom-up) into dst if it fits; returns its number, or 0.
__attribute__((visibility("default"))) int64_t tundu_frame_copy(uint8_t* dst, int32_t capacity, int32_t* width,
                                                                int32_t* height, int64_t* time) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_newest < 0 || g_slots[g_newest].number == 0) return 0;
    const Slot& s = g_slots[g_newest];
    const size_t size = (size_t)s.width * s.height * 4;
    if (size == 0 || size > (size_t)capacity) return 0;
    std::memcpy(dst, s.data.data(), size);
    *width = s.width;
    *height = s.height;
    *time = s.time;
    return s.number;
}

// The game: how many recent frames to keep for tundu_frame_pick (1..8). Fewer than before: the others' memory goes.
__attribute__((visibility("default"))) void tundu_frame_keep(int32_t count) {
    std::lock_guard<std::mutex> lock(g_mutex);
    int keep = std::max(1, std::min(kMaxKeep, (int)count));
    bool fewer = keep < g_keep;
    g_keep = keep;
    if (!fewer) return;
    int kept[kMaxKeep];
    int nKept = KeptSlots(kept);
    for (int i = 0; i < kSlots; i++) {
        Slot& s = g_slots[i];
        if (s.writing || s.pins > 0 || IsIn(i, kept, nKept)) continue;
        std::vector<uint8_t>().swap(s.data);
        s.number = 0;
    }
}

// The game: the newest kept frame captured at or before `at` (Unix ms; 0 or less = the newest), or the oldest kept when
// none is that old. Returns its number (0 = none yet), with its size and capture time.
__attribute__((visibility("default"))) int64_t tundu_frame_pick(int64_t at, int32_t* width, int32_t* height,
                                                                int64_t* time) {
    std::lock_guard<std::mutex> lock(g_mutex);
    int best = -1, oldest = -1;
    for (int i = 0; i < kSlots; i++) {
        const Slot& s = g_slots[i];
        if (s.number == 0 || s.writing) continue;
        if (oldest < 0 || s.number < g_slots[oldest].number) oldest = i;
        if ((at <= 0 || s.time <= at) && (best < 0 || s.number > g_slots[best].number)) best = i;
    }
    if (best < 0) best = oldest;
    if (best < 0) return 0;
    const Slot& s = g_slots[best];
    *width = s.width;
    *height = s.height;
    *time = s.time;
    return s.number;
}

// The game: upload frame `number` into the OpenGL ES texture `texture` (width x height RGBA8, made by Unity) when the
// render thread reaches render event `id` (0..3, GL.IssuePluginEvent with tundu_frame_render_func). Returns 1 if the
// frame is still here (it stays until the upload is done), 0 if not.
__attribute__((visibility("default"))) int32_t tundu_frame_request(int32_t id, int64_t number, uint32_t texture,
                                                                   int32_t width, int32_t height) {
    if (id < 0 || id >= kRequests || number == 0 || texture == 0) return 0;
    std::lock_guard<std::mutex> lock(g_mutex);
    Request& r = g_requests[id];
    if (r.slot >= 0) g_slots[r.slot].pins--;   // an earlier request on this id that never ran
    r = Request{};
    for (int i = 0; i < kSlots; i++) {
        Slot& s = g_slots[i];
        if (s.number != number || s.writing || s.width != width || s.height != height) continue;
        s.pins++;
        r.slot = i;
        r.number = number;
        r.texture = texture;
        r.width = width;
        r.height = height;
        return 1;
    }
    return 0;
}

// The render event's callback for GL.IssuePluginEvent.
__attribute__((visibility("default"))) void* tundu_frame_render_func() {
    return (void*)&OnRenderEvent;
}

}  // extern "C"
