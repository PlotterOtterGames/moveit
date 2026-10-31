package al.moveit.motion;

import android.content.Context;
import android.graphics.Bitmap;
import android.os.SystemClock;
import android.util.Log;

import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.core.Delegate;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarker;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarkerResult;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

/**
 * The pose AI, one frame at a time, synchronously, on the camera analysis thread. The camera keeps only its latest
 * frame while we're busy, so every result is for the newest possible frame (lowest latency), and there are no
 * callbacks that could get lost. Where it runs:
 * <ul>
 * <li>A Rockchip NPU first (the TV boxes: the GPU stays free for the games; {@link NpuPose}), when the APK has the
 *     models built for this chip and the NPU runs them.</li>
 * <li>Otherwise MediaPipe Pose Landmarker in VIDEO mode: the GPU delegate, the CPU if the GPU can't be used or keeps
 *     failing.</li>
 * <li>Auto model: starts with "full" (more accurate) and switches to "lite" once if full is too slow on this device.</li>
 * </ul>
 */
final class PoseEngine {
    private static final String TAG = "MoveItMotion";
    private static final float SLOW_MS = 40f;      // MediaPipe: the full model couldn't keep ~25 fps
    // NPU, while following someone: the full model keeps the NPU busy more than ~70 % of a 30 fps frame. Heat, not speed:
    // on the RK3566 full took ~29 ms (it kept up) and lite ~19; switching to lite took the NPU load 64 → 34 % and the
    // SoC 77 → 75 °C within minutes (2026-10-31). A faster NPU (RK3576) keeps full.
    private static final float NPU_SLOW_MS = 24f;
    /** Chips with RKNN builds of the models in the APK (pose_detector.<chip>.rknn, pose_landmarks_<model>.<chip>.rknn). */
    private static final String[] NPU_CHIPS = {"rk3566"};

    private final Context context;
    private boolean autoModel;

    private PoseLandmarker landmarker;
    private NpuPose npu;
    private volatile String model;
    private volatile String delegate = "";
    private volatile String detail = "";
    private long lastTimestamp = -1;
    private float slowAverage = -1f;
    private int frames;
    private int gpuErrors, npuErrors;

    /** Cleared when the GPU fails at runtime: the next open() uses the CPU. */
    volatile boolean gpuAllowed = true;
    /** Cleared when the NPU can't run the models or keeps failing: the next open() uses MediaPipe. */
    volatile boolean npuAllowed = true;

    PoseEngine(Context context, String model) {
        this.context = context.getApplicationContext();
        this.autoModel = "auto".equals(model);
        this.model = autoModel ? "full" : model;
    }

    String model() { return model; }
    String delegate() { return delegate; }

    /** "auto", "full" or "lite": re-opens with that model if it changes. Call on the analysis thread. */
    void setModel(String requested) throws Exception {
        boolean auto = "auto".equals(requested);
        String next = auto ? "full" : requested;
        if (auto == autoModel && (auto || next.equals(model))) return;
        autoModel = auto;
        model = next;
        Log.i(TAG, "Pose model set to " + requested);
        open();
    }
    /** Where the time goes, for the stats log (NPU: the detector and the landmark model). */
    String detail() { return detail; }

    /** The packets' "src": android-rknn-full, android-mediapipe-gpu-lite, ... (docs/protocol.md). */
    String source() {
        return npu != null ? "android-rknn-" + model : "android-mediapipe-" + delegate.toLowerCase() + "-" + model;
    }

    /** Creates the engine. Call on the analysis thread. */
    void open() throws Exception {
        close();
        frames = 0;
        slowAverage = -1f;
        if (npuAllowed && openNpu()) {
            delegate = "NPU";
            Log.i(TAG, "Pose model " + model + " on the NPU");
            return;
        }
        try {
            if (!gpuAllowed) throw new IllegalStateException("GPU disabled");
            landmarker = create(Delegate.GPU);
            delegate = "GPU";
        } catch (Throwable gpuError) {
            if (gpuAllowed) Log.w(TAG, "GPU delegate unavailable, using CPU: " + gpuError);
            landmarker = create(Delegate.CPU);
            delegate = "CPU";
        }
        Log.i(TAG, "Pose model " + model + " on " + delegate);
    }

    private boolean openNpu() {
        if (!NpuPose.libraryLoaded()) {
            npuAllowed = false;
            return false;
        }
        for (String chip : NPU_CHIPS) {
            String detector = "pose_detector." + chip + ".rknn", landmarks = "pose_landmarks_" + model + "." + chip + ".rknn";
            if (!hasAsset(detector) || !hasAsset(landmarks)) continue;
            try {
                npu = new NpuPose(loadAsset(detector), loadAsset(landmarks));
                Log.i(TAG, "NPU engine for " + chip + ": " + npu.versions());
                return true;
            } catch (Throwable e) {
                Log.i(TAG, "NPU engine for " + chip + " can't run here: " + e.getMessage());
            }
        }
        npuAllowed = false;  // not this device: don't try again on every re-open
        return false;
    }

    private PoseLandmarker create(Delegate d) throws Exception {
        BaseOptions base = BaseOptions.builder()
                .setModelAssetBuffer(loadAsset("pose_landmarker_" + model + ".task"))
                .setDelegate(d)
                .build();
        PoseLandmarker.PoseLandmarkerOptions options = PoseLandmarker.PoseLandmarkerOptions.builder()
                .setBaseOptions(base)
                .setRunningMode(RunningMode.VIDEO)
                .setNumPoses(1)
                .setMinPoseDetectionConfidence(0.5f)
                .setMinPosePresenceConfidence(0.5f)
                .setMinTrackingConfidence(0.5f)
                .build();
        return PoseLandmarker.createFromOptions(context, options);
    }

    private boolean hasAsset(String name) {
        try {
            String[] names = context.getAssets().list("");
            return names != null && Arrays.asList(names).contains(name);
        } catch (Exception e) {
            return false;
        }
    }

    /** Model files are read into memory, so it doesn't matter whether the APK stores them compressed. */
    private ByteBuffer loadAsset(String name) throws Exception {
        try (InputStream in = context.getAssets().open(name)) {
            ByteArrayOutputStream out = new ByteArrayOutputStream(10 * 1024 * 1024);
            byte[] chunk = new byte[64 * 1024];
            int n;
            while ((n = in.read(chunk)) > 0) out.write(chunk, 0, n);
            byte[] bytes = out.toByteArray();
            ByteBuffer buffer = ByteBuffer.allocateDirect(bytes.length).order(ByteOrder.nativeOrder());
            buffer.put(bytes).rewind();
            return buffer;
        }
    }

    /**
     * Runs the pose AI on one frame (blocking, on the analysis thread) into pose. Returns false if there's no engine
     * or the frame failed. inferenceMs[0] receives the time it took. Re-opens itself on model switch or failures.
     * captureNs = the frame's capture time on a monotonic clock (the sensor timestamp): the landmarks are smoothed
     * (One Euro, MediaPipe's VIDEO mode, also on the NPU) with their dt from these timestamps, and the moment inference
     * starts varies.
     */
    boolean detect(Bitmap frame, float[] inferenceMs, long captureNs, Pose pose) throws Exception {
        long timestamp = Math.max(captureNs, lastTimestamp + 1_000_000L);  // must increase
        lastTimestamp = timestamp;
        if (npu != null) return detectNpu(frame, null, 0, 0, 0, inferenceMs, timestamp, pose);
        if (landmarker == null) return false;
        long start = SystemClock.elapsedRealtime();
        PoseLandmarkerResult result;
        try {
            MPImage image = new BitmapImageBuilder(frame).build();
            result = landmarker.detectForVideo(image, timestamp / 1_000_000L);
            gpuErrors = 0;
        } catch (Throwable e) {
            Log.w(TAG, "Pose frame failed on " + delegate + ": " + e);
            if ("GPU".equals(delegate) && ++gpuErrors >= 3) {
                Log.w(TAG, "GPU keeps failing, switching to CPU");
                gpuAllowed = false;
                open();
            }
            return false;
        }
        inferenceMs[0] = SystemClock.elapsedRealtime() - start;
        pose.set(result);
        autoModel(inferenceMs[0], SLOW_MS);
        return true;
    }

    /** True while the NPU runs the pose AI: it can then read the camera's buffer itself ({@link #detect(ByteBuffer,
     *  int, int, int, float[], long, Pose)}). */
    boolean usesNpu() {
        return npu != null;
    }

    /** The NPU straight from the camera's RGBA buffer (direct), without copying it into a Bitmap first. */
    boolean detect(ByteBuffer rgba, int width, int height, int rowStride, float[] inferenceMs, long captureNs, Pose pose)
            throws Exception {
        long timestamp = Math.max(captureNs, lastTimestamp + 1_000_000L);  // must increase
        lastTimestamp = timestamp;
        if (npu == null) return false;
        return detectNpu(null, rgba, width, height, rowStride, inferenceMs, timestamp, pose);
    }

    private boolean detectNpu(Bitmap frame, ByteBuffer rgba, int width, int height, int rowStride, float[] inferenceMs,
                              long timestampNs, Pose pose) throws Exception {
        boolean found;
        try {
            found = frame != null ? npu.process(frame, timestampNs) : npu.process(rgba, width, height, rowStride, timestampNs);
            npuErrors = 0;
        } catch (Throwable e) {
            Log.w(TAG, "Pose frame failed on the NPU: " + e.getMessage());
            if (++npuErrors >= 3) {
                Log.w(TAG, "NPU keeps failing, switching to MediaPipe");
                npuAllowed = false;
                open();
            }
            return false;
        }
        inferenceMs[0] = npu.totalMs;
        detail = (npu.detected ? "detector " + Math.round(npu.detectMs) + " ms, " : "")
                + "landmarks " + Math.round(npu.landmarkMs) + " ms";
        pose.set(npu, found);
        if (found && !npu.detected) autoModel(npu.totalMs, NPU_SLOW_MS);  // following someone: the steady cost
        return true;
    }

    /** Auto model: after a warm-up, full drops to lite once if it's slower than slowMs per frame on average. */
    private void autoModel(float ms, float slowMs) throws Exception {
        if (!autoModel || !"full".equals(model) || ++frames <= 60) return;  // skip warm-up frames
        slowAverage = slowAverage < 0 ? ms : 0.9f * slowAverage + 0.1f * ms;
        if (frames > 150 && slowAverage > slowMs) {
            Log.i(TAG, "Full model needs " + Math.round(slowAverage) + " ms per frame on " + delegate
                    + ", switching to lite");
            model = "lite";
            open();
        }
    }

    void close() {
        if (landmarker != null) {
            try { landmarker.close(); } catch (Throwable ignored) { }
            landmarker = null;
        }
        if (npu != null) {
            npu.close();
            npu = null;
        }
    }
}
