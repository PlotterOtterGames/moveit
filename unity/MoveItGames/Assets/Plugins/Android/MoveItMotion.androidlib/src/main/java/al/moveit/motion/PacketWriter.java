package al.moveit.motion;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;

/**
 * Builds Motion Protocol v1.1 datagrams (docs/protocol.md) from pose results (the NPU's or MediaPipe's) and sends them
 * over UDP to the game on the same device. Same format as the PC service (motion-service-py/protocol.py), so the
 * game's MoveSDK doesn't know or care where the skeleton came from.
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

    void send(Pose pose, String src, String model, long captureWallMs, int width, int height,
              float fps, float inferenceMs, float cameraFps) {
        sb.setLength(0);
        sb.append("{\"v\":1,\"src\":\"").append(src).append("\",\"t\":").append(captureWallMs)
          .append(",\"f\":").append(frame++).append(",\"w\":").append(width).append(",\"h\":").append(height)
          .append(",\"fps\":"); fixed(fps, 10);
        sb.append(",\"inf\":"); fixed(inferenceMs, 10);
        sb.append(",\"cam\":"); fixed(cameraFps, 10);
        sb.append(",\"model\":\"").append(model).append("\",\"people\":[");

        if (pose != null && pose.found) {
            float[] lms = pose.landmarks;
            float score = 0f;
            for (int t : TORSO) score += lms[MEDIAPIPE_TO_COCO17[t] * 5 + 3];
            sb.append("{\"id\":0,\"score\":"); fixed(score / TORSO.length, 1000);
            sb.append(",\"k\":[");
            for (int j = 0; j < MEDIAPIPE_TO_COCO17.length; j++) {
                int i = MEDIAPIPE_TO_COCO17[j] * 5;
                if (j > 0) sb.append(',');
                fixed(lms[i], 10000); sb.append(','); fixed(lms[i + 1], 10000); sb.append(',');
                fixed(lms[i + 2], 10000); sb.append(','); fixed(lms[i + 3], 1000);
            }
            sb.append(']');
            if (pose.hasWorld) {
                sb.append(",\"wk\":[");
                for (int j = 0; j < MEDIAPIPE_TO_COCO17.length; j++) {
                    int i = MEDIAPIPE_TO_COCO17[j] * 3;
                    if (j > 0) sb.append(',');
                    fixed(pose.world[i], 10000); sb.append(','); fixed(pose.world[i + 1], 10000); sb.append(',');
                    fixed(pose.world[i + 2], 10000);
                }
                sb.append(']');
            }
            sb.append('}');
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
