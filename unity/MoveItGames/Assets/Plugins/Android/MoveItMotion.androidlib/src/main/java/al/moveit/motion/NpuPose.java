package al.moveit.motion;

import android.graphics.Bitmap;
import android.util.Log;

import java.nio.ByteBuffer;

/**
 * The pose AI on a Rockchip NPU: MediaPipe's pose models converted to RKNN (tools/npu/convert.py), run by our native
 * engine (src/main/cpp/pose_npu.cpp, libtundupose.so) on Rockchip's runtime (librknnrt.so). Same landmarks, world
 * landmarks and smoothing as MediaPipe's VIDEO mode, without touching the GPU the games draw with. Follows up to
 * {@link #MAX_PEOPLE} people ({@link #setPeople}).
 */
final class NpuPose implements AutoCloseable {
    private static final String TAG = "MoveItMotion";
    static final int LANDMARKS = 33;
    static final int MAX_PEOPLE = 2;
    // pose_npu_jni.cpp: the frame (count, detected, detect ms, landmark ms, total ms, followed, crop ms, NPU ms, lost,
    // -), then per person: track, found, score, -, landmarks, world landmarks, crop box
    private static final int HEADER = 10;
    private static final int PERSON = 4 + LANDMARKS * 5 + LANDMARKS * 3 + 4;
    private static int libraries = -1;     // -1 not tried, 0 failed, 1 loaded

    private long handle;
    private final float[] out = new float[HEADER + MAX_PEOPLE * PERSON];

    /** The last frame: the people the landmark model ran on ({@link #count}), each found or lost in it. */
    int count;
    final int[] track = new int[MAX_PEOPLE];
    final boolean[] found = new boolean[MAX_PEOPLE];
    final float[] score = new float[MAX_PEOPLE];
    /** x, y, z, visibility, presence per landmark (x, y 0..1 of the picture), and world (metres), per person. */
    final float[][] landmarks = new float[MAX_PEOPLE][LANDMARKS * 5];
    final float[][] world = new float[MAX_PEOPLE][LANDMARKS * 3];
    /** landmarkMs: the landmark model's part (with the crops for process(), without them for run()); cropMs: the
     *  crops; npuMs: the NPU's own time in the runs. followed: people followed after the frame; lost: people lost in it. */
    float detectMs, landmarkMs, totalMs, cropMs, npuMs;
    int followed, lost;
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
     * How many people to follow (1 or 2), the landmark model's runs per second shared by them while two are followed (0
     * = each every frame; 30 = they take turns), and twin: one person's landmarks run twice (the second result dropped),
     * to measure a second person's cost with one (PerfLab twin=1). Fewer than followed: track keep (an id) stays
     * followed. bench: maxPeople fixed crops run as if they were people (never found): their cost with nobody in front
     * of the camera (PerfLab npubench=1). Not while a pipelined frame runs.
     */
    void setPeople(int maxPeople, float runsPerSecond, boolean twin, int keep, boolean bench) {
        if (handle != 0) nativeSetPeople(handle, maxPeople, runsPerSecond, twin, keep, bench);
    }

    /**
     * One camera frame (RGBA, read in place). Returns true when someone is there (the fields hold the poses), false when
     * nobody is. Throws when the NPU failed. timestampNs = the capture time on a monotonic clock (smoothing).
     */
    boolean process(Bitmap frame, long timestampNs) {
        return unpack(nativeProcess(handle, frame, timestampNs, out));
    }

    /** The same, straight from the camera's RGBA buffer (direct): no copy into a Bitmap first. */
    boolean process(ByteBuffer rgba, int width, int height, int rowStride, long timestampNs) {
        return unpack(nativeProcessBuffer(handle, rgba, width, height, rowStride, timestampNs, out));
    }

    /**
     * Pipelined tracking (pose_npu.h), the camera thread's half: this frame's crops for the landmark model, made with the
     * newest crop boxes, while the NPU may still be running the frame before. Returns the slot to {@link #run(int)}, or
     * -1 while nobody is followed (then {@link #process} the frame: the person detector). Doesn't touch the NPU.
     */
    int prepare(ByteBuffer rgba, int width, int height, int rowStride, long timestampNs) {
        return nativePrepareBuffer(handle, rgba, width, height, rowStride, timestampNs);
    }

    /** The other half, on the NPU's own thread: those crops through the landmark model, smoothed. True when someone is
     *  still there (the fields hold the poses). One run at a time, and no process() meanwhile. Throws when it failed. */
    boolean run(int slot) {
        return unpack(nativeRunJob(handle, slot, out));
    }

    /** Someone is being followed: the next frame can be prepared. */
    boolean tracking() {
        return handle != 0 && nativeTracking(handle);
    }

    private boolean unpack(int people) {
        if (people < 0) throw new IllegalStateException(nativeError(handle));
        count = Math.min(MAX_PEOPLE, (int) out[0]);
        detected = out[1] > 0;
        detectMs = out[2];
        landmarkMs = out[3];
        totalMs = out[4];
        followed = (int) out[5];
        cropMs = out[6];
        npuMs = out[7];
        lost = (int) out[8];
        for (int i = 0; i < count; i++) {
            int o = HEADER + i * PERSON;
            track[i] = (int) out[o];
            found[i] = out[o + 1] > 0;
            score[i] = out[o + 2];
            if (found[i]) {
                System.arraycopy(out, o + 4, landmarks[i], 0, LANDMARKS * 5);
                System.arraycopy(out, o + 4 + LANDMARKS * 5, world[i], 0, LANDMARKS * 3);
            }
        }
        return people > 0;
    }

    /** Forget everyone followed: the next frame runs the person detector. */
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
    private static native void nativeSetPeople(long handle, int maxPeople, float runsPerSecond, boolean twin, int keep,
                                               boolean bench);
    private static native int nativeProcess(long handle, Bitmap frame, long timestampNs, float[] out);
    private static native int nativeProcessBuffer(long handle, ByteBuffer rgba, int width, int height, int rowStride,
                                                  long timestampNs, float[] out);
    private static native int nativePrepareBuffer(long handle, ByteBuffer rgba, int width, int height, int rowStride,
                                                  long timestampNs);
    private static native int nativeRunJob(long handle, int slot, float[] out);
    private static native boolean nativeTracking(long handle);
    private static native void nativeReset(long handle);
    private static native void nativeDestroy(long handle);
}
