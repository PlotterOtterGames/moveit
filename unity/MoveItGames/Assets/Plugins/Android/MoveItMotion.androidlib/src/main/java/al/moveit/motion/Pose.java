package al.moveit.motion;

import com.google.mediapipe.tasks.components.containers.Landmark;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarkerResult;

import java.util.List;

/** One camera frame's pose, the same numbers whichever engine made it (the NPU or MediaPipe). */
final class Pose {
    static final int LANDMARKS = 33;   // MediaPipe's pose landmarks

    boolean found;
    boolean hasWorld;
    /** x, y (0..1 of the picture), z (x's scale), visibility, presence per landmark. */
    final float[] landmarks = new float[LANDMARKS * 5];
    /** x, y, z in metres, hips at 0. */
    final float[] world = new float[LANDMARKS * 3];

    void set(NpuPose npu, boolean found) {
        this.found = found;
        hasWorld = found;
        if (found) {
            System.arraycopy(npu.landmarks, 0, landmarks, 0, landmarks.length);
            System.arraycopy(npu.world, 0, world, 0, world.length);
        }
    }

    void set(PoseLandmarkerResult result) {
        List<List<NormalizedLandmark>> people = result.landmarks();
        found = people != null && !people.isEmpty();
        hasWorld = false;
        if (!found) return;
        List<NormalizedLandmark> lms = people.get(0);
        for (int i = 0; i < LANDMARKS; i++) {
            NormalizedLandmark lm = lms.get(i);
            landmarks[i * 5] = lm.x();
            landmarks[i * 5 + 1] = lm.y();
            landmarks[i * 5 + 2] = lm.z();
            landmarks[i * 5 + 3] = lm.visibility().isPresent() ? lm.visibility().get() : 0f;
            landmarks[i * 5 + 4] = lm.presence().isPresent() ? lm.presence().get() : 0f;
        }
        List<List<Landmark>> worldPeople = result.worldLandmarks();
        if (worldPeople != null && !worldPeople.isEmpty()) {
            List<Landmark> w = worldPeople.get(0);
            for (int i = 0; i < LANDMARKS; i++) {
                world[i * 3] = w.get(i).x();
                world[i * 3 + 1] = w.get(i).y();
                world[i * 3 + 2] = w.get(i).z();
            }
            hasWorld = true;
        }
    }
}
