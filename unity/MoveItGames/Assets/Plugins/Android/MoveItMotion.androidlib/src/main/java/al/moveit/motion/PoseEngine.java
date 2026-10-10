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

/**
 * MediaPipe Pose Landmarker in VIDEO mode: one frame at a time, synchronously, on the camera analysis thread.
 * The camera keeps only its latest frame while we're busy, so every result is for the newest possible frame
 * (lowest latency), and there are no callbacks that could get lost.
 * <ul>
 * <li>GPU delegate first, CPU if the GPU can't be used or keeps failing.</li>
 * <li>Auto model: starts with "full" (more accurate) and switches to "lite" once if full needs more than
 *     40 ms per frame on this device (it couldn't keep ~25 fps).</li>
 * </ul>
 */
final class PoseEngine {
    private static final String TAG = "MoveItMotion";
    private static final float SLOW_MS = 40f;

    private final Context context;
    private final boolean autoModel;

    private PoseLandmarker landmarker;
    private volatile String model;
    private volatile String delegate = "";
    private long lastTimestamp = -1;
    private float slowAverage = -1f;
    private int frames;
    private int gpuErrors;

    /** Cleared when the GPU fails at runtime: the next open() uses the CPU. */
    volatile boolean gpuAllowed = true;

    PoseEngine(Context context, String model) {
        this.context = context.getApplicationContext();
        this.autoModel = "auto".equals(model);
        this.model = autoModel ? "full" : model;
    }

    String model() { return model; }
    String delegate() { return delegate; }

    /** Creates the landmarker. Call on the analysis thread. */
    void open() throws Exception {
        close();
        try {
            if (!gpuAllowed) throw new IllegalStateException("GPU disabled");
            landmarker = create(Delegate.GPU);
            delegate = "GPU";
        } catch (Throwable gpuError) {
            if (gpuAllowed) Log.w(TAG, "GPU delegate unavailable, using CPU: " + gpuError);
            landmarker = create(Delegate.CPU);
            delegate = "CPU";
        }
        frames = 0;
        slowAverage = -1f;
        Log.i(TAG, "Pose model " + model + " on " + delegate);
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
     * Runs the pose model on one frame (blocking, on the analysis thread). Returns null if there's no engine or the
     * frame failed. inferenceMs[0] receives the time it took. Re-opens itself on model switch or GPU failure.
     */
    PoseLandmarkerResult detect(Bitmap frame, float[] inferenceMs) throws Exception {
        if (landmarker == null) return null;
        long timestamp = Math.max(SystemClock.uptimeMillis(), lastTimestamp + 1);  // must increase
        lastTimestamp = timestamp;
        long start = SystemClock.elapsedRealtime();
        PoseLandmarkerResult result;
        try {
            MPImage image = new BitmapImageBuilder(frame).build();
            result = landmarker.detectForVideo(image, timestamp);
            gpuErrors = 0;
        } catch (Throwable e) {
            Log.w(TAG, "Pose frame failed on " + delegate + ": " + e);
            if ("GPU".equals(delegate) && ++gpuErrors >= 3) {
                Log.w(TAG, "GPU keeps failing, switching to CPU");
                gpuAllowed = false;
                open();
            }
            return null;
        }
        inferenceMs[0] = SystemClock.elapsedRealtime() - start;

        if (autoModel && "full".equals(model) && ++frames > 60) {  // skip warm-up frames
            slowAverage = slowAverage < 0 ? inferenceMs[0] : 0.9f * slowAverage + 0.1f * inferenceMs[0];
            if (frames > 150 && slowAverage > SLOW_MS) {
                Log.i(TAG, "Full model needs " + Math.round(slowAverage) + " ms per frame, switching to lite");
                model = "lite";
                open();
            }
        }
        return result;
    }

    void close() {
        if (landmarker != null) {
            try { landmarker.close(); } catch (Throwable ignored) { }
            landmarker = null;
        }
    }
}
