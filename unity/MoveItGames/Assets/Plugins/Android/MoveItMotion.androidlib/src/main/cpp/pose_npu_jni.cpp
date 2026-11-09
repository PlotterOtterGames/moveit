// JNI for al.moveit.motion.NpuPose: the camera frame (an RGBA Bitmap or the camera's own buffer, read in place) in, the
// frame's poses out as floats (up to tundu::kMaxPeople people).
#include <android/bitmap.h>
#include <jni.h>

#include <string>

#include "pose_npu.h"

using tundu::Frame;
using tundu::Pose;
using tundu::PoseNpu;

namespace {

std::string g_createError;

// Layout of the float array handed back (NpuPose.java reads the same offsets): the frame, then one block per person.
constexpr int kHeader = 10;  // count, detected, detect ms, landmark ms, total ms, followed, crop ms, NPU ms, lost, -
constexpr int kPerson = 4 + tundu::kPoseLandmarks * 5 + tundu::kPoseLandmarks * 3 + 4;  // track, found, score, -, ...
constexpr int kOutSize = kHeader + tundu::kMaxPeople * kPerson;

PoseNpu* Get(jlong handle) { return reinterpret_cast<PoseNpu*>(handle); }

// The number of people found (0..kMaxPeople).
jint Pack(JNIEnv* env, const Frame& frame, jfloatArray out) {
    float buf[kOutSize] = {};
    buf[0] = (float)frame.count;
    buf[1] = frame.detected ? 1.f : 0.f;
    buf[2] = frame.detectMs;
    buf[3] = frame.landmarkMs;
    buf[4] = frame.totalMs;
    buf[5] = (float)frame.followed;
    buf[6] = frame.cropMs;
    buf[7] = frame.npuMs;
    buf[8] = (float)frame.lost;
    for (int k = 0; k < frame.count; k++) {
        const Pose& pose = frame.people[k];
        float* p = buf + kHeader + k * kPerson;
        *p++ = (float)pose.track;
        *p++ = pose.found ? 1.f : 0.f;
        *p++ = pose.score;
        *p++ = 0.f;
        for (int i = 0; i < tundu::kPoseLandmarks; i++)
            for (int c = 0; c < 5; c++) *p++ = pose.lm[i][c];
        for (int i = 0; i < tundu::kPoseLandmarks; i++)
            for (int c = 0; c < 3; c++) *p++ = pose.world[i][c];
        for (int c = 0; c < 4; c++) *p++ = pose.roi[c];
    }
    env->SetFloatArrayRegion(out, 0, kOutSize, buf);
    return frame.Found();
}

}  // namespace

extern "C" {

JNIEXPORT jlong JNICALL Java_al_moveit_motion_NpuPose_nativeCreate(JNIEnv* env, jclass, jobject detector,
                                                                    jobject landmarks) {
    void* d = env->GetDirectBufferAddress(detector);
    void* l = env->GetDirectBufferAddress(landmarks);
    const jlong ds = env->GetDirectBufferCapacity(detector), ls = env->GetDirectBufferCapacity(landmarks);
    if (!d || !l || ds <= 0 || ls <= 0) {
        g_createError = "models must be direct buffers";
        return 0;
    }
    auto* pose = new PoseNpu();
    if (!pose->Load(d, (size_t)ds, l, (size_t)ls)) {
        g_createError = pose->Error();
        delete pose;
        return 0;
    }
    return reinterpret_cast<jlong>(pose);
}

JNIEXPORT jstring JNICALL Java_al_moveit_motion_NpuPose_nativeCreateError(JNIEnv* env, jclass) {
    return env->NewStringUTF(g_createError.c_str());
}

JNIEXPORT jstring JNICALL Java_al_moveit_motion_NpuPose_nativeError(JNIEnv* env, jclass, jlong handle) {
    return env->NewStringUTF(Get(handle)->Error().c_str());
}

JNIEXPORT jstring JNICALL Java_al_moveit_motion_NpuPose_nativeVersions(JNIEnv* env, jclass, jlong handle) {
    return env->NewStringUTF(Get(handle)->Versions().c_str());
}

JNIEXPORT jint JNICALL Java_al_moveit_motion_NpuPose_nativeOutputSize(JNIEnv*, jclass) { return kOutSize; }

JNIEXPORT void JNICALL Java_al_moveit_motion_NpuPose_nativeSetPeople(JNIEnv*, jclass, jlong handle, jint maxPeople,
                                                                      jfloat runsPerSecond, jboolean twin, jint keep,
                                                                      jboolean bench) {
    Get(handle)->SetPeople((int)maxPeople, (float)runsPerSecond, twin == JNI_TRUE, (int)keep, bench == JNI_TRUE);
}

// -1 = failed (nativeError), else the number of people found (their poses in out).
JNIEXPORT jint JNICALL Java_al_moveit_motion_NpuPose_nativeProcess(JNIEnv* env, jclass, jlong handle, jobject bitmap,
                                                                    jlong timestampNs, jfloatArray out) {
    AndroidBitmapInfo info;
    if (AndroidBitmap_getInfo(env, bitmap, &info) != ANDROID_BITMAP_RESULT_SUCCESS ||
        info.format != ANDROID_BITMAP_FORMAT_RGBA_8888)
        return -1;
    void* pixels = nullptr;
    if (AndroidBitmap_lockPixels(env, bitmap, &pixels) != ANDROID_BITMAP_RESULT_SUCCESS) return -1;
    Frame frame;
    const bool ok = Get(handle)->Process(
        tundu::Rgba{(const uint8_t*)pixels, (int)info.width, (int)info.height, (int)info.stride}, timestampNs, &frame);
    AndroidBitmap_unlockPixels(env, bitmap);
    return ok ? Pack(env, frame, out) : -1;
}

// The same from the camera's own RGBA buffer (a direct ByteBuffer): no copy into a Bitmap first.
JNIEXPORT jint JNICALL Java_al_moveit_motion_NpuPose_nativeProcessBuffer(JNIEnv* env, jclass, jlong handle,
                                                                          jobject buffer, jint width, jint height,
                                                                          jint rowStride, jlong timestampNs,
                                                                          jfloatArray out) {
    const uint8_t* pixels = (const uint8_t*)env->GetDirectBufferAddress(buffer);
    if (!pixels) return -1;
    Frame frame;
    const bool ok = Get(handle)->Process(tundu::Rgba{pixels, (int)width, (int)height, (int)rowStride}, timestampNs, &frame);
    return ok ? Pack(env, frame, out) : -1;
}

// Pipelined tracking (pose_npu.h): this frame's landmark crops into a staging slot. The slot, -1 = nobody followed
// (process the frame instead), -2 = no buffer.
JNIEXPORT jint JNICALL Java_al_moveit_motion_NpuPose_nativePrepareBuffer(JNIEnv* env, jclass, jlong handle,
                                                                          jobject buffer, jint width, jint height,
                                                                          jint rowStride, jlong timestampNs) {
    const uint8_t* pixels = (const uint8_t*)env->GetDirectBufferAddress(buffer);
    if (!pixels) return -2;
    return Get(handle)->Prepare(tundu::Rgba{pixels, (int)width, (int)height, (int)rowStride}, timestampNs);
}

// That slot through the NPU: -1 = failed (nativeError), else the number of people still found (their poses in out).
JNIEXPORT jint JNICALL Java_al_moveit_motion_NpuPose_nativeRunJob(JNIEnv* env, jclass, jlong handle, jint slot,
                                                                   jfloatArray out) {
    Frame frame;
    return Get(handle)->RunJob(slot, &frame) ? Pack(env, frame, out) : -1;
}

JNIEXPORT jboolean JNICALL Java_al_moveit_motion_NpuPose_nativeTracking(JNIEnv*, jclass, jlong handle) {
    return Get(handle)->Tracking() ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT void JNICALL Java_al_moveit_motion_NpuPose_nativeReset(JNIEnv*, jclass, jlong handle) {
    Get(handle)->Reset();
}

JNIEXPORT void JNICALL Java_al_moveit_motion_NpuPose_nativeDestroy(JNIEnv*, jclass, jlong handle) {
    delete Get(handle);
}

}  // extern "C"
