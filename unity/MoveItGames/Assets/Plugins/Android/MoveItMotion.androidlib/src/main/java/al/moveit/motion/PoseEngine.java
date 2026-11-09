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
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

/**
 * The pose AI on the camera analysis thread. The camera keeps only its latest frame while we're busy, so every result
 * is for the newest possible frame (lowest latency). Where it runs:
 * <ul>
 * <li>A Rockchip NPU first (the TV boxes: the GPU stays free for the games; {@link NpuPose}), when the APK has the
 *     models built for this chip and the NPU runs them. While someone is followed it's pipelined ({@link #submit}):
 *     this thread makes a frame's crop while the NPU still runs the frame before on its own thread, which sends each
 *     pose as soon as it's done. The person detector (nobody followed) runs here, synchronously.</li>
 * <li>Otherwise MediaPipe Pose Landmarker in VIDEO mode, synchronously: the GPU delegate, the CPU if the GPU can't be
 *     used or keeps failing.</li>
 * <li>Auto model: starts with "full" (more accurate) and switches to "lite" once if full is too slow on this device.</li>
 * </ul>
 */
final class PoseEngine {
    private static final String TAG = "MoveItMotion";
    private static final float SLOW_MS = 40f;      // MediaPipe: the full model couldn't keep ~25 fps
    // NPU, while following someone: the NPU's part of a frame (its run, outputs, smoothing; the crop runs beside it)
    // can't keep up with a 30 fps camera any more. On the RK3566 full takes ~19 ms idle (2026-11-01: the crop pipelined,
    // the output reshapes out of the model, the heatmap read as float16; before: ~29 ms serial in a game, so lite was
    // kept for heat since 2026-10-31: NPU 64 % busy against 34 %, ~2 °C). The user chose full (The Link's model).
    private static final float NPU_SLOW_MS = 31f;
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
    // people to follow (1 or 2), the landmark model's shared budget on the NPU (runs per second, 0 = everyone every
    // frame), and the twin measurement (setPeople)
    private int maxPeople = 1;
    private float runsPerSecond = 0f;
    private boolean twin, bench;
    private int mediaPipePoses = 1;   // what the MediaPipe landmarker was created with
    private int frames;
    private int gpuErrors, npuErrors;

    // The pipelined NPU (submit): its thread, the frame it's running, what it reports back
    private final ExecutorService npuThread = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "pose-npu");
        t.setDaemon(true);
        return t;
    });
    private Future<?> inFlight;                     // analysis thread
    private volatile boolean jobFailed;
    private volatile float lastRunMs;
    private final Pose pipelinedPose = new Pose();  // the NPU thread's

    /** A pipelined frame's pose, delivered on the NPU thread as soon as it's done. ms = its crop + its NPU part. */
    interface PipelinedResult {
        void onPose(Pose pose, float ms);
    }

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

    /**
     * Up to how many people to follow (1 or 2; two-player games ask for 2), the NPU's landmark runs per second shared by
     * them (0 = each one every frame; 30 = they take turns, each ~15 times a second, the same NPU load as one person),
     * and twin (one person's landmarks run twice: a second person's cost, PerfLab). Fewer than followed: track keep
     * (the NPU's id) stays followed. bench: the NPU runs that many fixed crops as if they were people (their cost with
     * nobody there). Call on the analysis thread.
     */
    void setPeople(int max, float rate, boolean twin, int keep, boolean bench) throws Exception {
        max = Math.max(1, Math.min(NpuPose.MAX_PEOPLE, max));
        if (max == maxPeople && rate == runsPerSecond && twin == this.twin && bench == this.bench) return;
        maxPeople = max;
        runsPerSecond = rate;
        this.twin = twin;
        this.bench = bench;
        Log.i(TAG, "People: up to " + max + (max > 1 ? ", " + (rate > 0 ? Math.round(rate) + " landmark runs/s" : "each every frame") : "")
                + (twin ? ", twin" : "") + (bench ? ", bench (fixed crops, nobody there)" : ""));
        if (npu != null) {
            finish();   // not while a pipelined frame runs
            if (npu != null) npu.setPeople(maxPeople, runsPerSecond, this.twin, keep, this.bench);
        } else if (landmarker != null && max != mediaPipePoses) {
            open();     // MediaPipe takes the number of poses when it's created
        }
    }

    int maxPeople() { return maxPeople; }

    /** The NPU runs fixed crops as if they were people (measuring): the service mustn't idle for lack of anyone. */
    boolean bench() { return bench && npu != null; }

    /** Creates the engine. Call on the analysis thread. */
    void open() throws Exception {
        close();
        frames = 0;
        slowAverage = -1f;
        if (npuAllowed && openNpu()) {
            delegate = "NPU";
            npu.setPeople(maxPeople, runsPerSecond, twin, 0, bench);
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
        mediaPipePoses = maxPeople;
        BaseOptions base = BaseOptions.builder()
                .setModelAssetBuffer(loadAsset("pose_landmarker_" + model + ".task"))
                .setDelegate(d)
                .build();
        PoseLandmarker.PoseLandmarkerOptions options = PoseLandmarker.PoseLandmarkerOptions.builder()
                .setBaseOptions(base)
                .setRunningMode(RunningMode.VIDEO)
                .setNumPoses(maxPeople)
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
        finish();  // a pipelined frame still on the NPU first
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
        finish();  // a pipelined frame still on the NPU first
        long timestamp = Math.max(captureNs, lastTimestamp + 1_000_000L);  // must increase
        lastTimestamp = timestamp;
        if (npu == null) return false;
        return detectNpu(null, rgba, width, height, rowStride, inferenceMs, timestamp, pose);
    }

    /**
     * The NPU while someone is followed, pipelined: this frame's crop is made here (the camera's buffer is read now,
     * so the frame can be closed when this returns), then, once the NPU is done with the frame before, it runs this one
     * on its own thread and hands the pose to result there. The crop uses the newest crop box: the frame before's when
     * the NPU finished it in time (MediaPipe's own chain), else the one before that (checked on a recorded clip: no
     * loss of accuracy, docs/hardware.md). Returns false when this frame needs {@link #detect} instead: nobody
     * followed (the person detector), the NPU isn't used, or the frame before lost the person.
     */
    boolean submit(ByteBuffer rgba, int width, int height, int rowStride, long captureNs, PipelinedResult result)
            throws Exception {
        NpuPose engine = npu;
        if (engine == null || (inFlight == null && !engine.tracking())) return false;
        long timestamp = Math.max(captureNs, lastTimestamp + 1_000_000L);  // must increase
        int slot = engine.prepare(rgba, width, height, rowStride, timestamp);   // beside the NPU's run of the frame before
        finish();                                                               // that frame's pose is out
        if (npu != engine || slot < 0 || !engine.tracking()) return false;      // re-opened or lost: the detector now
        lastTimestamp = timestamp;
        inFlight = npuThread.submit(() -> run(engine, slot, result));
        return true;
    }

    /** The NPU thread's part of a pipelined frame. */
    private void run(NpuPose engine, int slot, PipelinedResult result) {
        boolean found;
        try {
            found = engine.run(slot);
        } catch (Throwable e) {
            Log.w(TAG, "Pose frame failed on the NPU: " + e.getMessage());
            jobFailed = true;
            return;
        }
        lastRunMs = engine.landmarkMs;
        detail = "crop " + Math.round(engine.cropMs) + " ms, landmarks " + Math.round(engine.landmarkMs) + " ms (NPU "
                + Math.round(engine.npuMs) + ", pipelined)" + people(engine);
        pipelinedPose.set(engine);
        result.onPose(pipelinedPose, engine.cropMs + engine.landmarkMs);
    }

    /** Waits for the pipelined frame on the NPU, if any (its pose is sent by then); its errors and the auto model are
     *  handled here, on the analysis thread. */
    private void finish() throws Exception {
        Future<?> job = inFlight;
        if (job == null) return;
        inFlight = null;
        try {
            job.get();
        } catch (Exception e) {
            jobFailed = true;
        }
        if (jobFailed) {
            jobFailed = false;
            if (++npuErrors >= 3) {
                Log.w(TAG, "NPU keeps failing, switching to MediaPipe");
                npuAllowed = false;
                open();
            }
            return;
        }
        npuErrors = 0;
        if (npu != null && npu.tracking()) autoModel(lastRunMs, NPU_SLOW_MS);
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
                + "landmarks " + Math.round(npu.landmarkMs) + " ms (crop " + Math.round(npu.cropMs) + ", NPU "
                + Math.round(npu.npuMs) + ")" + people(npu);
        pose.set(npu);
        // following someone: the NPU's part of a frame, as in the pipelined runs (the crop runs beside it there)
        if (found && !npu.detected) autoModel(npu.landmarkMs - npu.cropMs, NPU_SLOW_MS);
        return true;
    }

    /** The stats line's people part, when two may be followed: how many are, and this frame's landmark runs. */
    private String people(NpuPose engine) {
        if (maxPeople < 2 && !twin && !bench) return "";
        return ", people " + engine.followed + "/" + maxPeople + " (" + engine.count + " run"
                + (engine.count == 1 ? "" : "s") + (runsPerSecond > 0 ? ", budget " + Math.round(runsPerSecond) + "/s" : "")
                + (twin ? ", twin" : "") + (bench ? ", bench" : "") + ")";
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
        Future<?> job = inFlight;  // the NPU thread may still be running a frame on the engine about to go
        inFlight = null;
        if (job != null) {
            try { job.get(); } catch (Throwable ignored) { }
        }
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
