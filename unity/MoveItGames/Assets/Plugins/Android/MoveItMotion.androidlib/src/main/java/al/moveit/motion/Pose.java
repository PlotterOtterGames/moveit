package al.moveit.motion;

import com.google.mediapipe.tasks.components.containers.Landmark;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarkerResult;

import java.util.List;

/**
 * One camera frame's poses, the same numbers whichever engine made them (the NPU or MediaPipe): the people the pose AI
 * looked at in this frame (with two followed on the NPU and a shared budget, not always both).
 */
final class Pose {
    static final int LANDMARKS = 33;   // MediaPipe's pose landmarks
    static final int MAX_PEOPLE = NpuPose.MAX_PEOPLE;

    /** One person. */
    static final class Person {
        boolean found;
        boolean hasWorld;
        /** The NPU's track: the same id while this person is followed (0 = not known: MediaPipe). */
        int track;
        /** x, y (0..1 of the picture), z (x's scale), visibility, presence per landmark. */
        final float[] landmarks = new float[LANDMARKS * 5];
        /** x, y, z in metres, hips at 0. */
        final float[] world = new float[LANDMARKS * 3];
    }

    /** People in {@link #people} for this frame (found or lost in it). */
    int count;
    /** Someone was found in this frame. */
    boolean found;
    final Person[] people = new Person[MAX_PEOPLE];

    Pose() {
        for (int i = 0; i < MAX_PEOPLE; i++) people[i] = new Person();
    }

    void set(NpuPose npu) {
        count = npu.count;
        found = false;
        for (int i = 0; i < count; i++) {
            Person p = people[i];
            p.found = npu.found[i];
            p.hasWorld = p.found;
            p.track = npu.track[i];
            if (p.found) {
                System.arraycopy(npu.landmarks[i], 0, p.landmarks, 0, p.landmarks.length);
                System.arraycopy(npu.world[i], 0, p.world, 0, p.world.length);
                found = true;
            }
        }
    }

    void set(PoseLandmarkerResult result) {
        List<List<NormalizedLandmark>> all = result.landmarks();
        List<List<Landmark>> allWorld = result.worldLandmarks();
        count = all == null ? 0 : Math.min(all.size(), MAX_PEOPLE);
        found = count > 0;
        for (int k = 0; k < count; k++) {
            Person p = people[k];
            p.found = true;
            p.track = 0;
            List<NormalizedLandmark> lms = all.get(k);
            for (int i = 0; i < LANDMARKS; i++) {
                NormalizedLandmark lm = lms.get(i);
                p.landmarks[i * 5] = lm.x();
                p.landmarks[i * 5 + 1] = lm.y();
                p.landmarks[i * 5 + 2] = lm.z();
                p.landmarks[i * 5 + 3] = lm.visibility().isPresent() ? lm.visibility().get() : 0f;
                p.landmarks[i * 5 + 4] = lm.presence().isPresent() ? lm.presence().get() : 0f;
            }
            p.hasWorld = allWorld != null && allWorld.size() > k;
            if (p.hasWorld) {
                List<Landmark> w = allWorld.get(k);
                for (int i = 0; i < LANDMARKS; i++) {
                    p.world[i * 3] = w.get(i).x();
                    p.world[i * 3 + 1] = w.get(i).y();
                    p.world[i * 3 + 2] = w.get(i).z();
                }
            }
        }
    }
}
