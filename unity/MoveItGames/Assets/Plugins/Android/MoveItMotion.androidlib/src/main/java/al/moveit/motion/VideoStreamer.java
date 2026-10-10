package al.moveit.motion;

import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Rect;
import android.os.SystemClock;
import android.util.Log;

import java.io.ByteArrayOutputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Camera preview stream for the game (docs/protocol.md, "Camera preview stream"), same format as the PC service's
 * motion-service-py/video.py: one UDP datagram per frame to 127.0.0.1, header ("TVF1", capture time ms, frame number,
 * width, height; little-endian) + JPEG. Only while the game asks for it (Pikturo's camera mode) and only to this
 * device: the picture never leaves the box.
 *
 * The frame is copied into a reused bitmap on the camera thread (~1 ms) and encoded on its own thread, so the pose
 * AI never waits for the JPEG encoder. While the encoder is busy, frames are skipped.
 */
final class VideoStreamer {
    private static final String TAG = "MoveItMotion";
    private static final int MAX_DATAGRAM = 60000;
    private static final int HEADER = 20;
    private static final long KEEPALIVE_MS = 3000;  // stop if the game stops asking (crash, scene change)

    private final ExecutorService encoder = Executors.newSingleThreadExecutor();
    private final AtomicBoolean busy = new AtomicBoolean();
    private final ByteArrayOutputStream jpeg = new ByteArrayOutputStream(64 * 1024);
    private final Paint paint = new Paint(Paint.FILTER_BITMAP_FLAG);
    private DatagramSocket socket;
    private InetAddress localhost;
    private Bitmap copy;  // encoder's frame, written only while !busy

    private volatile int port;
    private volatile int width = 640;
    private volatile long wantedUntil;
    private volatile int sent;

    /** Called by the game about once a second while it wants frames; on = false stops at once. */
    void request(int port, int width, boolean on) {
        this.port = port;
        this.width = Math.max(160, Math.min(1280, width));
        boolean was = streaming();
        wantedUntil = on ? SystemClock.elapsedRealtime() + KEEPALIVE_MS : 0;
        if (on != was) Log.i(TAG, "Camera preview stream " + (on ? "on, port " + port : "off, " + sent + " frames sent"));
    }

    boolean streaming() {
        return SystemClock.elapsedRealtime() < wantedUntil;
    }

    /** On the camera thread, with the frame the pose AI sees. Returns at once. */
    void offer(Bitmap frame, long captureWallMs, long frameNumber) {
        if (!streaming() || !busy.compareAndSet(false, true)) return;
        try {
            int w = Math.min(width, frame.getWidth());
            int h = Math.round(frame.getHeight() * (w / (float) frame.getWidth()));
            if (copy == null || copy.getWidth() != w || copy.getHeight() != h)
                copy = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888);
            new Canvas(copy).drawBitmap(frame, null, new Rect(0, 0, w, h), paint);
        } catch (Throwable e) {
            busy.set(false);
            Log.w(TAG, "Preview frame copy failed: " + e);
            return;
        }
        encoder.execute(() -> {
            try {
                send(captureWallMs, frameNumber);
            } catch (Throwable e) {
                Log.w(TAG, "Preview frame failed: " + e);
            } finally {
                busy.set(false);
            }
        });
    }

    private void send(long captureWallMs, long frameNumber) throws Exception {
        if (socket == null) {
            socket = new DatagramSocket();
            localhost = InetAddress.getByName("127.0.0.1");
        }
        int quality = 80;
        do {
            jpeg.reset();
            jpeg.write(new byte[HEADER]);  // placeholder, filled below
            copy.compress(Bitmap.CompressFormat.JPEG, quality, jpeg);
            quality -= 15;
        } while (jpeg.size() > MAX_DATAGRAM && quality >= 35);
        if (jpeg.size() > MAX_DATAGRAM) return;

        byte[] data = jpeg.toByteArray();
        ByteBuffer.wrap(data, 0, HEADER).order(ByteOrder.LITTLE_ENDIAN)
                .put((byte) 'T').put((byte) 'V').put((byte) 'F').put((byte) '1')
                .putLong(captureWallMs).putInt((int) frameNumber)
                .putShort((short) copy.getWidth()).putShort((short) copy.getHeight());
        try {
            socket.send(new DatagramPacket(data, data.length, localhost, port));
            sent++;
        } catch (Exception ignored) {
            // the game stopped listening: the keepalive runs out soon
        }
    }
}
