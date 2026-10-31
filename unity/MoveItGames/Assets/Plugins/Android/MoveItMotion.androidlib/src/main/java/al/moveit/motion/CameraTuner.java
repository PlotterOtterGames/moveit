package al.moveit.motion;

import android.util.Log;

import androidx.camera.core.ImageProxy;

import java.io.File;
import java.nio.ByteBuffer;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Keeps the USB camera's picture usable in a dim living room. In the user's evening room the camera's own settings
 * (gain 0, gamma 100) gave a nearly black picture (Vishu!'s mirror: mean 31 of 255 on the X98K Pro); gain 100,
 * gamma 160 and saturation 100 brought it to ~107 at the same 30 fps. This measures the picture a few times a second
 * and steps those controls up a ladder in the dark and back down in the light (hysteresis, one step at a time while
 * the camera's auto exposure settles), and keeps exposure priority off so the frame rate never drops in the dark
 * (fewer, longer frames blur and delay every move). The controls themselves: {@link CameraControls} (the camera's
 * node where the firmware allows it, else root on test boxes, else nothing: the camera keeps its own settings).
 */
final class CameraTuner {
    private static final String TAG = "MoveItMotion";
    // The ladder: gain as a share of its range, gamma and saturation as multiples of their defaults. Level 4 = what
    // worked in the user's evening room (gain 100, gamma 160, saturation 100 on the X98K Pro's camera).
    private static final float[] GAIN = {0f, 0.4f, 0.8f, 1f, 1f, 1f};
    private static final float[] GAMMA = {1f, 1f, 1.1f, 1.3f, 1.6f, 1.9f};
    private static final float[] SATURATION = {1f, 1.17f, 1.33f, 1.5f, 1.67f, 1.83f};
    // Mean brightness (0..255) of the middle of the picture, where the player is.
    private static final int VERY_DARK = 55, DARK = 90, BRIGHT = 170, VERY_BRIGHT = 210;
    private static final long SAMPLE_MS = 250, SETTLE_MS = 2000, GAP_MS = 2000;

    private final ExecutorService worker = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "camera-tuner");
        t.setDaemon(true);
        return t;
    });
    private CameraControls controls;          // worker thread only
    private volatile boolean ready, busy, unavailable;
    private volatile String how = "";
    private volatile int level, appliedLevel = -1;
    private volatile float mean = -1;
    private long lastSample, lastChange, lastFrameAt;   // analysis thread only

    /** The camera started: tune it. cameraId from CameraX ("109" on the X98K Pro = /dev/video9). */
    void cameraStarted(String cameraId) {
        final String device = devicePath(cameraId);
        worker.execute(() -> {
            if (device == null) {
                giveUp("no video node for camera " + cameraId);
                return;
            }
            controls = CameraControls.open(device);
            if (controls == null) {
                giveUp("can't reach " + device + " (not open to the app, no root)");
                return;
            }
            how = controls.how();
            ready = true;
            Log.i(TAG, "Camera tuning " + device + " (" + how + "): " + describeRanges());
        });
    }

    /** Every camera frame (analysis thread). Cheap: a 32 x 24 grid of pixels a few times a second. */
    void onFrame(ImageProxy image, long nowMs) {
        if (unavailable) return;
        if (lastFrameAt > 0 && nowMs - lastFrameAt > GAP_MS) appliedLevel = -1;  // the camera restarted: set it again
        lastFrameAt = nowMs;
        if (nowMs - lastSample < SAMPLE_MS) return;
        lastSample = nowMs;
        float m = brightness(image);
        mean = mean < 0 ? m : 0.7f * mean + 0.3f * m;
        if (!ready || busy) return;
        int next = level;
        if (appliedLevel >= 0 && nowMs - lastChange > SETTLE_MS) {
            if (mean < VERY_DARK) next = level + 2;
            else if (mean < DARK) next = level + 1;
            else if (mean > VERY_BRIGHT) next = level - 2;
            else if (mean > BRIGHT) next = level - 1;
            next = Math.max(0, Math.min(GAIN.length - 1, next));
        }
        if (next == level && appliedLevel == level) return;
        level = next;
        lastChange = nowMs;
        busy = true;
        final int target = next;
        final float seen = mean;
        worker.execute(() -> apply(target, seen));
    }

    /** For the stats line: "light 4 (mean 107, root)". */
    String describe() {
        if (unavailable) return "";
        return "light " + (appliedLevel >= 0 ? appliedLevel : "-") + " (mean " + Math.round(mean) + (how.isEmpty() ? "" : ", " + how) + ")";
    }

    private void apply(int target, float seen) {
        try {
            Map<String, Integer> values = new LinkedHashMap<>();
            int[] r = controls.range("gain");
            if (r != null) values.put("gain", Math.round(r[0] + GAIN[target] * (r[1] - r[0])));
            r = controls.range("gamma");
            if (r != null) values.put("gamma", clamp(Math.round(r[2] * GAMMA[target]), r));
            r = controls.range("saturation");
            if (r != null) values.put("saturation", clamp(Math.round(r[2] * SATURATION[target]), r));
            if (controls.range("exposure_auto_priority") != null) values.put("exposure_auto_priority", 0);
            if (controls.set(values)) {
                appliedLevel = target;
                Log.i(TAG, "Camera light level " + target + " (picture mean " + Math.round(seen) + "): " + values);
            } else {
                Log.w(TAG, "Camera light level " + target + " refused: " + values);
                appliedLevel = target;  // don't retry every frame; the next change tries again
            }
        } catch (Throwable e) {
            giveUp("tuning failed: " + e);
        } finally {
            busy = false;
        }
    }

    private void giveUp(String why) {
        unavailable = true;
        Log.i(TAG, "Camera tuning off: " + why + ". The camera keeps its own settings.");
    }

    private String describeRanges() {
        StringBuilder sb = new StringBuilder();
        for (String name : CameraControls.NAMES) {
            int[] r = controls.range(name);
            if (r == null) continue;
            if (sb.length() > 0) sb.append(", ");
            sb.append(name).append(' ').append(r[0]).append("..").append(r[1]).append(" (default ").append(r[2])
              .append(", now ").append(r[3]).append(')');
        }
        return sb.toString();
    }

    private static int clamp(int v, int[] range) {
        return Math.max(range[0], Math.min(range[1], v));
    }

    /** Mean brightness of the picture's middle (x 15-85 %, y 10-90 %): where the player stands. */
    private static float brightness(ImageProxy image) {
        ImageProxy.PlaneProxy plane = image.getPlanes()[0];
        ByteBuffer b = plane.getBuffer();
        int w = image.getWidth(), h = image.getHeight(), rs = plane.getRowStride(), ps = plane.getPixelStride();
        long sum = 0;
        int n = 0;
        for (int j = 0; j < 24; j++) {
            int y = h * (10 + 80 * j / 23) / 100;
            for (int i = 0; i < 32; i++) {
                int x = w * (15 + 70 * i / 31) / 100;
                int o = y * rs + x * ps;
                if (o + 2 >= b.limit()) continue;
                sum += (77 * (b.get(o) & 255) + 150 * (b.get(o + 1) & 255) + 29 * (b.get(o + 2) & 255)) >> 8;
                n++;
            }
        }
        return n > 0 ? (float) sum / n : 0f;
    }

    /**
     * The camera's V4L2 node. Android's external camera HAL numbers USB cameras 100 + the node number ("109" =
     * /dev/video9); otherwise the first node the UVC driver made.
     */
    private static String devicePath(String cameraId) {
        try {
            int id = Integer.parseInt(cameraId);
            if (id >= 100 && new File("/dev/video" + (id - 100)).exists()) return "/dev/video" + (id - 100);
        } catch (Exception ignored) {
            // not a number: look for the UVC node below
        }
        File[] nodes = new File("/sys/class/video4linux").listFiles();
        if (nodes == null) return null;
        String best = null;
        int bestNumber = Integer.MAX_VALUE;
        for (File node : nodes) {
            try {
                if (!new File(node, "device/driver").getCanonicalPath().endsWith("uvcvideo")) continue;
                int number = Integer.parseInt(node.getName().replace("video", ""));
                if (number < bestNumber) {
                    bestNumber = number;
                    best = "/dev/" + node.getName();
                }
            } catch (Exception ignored) {
                // not readable: skip
            }
        }
        return best;
    }
}
