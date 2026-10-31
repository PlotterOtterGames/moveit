package al.moveit.motion;

import android.graphics.Bitmap;
import android.util.Log;

import java.nio.ByteBuffer;

/**
 * The pose AI on a Rockchip NPU: MediaPipe's pose models converted to RKNN (tools/npu/convert.py), run by our native
 * engine (src/main/cpp/pose_npu.cpp, libtundupose.so) on Rockchip's runtime (librknnrt.so). Same landmarks, world
 * landmarks and smoothing as MediaPipe's VIDEO mode, without touching the GPU the games draw with.
 */
final class NpuPose implements AutoCloseable {
    private static final String TAG = "MoveItMotion";
    static final int LANDMARKS = 33;
    private static final int HEADER = 6;   // score, detected, detect ms, landmark ms, total ms, found (pose_npu_jni.cpp)
    private static int libraries = -1;     // -1 not tried, 0 failed, 1 loaded

    private long handle;
    private final float[] out = new float[HEADER + LANDMARKS * 5 + LANDMARKS * 3 + 4];

    /** The last frame: x, y, z, visibility, presence per landmark (x, y 0..1 of the picture), and world (metres). */
    final float[] landmarks = new float[LANDMARKS * 5];
    final float[] world = new float[LANDMARKS * 3];
    float score, detectMs, landmarkMs, totalMs;
    boolean detected;

    /** Loads libtundupose.so and Rockchip's runtime once; false on other chips' builds or a broken install. */
    static synchronized boolean libraryLoaded() {
        if (libraries < 0) {
            try {
                System.loadLibrary("rknnrt");
                System.loadLibrary("tundupose");
                libraries = 1;
            } catch (Throwable e) {
                Log.i(TAG, "NPU engine not available: " + e);
                libraries = 0;
            }
        }
        return libraries == 1;
    }

    /** The models' bytes (direct buffers). Throws when the NPU can't run them (another chip, a driver too old). */
    NpuPose(ByteBuffer detector, ByteBuffer landmarks) {
        handle = nativeCreate(detector, landmarks);
        if (handle == 0) throw new IllegalStateException(nativeCreateError());
    }

    String versions() {
        return handle != 0 ? nativeVersions(handle) : "";
    }

    /**
     * One camera frame (RGBA, read in place). Returns true when a person is there (the fields hold the pose), false
     * when nobody is. Throws when the NPU failed. timestampNs = the capture time on a monotonic clock (smoothing).
     */
    boolean process(Bitmap frame, long timestampNs) {
        return unpack(nativeProcess(handle, frame, timestampNs, out));
    }

    /** The same, straight from the camera's RGBA buffer (direct): no copy into a Bitmap first. */
    boolean process(ByteBuffer rgba, int width, int height, int rowStride, long timestampNs) {
        return unpack(nativeProcessBuffer(handle, rgba, width, height, rowStride, timestampNs, out));
    }

    private boolean unpack(int found) {
        if (found < 0) throw new IllegalStateException(nativeError(handle));
        score = out[0];
        detected = out[1] > 0;
        detectMs = out[2];
        landmarkMs = out[3];
        totalMs = out[4];
        if (found > 0) {
            System.arraycopy(out, HEADER, landmarks, 0, landmarks.length);
            System.arraycopy(out, HEADER + landmarks.length, world, 0, world.length);
        }
        return found > 0;
    }

    /** Forget the person being followed: the next frame runs the person detector. */
    void reset() {
        if (handle != 0) nativeReset(handle);
    }

    @Override
    public void close() {
        if (handle != 0) {
            nativeDestroy(handle);
            handle = 0;
        }
    }

    private static native long nativeCreate(ByteBuffer detector, ByteBuffer landmarks);
    private static native String nativeCreateError();
    private static native String nativeError(long handle);
    private static native String nativeVersions(long handle);
    private static native int nativeProcess(long handle, Bitmap frame, long timestampNs, float[] out);
    private static native int nativeProcessBuffer(long handle, ByteBuffer rgba, int width, int height, int rowStride,
                                                  long timestampNs, float[] out);
    private static native void nativeReset(long handle);
    private static native void nativeDestroy(long handle);
}
