package al.moveit.motion;

import android.app.Activity;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CaptureRequest;
import android.util.Log;
import android.util.Range;
import android.util.Size;

import androidx.annotation.OptIn;
import androidx.camera.camera2.interop.Camera2CameraInfo;
import androidx.camera.camera2.interop.Camera2Interop;
import androidx.camera.camera2.interop.ExperimentalCamera2Interop;
import androidx.camera.core.CameraInfo;
import androidx.camera.core.CameraSelector;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.resolutionselector.ResolutionSelector;
import androidx.camera.core.resolutionselector.ResolutionStrategy;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.core.content.ContextCompat;
import androidx.lifecycle.LifecycleOwner;

import com.google.common.util.concurrent.ListenableFuture;

import java.util.Arrays;
import java.util.List;
import java.util.concurrent.Executor;

/**
 * CameraX frames for the pose engine. Prefers the box's USB camera (external), then a back camera, then a front one
 * (phones, emulators). 640×480 RGBA, keep-only-latest: frames are never queued, so there's no hidden latency.
 *
 * Frame rate: cameras slow down to ~15 fps in a dim room to expose longer, which adds ~33 ms of delay and blurs
 * fast moves. We ask auto-exposure for a range that never goes below ~30 fps (a slightly darker, noisier image
 * tracks far better than a blurred one). If the camera refuses it, it runs with its default range.
 */
final class CameraSource {
    interface Callback {
        void onStarted(String description);
        void onError(String message);
    }

    private static final String TAG = "MoveItMotion";
    private static final int LENS_FACING_EXTERNAL = 2;  // CameraSelector.LENS_FACING_EXTERNAL (marked experimental)

    private ProcessCameraProvider provider;

    /** Must be called on the main thread. */
    void start(Activity activity, LifecycleOwner owner, Executor analysisExecutor, ImageAnalysis.Analyzer analyzer,
               Callback callback) {
        ListenableFuture<ProcessCameraProvider> future = ProcessCameraProvider.getInstance(activity);
        future.addListener(() -> {
            try {
                provider = future.get();
                CameraInfo info = pick(provider.getAvailableCameraInfos());
                if (info == null) {
                    callback.onError("no-camera");
                    return;
                }
                Range<Integer> fps = pickFpsRange(info);
                try {
                    bind(owner, info, fps, analysisExecutor, analyzer);
                } catch (Throwable e) {
                    if (fps == null) throw e;
                    Log.w(TAG, "Camera refused " + fps + " fps, using its default range: " + e);
                    fps = null;
                    bind(owner, info, null, analysisExecutor, analyzer);
                }
                Log.i(TAG, "Camera " + describe(info) + ", fps range " + (fps != null ? fps : "default"));
                callback.onStarted(describe(info));
            } catch (Throwable e) {
                Log.e(TAG, "Camera start failed", e);
                callback.onError("camera: " + e.getMessage());
            }
        }, ContextCompat.getMainExecutor(activity));
    }

    /** Must be called on the main thread. */
    void stop() {
        if (provider != null) provider.unbindAll();
    }

    @OptIn(markerClass = ExperimentalCamera2Interop.class)
    private void bind(LifecycleOwner owner, CameraInfo info, Range<Integer> fps, Executor analysisExecutor,
                      ImageAnalysis.Analyzer analyzer) {
        ImageAnalysis.Builder builder = new ImageAnalysis.Builder()
                .setResolutionSelector(new ResolutionSelector.Builder()
                        .setResolutionStrategy(new ResolutionStrategy(new Size(640, 480),
                                ResolutionStrategy.FALLBACK_RULE_CLOSEST_HIGHER_THEN_LOWER))
                        .build())
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_RGBA_8888);
        if (fps != null)
            new Camera2Interop.Extender<>(builder).setCaptureRequestOption(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, fps);
        ImageAnalysis analysis = builder.build();
        analysis.setAnalyzer(analysisExecutor, analyzer);
        provider.unbindAll();
        provider.bindToLifecycle(owner, info.getCameraSelector(), analysis);
    }

    /**
     * The auto-exposure range whose lowest frame rate is closest to 30 fps (at least 25), preferring a higher top
     * (up to 60: in a bright room fresher frames mean less delay). Null if the camera has no such range.
     */
    @OptIn(markerClass = ExperimentalCamera2Interop.class)
    private static Range<Integer> pickFpsRange(CameraInfo info) {
        Range<Integer>[] ranges;
        try {
            ranges = Camera2CameraInfo.from(info).getCameraCharacteristic(
                    CameraCharacteristics.CONTROL_AE_AVAILABLE_TARGET_FPS_RANGES);
        } catch (Throwable e) {
            Log.w(TAG, "Can't read camera fps ranges: " + e);
            return null;
        }
        if (ranges == null) return null;
        Log.i(TAG, "Camera fps ranges: " + Arrays.toString(ranges));
        Range<Integer> best = null;
        for (Range<Integer> r : ranges) {
            int low = r.getLower(), high = r.getUpper();
            if (low < 25 || low > 30 || high > 60) continue;
            if (best == null || low > best.getLower() || (low == best.getLower() && high > best.getUpper())) best = r;
        }
        return best;
    }

    private static CameraInfo pick(List<CameraInfo> infos) {
        int[] preference = {LENS_FACING_EXTERNAL, CameraSelector.LENS_FACING_BACK, CameraSelector.LENS_FACING_FRONT};
        for (int facing : preference)
            for (CameraInfo info : infos)
                if (info.getLensFacing() == facing) return info;
        return infos.isEmpty() ? null : infos.get(0);
    }

    private static String describe(CameraInfo info) {
        switch (info.getLensFacing()) {
            case LENS_FACING_EXTERNAL: return "USB";
            case CameraSelector.LENS_FACING_BACK: return "back";
            case CameraSelector.LENS_FACING_FRONT: return "front";
            default: return "camera";
        }
    }
}
