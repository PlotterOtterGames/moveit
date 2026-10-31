// JNI for al.moveit.motion.NpuPose: the camera frame (an RGBA Bitmap, read in place) in, the pose out as floats.
#include <android/bitmap.h>
#include <jni.h>

#include <string>

#include "pose_npu.h"

using tundu::Pose;
using tundu::PoseNpu;

namespace {

std::string g_createError;

// Layout of the float array handed back (NpuPose.java reads the same offsets).
constexpr int kHeader = 6;  // score, detected, detect ms, landmark ms, total ms, found
constexpr int kOutSize = kHeader + tundu::kPoseLandmarks * 5 + tundu::kPoseLandmarks * 3 + 4;

PoseNpu* Get(jlong handle) { return reinterpret_cast<PoseNpu*>(handle); }

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

jint Pack(JNIEnv* env, const Pose& pose, jfloatArray out);

// -1 = failed (nativeError), 0 = nobody, 1 = a pose in out.
JNIEXPORT jint JNICALL Java_al_moveit_motion_NpuPose_nativeProcess(JNIEnv* env, jclass, jlong handle, jobject bitmap,
                                                                    jlong timestampNs, jfloatArray out) {
    AndroidBitmapInfo info;
    if (AndroidBitmap_getInfo(env, bitmap, &info) != ANDROID_BITMAP_RESULT_SUCCESS ||
        info.format != ANDROID_BITMAP_FORMAT_RGBA_8888)
        return -1;
    void* pixels = nullptr;
    if (AndroidBitmap_lockPixels(env, bitmap, &pixels) != ANDROID_BITMAP_RESULT_SUCCESS) return -1;
    Pose pose;
    const bool ok = Get(handle)->Process(
        tundu::Rgba{(const uint8_t*)pixels, (int)info.width, (int)info.height, (int)info.stride}, timestampNs, &pose);
    AndroidBitmap_unlockPixels(env, bitmap);
    return ok ? Pack(env, pose, out) : -1;
}

// The same from the camera's own RGBA buffer (a direct ByteBuffer): no copy into a Bitmap first.
JNIEXPORT jint JNICALL Java_al_moveit_motion_NpuPose_nativeProcessBuffer(JNIEnv* env, jclass, jlong handle,
                                                                          jobject buffer, jint width, jint height,
                                                                          jint rowStride, jlong timestampNs,
                                                                          jfloatArray out) {
    const uint8_t* pixels = (const uint8_t*)env->GetDirectBufferAddress(buffer);
    if (!pixels) return -1;
    Pose pose;
    const bool ok = Get(handle)->Process(tundu::Rgba{pixels, (int)width, (int)height, (int)rowStride}, timestampNs, &pose);
    return ok ? Pack(env, pose, out) : -1;
}

jint Pack(JNIEnv* env, const Pose& pose, jfloatArray out) {
    float buf[kOutSize];
    buf[0] = pose.score;
    buf[1] = pose.detected ? 1.f : 0.f;
    buf[2] = pose.detectMs;
    buf[3] = pose.landmarkMs;
    buf[4] = pose.totalMs;
    buf[5] = pose.found ? 1.f : 0.f;
    float* p = buf + kHeader;
    for (int i = 0; i < tundu::kPoseLandmarks; i++)
        for (int k = 0; k < 5; k++) *p++ = pose.lm[i][k];
    for (int i = 0; i < tundu::kPoseLandmarks; i++)
        for (int k = 0; k < 3; k++) *p++ = pose.world[i][k];
    for (int k = 0; k < 4; k++) *p++ = pose.roi[k];
    env->SetFloatArrayRegion(out, 0, kOutSize, buf);
    return pose.found ? 1 : 0;
}

JNIEXPORT void JNICALL Java_al_moveit_motion_NpuPose_nativeReset(JNIEnv*, jclass, jlong handle) {
    Get(handle)->Reset();
}

JNIEXPORT void JNICALL Java_al_moveit_motion_NpuPose_nativeDestroy(JNIEnv*, jclass, jlong handle) {
    delete Get(handle);
}

}  // extern "C"
