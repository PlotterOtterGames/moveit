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

import java.nio.ByteBuffer;
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
    private final CameraTuner tuner = new CameraTuner();   // the USB camera's brightness in a dim room
    private final VideoStreamer video = new VideoStreamer();
    private long videoFrame;
    private final PoseEngine engine;
    private PacketWriter writer;

    private volatile String state = "starting";
    private volatile String error = "";
    private volatile String cameraName = "";
    private volatile float fps, inferenceMs, cameraFps;
    private long lastResultAt, lastFrameAt, lastStatsLogAt;
    /** Nobody seen for this long: the pose AI only looks every IDLE_INTERVAL_MS (~7 times a second). */
    private static final long IDLE_AFTER_MS = 2000, IDLE_INTERVAL_MS = 150;
    private long lastPersonAt, lastAiAt;   // analysis thread only
    private final float[] inference = new float[1];
    private final Pose pose = new Pose();  // analysis thread only
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

    /** Switches the pose model ("auto", "full" or "lite") while running; the engine re-opens on the analysis thread. */
    public static void setModel(String model) {
        MotionService s = instance;
        if (s != null) s.analysisThread.execute(() -> {
            try {
                s.engine.setModel(model);
            } catch (Throwable e) {
                s.fail("model: " + e.getMessage());
            }
        });
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
                        if ("USB".equals(description)) tuner.cameraStarted(camera.cameraId());
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

    /**
     * CameraX analyzer, on the analysis thread. Nobody seen for a couple of seconds: the pose AI only looks a few
     * times a second ({@link #IDLE_INTERVAL_MS}), and when the game doesn't want the camera picture either, the thread
     * sleeps in between, so CameraX doesn't even convert those frames: on the box the person detector on every frame
     * kept the NPU ~80 % busy for an empty room (heat). On the NPU the frame is read straight from CameraX's buffer.
     */
    private void analyze(@NonNull ImageProxy image) {
        long now = SystemClock.elapsedRealtime();
        boolean idle = now - lastPersonAt > IDLE_AFTER_MS;
        boolean videoOn = video.streaming();
        try {
            if (lastFrameAt > 0) cameraFps = smooth(cameraFps, 1000f / Math.max(1, now - lastFrameAt));
            lastFrameAt = now;

            tuner.onFrame(image, now);
            if (now - lastStatsLogAt > 5000) {
                lastStatsLogAt = now;
                String detail = engine.detail(), light = tuner.describe();
                Log.i(TAG, "stats: camera " + round(cameraFps) + " fps, AI " + round(fps) + " fps, "
                        + round(inferenceMs) + " ms, " + engine.delegate() + " " + engine.model()
                        + (detail.isEmpty() ? "" : " (" + detail + ")") + ", " + state + (idle ? " (idle)" : "")
                        + ", frame " + image.getWidth() + "x" + image.getHeight() + (light.isEmpty() ? "" : ", " + light));
            }
            boolean runAi = !idle || now - lastAiAt >= IDLE_INTERVAL_MS;
            ImageProxy.PlaneProxy plane = image.getPlanes()[0];
            ByteBuffer rgba = plane.getBuffer();
            int rotation = image.getImageInfo().getRotationDegrees();
            // the camera's buffer as it is, when nothing needs it turned or as a Bitmap
            boolean direct = rotation == 0 && plane.getPixelStride() == 4 && rgba.isDirect() && FrameShare.available();
            boolean needBitmap = snapshotRequested || (!direct && (videoOn || runAi)) || (runAi && !engine.usesNpu());
            Bitmap frame = null;
            if (needBitmap) {
                frame = toBitmap(image);
                if (rotation != 0) {
                    Matrix m = new Matrix();
                    m.postRotate(rotation);
                    frame = Bitmap.createBitmap(frame, 0, 0, frame.getWidth(), frame.getHeight(), m, true);
                }
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
            if (videoOn) {
                rgba.rewind();
                if (direct) FrameShare.offer(rgba, image.getWidth(), image.getHeight(), plane.getRowStride(),
                        video.width(), captureWall, ++videoFrame);
                else video.offer(frame, captureWall, videoFrame++);  // copies the frame; encoded on its own thread
            }
            if (!runAi) return;
            lastAiAt = now;
            long captureNs = image.getImageInfo().getTimestamp();   // the sensor's monotonic clock
            // blocking: the camera keeps only the newest frame meanwhile
            boolean ok;
            int width, height;
            if (frame == null) {
                rgba.rewind();
                width = image.getWidth();
                height = image.getHeight();
                ok = engine.detect(rgba, width, height, plane.getRowStride(), inference, captureNs, pose);
            } else {
                width = frame.getWidth();
                height = frame.getHeight();
                ok = engine.detect(frame, inference, captureNs, pose);
            }
            if (ok) {
                if (pose.found) lastPersonAt = now;
                onPoseResult(pose, captureWall, width, height, inference[0]);
            }
        } catch (Throwable e) {
            Log.e(TAG, "Frame failed", e);
        } finally {
            image.close();
        }
        // idle with no picture wanted: wait for the next look (CameraX keeps only the newest frame meanwhile)
        if (idle && !videoOn) {
            long wait = lastAiAt + IDLE_INTERVAL_MS - SystemClock.elapsedRealtime();
            if (wait > 0) SystemClock.sleep(Math.min(wait, IDLE_INTERVAL_MS));
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

    private void onPoseResult(Pose result, long captureWallMs, int width, int height, float inf) {
        long now = SystemClock.elapsedRealtime();
        if (lastResultAt > 0) fps = smooth(fps, 1000f / Math.max(1, now - lastResultAt));
        lastResultAt = now;
        inferenceMs = smooth(inferenceMs, inf);
        if (writer != null)
            writer.send(result, engine.source(), engine.model(), captureWallMs, width, height, fps, inferenceMs, cameraFps);
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
