package al.moveit.motion;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.Matrix;
import android.os.SystemClock;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.camera.core.ImageProxy;
import androidx.lifecycle.Lifecycle;
import androidx.lifecycle.LifecycleOwner;
import androidx.lifecycle.LifecycleRegistry;

import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarkerResult;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * The box's motion service: camera → pose AI → skeleton packets to the game on 127.0.0.1 (Motion Protocol v1.1).
 * Called from Unity (Assets/MoveSDK/Runtime/Android/AndroidMotionService.cs):
 * {@code start(activity, port, model)}, {@code pause()}, {@code getStatus()}, and {@code setVideo(port, width, on)} for
 * the camera preview stream (Pikturo's camera mode).
 */
public final class MotionService implements LifecycleOwner {
    private static final String TAG = "MoveItMotion";
    private static MotionService instance;

    private final Activity activity;
    private final LifecycleRegistry lifecycle;
    private final ExecutorService analysisThread = Executors.newSingleThreadExecutor();
    private final CameraSource camera = new CameraSource();
    private final VideoStreamer video = new VideoStreamer();
    private long videoFrame;
    private final PoseEngine engine;
    private PacketWriter writer;

    private volatile String state = "starting";
    private volatile String error = "";
    private volatile String cameraName = "";
    private volatile float fps, inferenceMs, cameraFps;
    private long lastResultAt, lastFrameAt, lastStatsLogAt;
    private final float[] inference = new float[1];
    private Bitmap reusedFrame;  // analysis thread only
    private volatile boolean snapshotRequested;

    private MotionService(Activity activity, int port, String model) {
        this.activity = activity;
        this.lifecycle = new LifecycleRegistry(this);
        this.engine = new PoseEngine(activity, model);
        try {
            writer = new PacketWriter(port);
        } catch (Exception e) {
            fail("udp: " + e.getMessage());
        }
        // The manifest asks the TV for its game mode (ALLM); this says whether this box + TV can do it at all.
        if (android.os.Build.VERSION.SDK_INT >= 30 && activity.getDisplay() != null)
            Log.i(TAG, "TV game mode (ALLM) supported: " + activity.getDisplay().isMinimalPostProcessingSupported());
    }

    // ---------------------------------------------------------------- API for Unity

    /** Starts (or resumes) the camera and pose AI. model: "auto", "full" or "lite". */
    public static synchronized void start(Activity activity, int port, String model) {
        if (activity.checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            Log.w(TAG, "Camera permission not granted");
            if (instance != null) instance.state = "no-permission";
            else statusWithoutInstance = "no-permission";
            return;
        }
        statusWithoutInstance = "stopped";
        if (instance == null) {
            instance = new MotionService(activity, port, model);
            instance.boot();
        } else {
            instance.resume();
        }
    }

    /** Stops the camera while the app is in the background (the engine stays loaded for a quick resume). */
    public static synchronized void pause() {
        if (instance != null) instance.activity.runOnUiThread(() -> {
            instance.lifecycle.handleLifecycleEvent(Lifecycle.Event.ON_STOP);  // camera off
            instance.state = "paused";
        });
    }

    /** Small JSON status for the game's Settings / camera test screens. */
    public static String getStatus() {
        MotionService s = instance;
        if (s == null) return "{\"state\":\"" + statusWithoutInstance + "\"}";
        return "{\"state\":\"" + s.state + "\",\"camera\":\"" + s.cameraName + "\",\"delegate\":\"" + s.engine.delegate()
                + "\",\"model\":\"" + s.engine.model() + "\",\"fps\":" + round(s.fps) + ",\"inf\":" + round(s.inferenceMs)
                + ",\"cam\":" + round(s.cameraFps) + ",\"error\":\"" + s.error.replace("\"", "'") + "\"}";
    }

    private static volatile String statusWithoutInstance = "stopped";

    /**
     * Camera preview stream: while on, small JPEG frames of what the camera sees go to the game on 127.0.0.1:port
     * (never off the device). The game repeats on = true about once a second; it stops by itself 3 s after the last.
     */
    public static void setVideo(int port, int width, boolean on) {
        MotionService s = instance;
        if (s != null) s.video.request(port, width, on);
    }

    /**
     * Saves the next camera frame (exactly what the AI sees) as camera-snapshot.jpg in the app's files folder, for
     * checking camera framing and lighting. Only on request, overwritten each time, never sent anywhere.
     * Returns the file path.
     */
    public static String requestSnapshot() {
        MotionService s = instance;
        if (s == null) return "";
        s.snapshotRequested = true;
        return new java.io.File(s.activity.getExternalFilesDir(null), "camera-snapshot.jpg").getAbsolutePath();
    }

    @NonNull
    @Override
    public Lifecycle getLifecycle() {
        return lifecycle;
    }

    // ---------------------------------------------------------------- internals

    private void boot() {
        analysisThread.execute(() -> {
            try {
                engine.open();
            } catch (Throwable e) {
                fail("model: " + e.getMessage());
                return;
            }
            activity.runOnUiThread(() -> {
                lifecycle.handleLifecycleEvent(Lifecycle.Event.ON_CREATE);
                camera.start(activity, this, analysisThread, this::analyze, new CameraSource.Callback() {
                    @Override
                    public void onStarted(String description) {
                        cameraName = description;
                        state = "running";
                        Log.i(TAG, "Camera " + description + " started");
                    }

                    @Override
                    public void onError(String message) {
                        fail(message);
                    }
                });
                lifecycle.handleLifecycleEvent(Lifecycle.Event.ON_RESUME);  // camera on
            });
        });
    }

    private void resume() {
        activity.runOnUiThread(() -> {
            lifecycle.handleLifecycleEvent(Lifecycle.Event.ON_RESUME);
            if (state.equals("paused")) state = "running";
        });
    }

    /** CameraX analyzer, on the analysis thread. */
    private void analyze(@NonNull ImageProxy image) {
        try {
            long now = SystemClock.elapsedRealtime();
            if (lastFrameAt > 0) cameraFps = smooth(cameraFps, 1000f / Math.max(1, now - lastFrameAt));
            lastFrameAt = now;

            if (now - lastStatsLogAt > 5000) {
                lastStatsLogAt = now;
                Log.i(TAG, "stats: camera " + round(cameraFps) + " fps, AI " + round(fps) + " fps, "
                        + round(inferenceMs) + " ms, " + engine.delegate() + " " + engine.model() + ", " + state);
            }
            Bitmap frame = toBitmap(image);
            int rotation = image.getImageInfo().getRotationDegrees();
            if (rotation != 0) {
                Matrix m = new Matrix();
                m.postRotate(rotation);
                frame = Bitmap.createBitmap(frame, 0, 0, frame.getWidth(), frame.getHeight(), m, true);
            }
            if (snapshotRequested) {
                snapshotRequested = false;
                java.io.File file = new java.io.File(activity.getExternalFilesDir(null), "camera-snapshot.jpg");
                try (java.io.FileOutputStream out = new java.io.FileOutputStream(file)) {
                    frame.compress(Bitmap.CompressFormat.JPEG, 85, out);
                    Log.i(TAG, "Camera snapshot saved: " + file);
                }
            }
            long captureWall = captureWallTime(image);
            video.offer(frame, captureWall, videoFrame++);  // copies the frame and returns; encoded on its own thread
            PoseLandmarkerResult result = engine.detect(frame, inference);  // blocking: the camera keeps only the newest frame meanwhile
            if (result != null) onPoseResult(result, captureWall, frame.getWidth(), frame.getHeight(), inference[0]);
        } catch (Throwable e) {
            Log.e(TAG, "Frame failed", e);
        } finally {
            image.close();
        }
    }

    /**
     * Copies the RGBA frame into one reused Bitmap instead of a new 1.2 MB one per frame (ImageProxy.toBitmap), so the
     * box's garbage collector has ~36 MB/s less to clean up. Safe because detection is synchronous: the Bitmap is free
     * again before the next frame. Rows with padding fall back to CameraX's own copy.
     */
    private Bitmap toBitmap(ImageProxy image) {
        ImageProxy.PlaneProxy plane = image.getPlanes()[0];
        int width = image.getWidth(), height = image.getHeight();
        if (plane.getPixelStride() != 4 || plane.getRowStride() != width * 4) return image.toBitmap();
        if (reusedFrame == null || reusedFrame.getWidth() != width || reusedFrame.getHeight() != height)
            reusedFrame = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
        java.nio.ByteBuffer pixels = plane.getBuffer();
        pixels.rewind();
        reusedFrame.copyPixelsFromBuffer(pixels);
        return reusedFrame;
    }

    /** Converts the sensor timestamp to Unix ms, so the game can measure capture-to-screen latency. */
    private static long captureWallTime(ImageProxy image) {
        long ageMs = (SystemClock.elapsedRealtimeNanos() - image.getImageInfo().getTimestamp()) / 1_000_000L;
        long wall = System.currentTimeMillis();
        return (ageMs >= 0 && ageMs < 1000) ? wall - ageMs : wall;  // some devices use another clock: ignore then
    }

    private void onPoseResult(PoseLandmarkerResult result, long captureWallMs, int width, int height, float inf) {
        long now = SystemClock.elapsedRealtime();
        if (lastResultAt > 0) fps = smooth(fps, 1000f / Math.max(1, now - lastResultAt));
        lastResultAt = now;
        inferenceMs = smooth(inferenceMs, inf);
        if (writer != null) {
            String src = "android-mediapipe-" + engine.delegate().toLowerCase() + "-" + engine.model();
            writer.send(result, src, engine.model(), captureWallMs, width, height, fps, inferenceMs, cameraFps);
        }
    }

    private void fail(String message) {
        Log.e(TAG, message);
        error = message;
        state = message.startsWith("no-camera") ? "no-camera" : "error";
    }

    private static float smooth(float current, float sample) {
        return current <= 0 ? sample : 0.9f * current + 0.1f * sample;
    }

    private static String round(float v) {
        return String.valueOf(Math.round(v * 10f) / 10f);
    }
}
