package al.moveit.motion;

import com.google.mediapipe.tasks.components.containers.Landmark;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarkerResult;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.List;

/**
 * Builds Motion Protocol v1.1 datagrams (docs/protocol.md) from MediaPipe results and sends them over UDP to the
 * game on the same device. Same format as the PC service (motion-service-py/protocol.py), so the game's MoveSDK
 * doesn't know or care where the skeleton came from.
 */
final class PacketWriter {
    /** MediaPipe Pose (33 landmarks) index for each COCO-17 joint. Same table as motion-service-py/skeleton.py. */
    static final int[] MEDIAPIPE_TO_COCO17 = {0, 2, 5, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28};
    private static final int[] TORSO = {5, 6, 11, 12};

    private final DatagramSocket socket;
    private final InetAddress address;
    private final int port;
    private final StringBuilder sb = new StringBuilder(2048);
    private long frame;

    PacketWriter(int port) throws Exception {
        this.socket = new DatagramSocket();
        this.address = InetAddress.getByName("127.0.0.1");
        this.port = port;
    }

    void send(PoseLandmarkerResult result, String src, String model, long captureWallMs, int width, int height,
              float fps, float inferenceMs, float cameraFps) {
        sb.setLength(0);
        sb.append("{\"v\":1,\"src\":\"").append(src).append("\",\"t\":").append(captureWallMs)
          .append(",\"f\":").append(frame++).append(",\"w\":").append(width).append(",\"h\":").append(height)
          .append(",\"fps\":"); fixed(fps, 10);
        sb.append(",\"inf\":"); fixed(inferenceMs, 10);
        sb.append(",\"cam\":"); fixed(cameraFps, 10);
        sb.append(",\"model\":\"").append(model).append("\",\"people\":[");

        List<List<NormalizedLandmark>> people = result != null ? result.landmarks() : null;
        List<List<Landmark>> world = result != null ? result.worldLandmarks() : null;
        if (people != null) {
            for (int p = 0; p < people.size(); p++) {
                List<NormalizedLandmark> lms = people.get(p);
                if (p > 0) sb.append(',');
                float score = 0f;
                for (int t : TORSO) score += visibility(lms.get(MEDIAPIPE_TO_COCO17[t]));
                sb.append("{\"id\":").append(p).append(",\"score\":"); fixed(score / TORSO.length, 1000);
                sb.append(",\"k\":[");
                for (int j = 0; j < MEDIAPIPE_TO_COCO17.length; j++) {
                    NormalizedLandmark lm = lms.get(MEDIAPIPE_TO_COCO17[j]);
                    if (j > 0) sb.append(',');
                    fixed(lm.x(), 10000); sb.append(','); fixed(lm.y(), 10000); sb.append(',');
                    fixed(lm.z(), 10000); sb.append(','); fixed(visibility(lm), 1000);
                }
                sb.append(']');
                if (world != null && p < world.size()) {
                    List<Landmark> w = world.get(p);
                    sb.append(",\"wk\":[");
                    for (int j = 0; j < MEDIAPIPE_TO_COCO17.length; j++) {
                        Landmark lm = w.get(MEDIAPIPE_TO_COCO17[j]);
                        if (j > 0) sb.append(',');
                        fixed(lm.x(), 10000); sb.append(','); fixed(lm.y(), 10000); sb.append(','); fixed(lm.z(), 10000);
                    }
                    sb.append(']');
                }
                sb.append('}');
            }
        }
        sb.append("]}");

        byte[] data = sb.toString().getBytes(StandardCharsets.UTF_8);
        try {
            socket.send(new DatagramPacket(data, data.length, address, port));
        } catch (Exception ignored) {
            // The game isn't listening (yet): drop the frame, the next one comes in ~33 ms.
        }
    }

    void close() {
        socket.close();
    }

    private static float visibility(NormalizedLandmark lm) {
        return lm.visibility().isPresent() ? lm.visibility().get() : 0f;
    }

    /** Appends a number rounded to 1/scale, never in exponent notation and independent of the device's locale. */
    private void fixed(float value, int scale) {
        if (Float.isNaN(value) || Float.isInfinite(value)) value = 0f;
        long n = Math.round((double) value * scale);
        if (n < 0) {
            sb.append('-');
            n = -n;
        }
        sb.append(n / scale).append('.');
        long frac = n % scale;
        for (int s = scale / 10; s > 0; s /= 10) {
            sb.append((char) ('0' + frac / s));
            frac %= s;
        }
    }
}
