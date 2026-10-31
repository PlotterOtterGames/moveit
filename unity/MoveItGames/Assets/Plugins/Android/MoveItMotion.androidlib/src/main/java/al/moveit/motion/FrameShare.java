package al.moveit.motion;

import java.nio.ByteBuffer;

/**
 * The camera picture for the game, handed over inside the process (src/main/cpp/frame_share.cpp): the newest frame,
 * downscaled, which the game copies straight into its texture (CameraFeed.cs). Replaces {@link VideoStreamer}'s JPEG
 * over UDP on the box, which cost a JPEG encode here and a decode on the game's main thread for every frame.
 */
final class FrameShare {
    private FrameShare() { }

    /** True where the native library loads (the game then reads frames from it too). */
    static boolean available() {
        return NpuPose.libraryLoaded();
    }

    /** On the camera thread: the frame (RGBA, direct buffer), downscaled to targetWidth. Takes ~1-2 ms. */
    static void offer(ByteBuffer rgba, int width, int height, int rowStride, int targetWidth, long captureWallMs,
                      long frameNumber) {
        nativeOffer(rgba, width, height, rowStride, targetWidth, captureWallMs, frameNumber);
    }

    private static native void nativeOffer(ByteBuffer rgba, int width, int height, int rowStride, int targetWidth,
                                           long captureWallMs, long frameNumber);
}
