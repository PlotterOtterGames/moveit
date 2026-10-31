// JNI for al.moveit.motion.CameraControls: the USB camera's own controls (UVC gain, gamma, saturation, exposure
// priority) through V4L2 on its /dev/video node, which Android's camera API doesn't reach on the boxes. Works where
// the node is open to the app (a box maker's firmware can allow it); otherwise CameraTuner falls back to root.
#include <fcntl.h>
#include <jni.h>
#include <linux/videodev2.h>
#include <sys/ioctl.h>
#include <unistd.h>

#include <cerrno>

extern "C" {

// The node's file descriptor, or -errno.
JNIEXPORT jint JNICALL Java_al_moveit_motion_CameraControls_nativeOpen(JNIEnv* env, jclass, jstring path) {
    const char* p = env->GetStringUTFChars(path, nullptr);
    const int fd = open(p, O_RDWR | O_CLOEXEC);
    const int error = errno;
    env->ReleaseStringUTFChars(path, p);
    return fd >= 0 ? fd : -error;
}

// {min, max, default, current} of a control, or null if the camera doesn't have it.
JNIEXPORT jintArray JNICALL Java_al_moveit_motion_CameraControls_nativeQuery(JNIEnv* env, jclass, jint fd, jint id) {
    v4l2_queryctrl query{};
    query.id = (uint32_t)id;
    if (ioctl(fd, VIDIOC_QUERYCTRL, &query) < 0 || (query.flags & V4L2_CTRL_FLAG_DISABLED)) return nullptr;
    v4l2_control control{};
    control.id = (uint32_t)id;
    const jint value = ioctl(fd, VIDIOC_G_CTRL, &control) == 0 ? control.value : query.default_value;
    const jint values[4] = {query.minimum, query.maximum, query.default_value, value};
    jintArray result = env->NewIntArray(4);
    env->SetIntArrayRegion(result, 0, 4, values);
    return result;
}

// 0, or -errno.
JNIEXPORT jint JNICALL Java_al_moveit_motion_CameraControls_nativeSet(JNIEnv*, jclass, jint fd, jint id, jint value) {
    v4l2_control control{};
    control.id = (uint32_t)id;
    control.value = value;
    return ioctl(fd, VIDIOC_S_CTRL, &control) == 0 ? 0 : -errno;
}

JNIEXPORT void JNICALL Java_al_moveit_motion_CameraControls_nativeClose(JNIEnv*, jclass, jint fd) { close(fd); }

}  // extern "C"
