package al.moveit.motion;

import android.util.Log;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * The USB camera's own controls (UVC, through its V4L2 node /dev/videoN) by name: gain, gamma, saturation,
 * exposure_auto_priority. Android's camera API doesn't reach them on the boxes (the external camera HAL passes none).
 * Two ways in: the node itself, where the firmware lets the app open it (camera_controls.cpp), or root with v4l2-ctl,
 * which only test boxes with an open su allow (userdebug firmware like the X98K Pro's).
 */
abstract class CameraControls {
    private static final String TAG = "MoveItMotion";
    static final String[] NAMES = {"gain", "gamma", "saturation", "exposure_auto_priority"};
    private static final int[] IDS = {0x00980913, 0x00980910, 0x00980902, 0x009a0903};  // V4L2_CID_*

    /** min, max, default, current value; null if the camera doesn't have this control. */
    abstract int[] range(String name);

    /** Sets these controls in one go. False if the camera refused. */
    abstract boolean set(Map<String, Integer> values);

    /** "direct" or "root", for the log. */
    abstract String how();

    void close() { }

    /** The camera's controls, the direct way first; null if neither way works on this box. */
    static CameraControls open(String device) {
        CameraControls c = Direct.open(device);
        if (c == null) c = Root.open(device);
        return c;
    }

    private static final class Direct extends CameraControls {
        private final int fd;
        private final Map<String, int[]> ranges = new HashMap<>();

        private Direct(int fd) {
            this.fd = fd;
        }

        static Direct open(String device) {
            if (!NpuPose.libraryLoaded()) return null;  // the JNI lives in libtundupose.so
            int fd = nativeOpen(device);
            if (fd < 0) {
                Log.i(TAG, "Camera controls: " + device + " not open to the app (errno " + -fd + ")");
                return null;
            }
            Direct d = new Direct(fd);
            for (int i = 0; i < NAMES.length; i++) {
                int[] r = nativeQuery(fd, IDS[i]);
                if (r != null) d.ranges.put(NAMES[i], r);
            }
            if (d.ranges.isEmpty()) {
                d.close();
                return null;
            }
            return d;
        }

        @Override int[] range(String name) { return ranges.get(name); }

        @Override boolean set(Map<String, Integer> values) {
            boolean ok = true;
            for (Map.Entry<String, Integer> e : values.entrySet()) {
                for (int i = 0; i < NAMES.length; i++)
                    if (NAMES[i].equals(e.getKey())) ok &= nativeSet(fd, IDS[i], e.getValue()) == 0;
            }
            return ok;
        }

        @Override String how() { return "direct"; }

        @Override void close() { nativeClose(fd); }
    }

    private static final class Root extends CameraControls {
        // v4l2-ctl --list-ctrls: "  gain 0x00980913 (int)    : min=0 max=100 step=1 default=0 value=0"
        private static final Pattern LINE = Pattern.compile("^\\s*(\\w+)\\s+0x[0-9a-f]+\\s+\\((\\w+)\\)\\s*:\\s*(.*)$");
        private static final Pattern FIELD = Pattern.compile("(min|max|default|value)=(-?\\d+)");
        private final String device;
        private final Map<String, int[]> ranges = new HashMap<>();

        private Root(String device) {
            this.device = device;
        }

        static Root open(String device) {
            Root r = new Root(device);
            List<String> lines = run("v4l2-ctl", "-d", device, "--list-ctrls");
            if (lines == null) return null;
            for (String line : lines) {
                Matcher m = LINE.matcher(line);
                if (!m.matches()) continue;
                int[] v = {0, 1, 0, 0};
                Matcher f = FIELD.matcher(m.group(3));
                while (f.find()) {
                    int value = Integer.parseInt(f.group(2));
                    switch (f.group(1)) {
                        case "min": v[0] = value; break;
                        case "max": v[1] = value; break;
                        case "default": v[2] = value; break;
                        default: v[3] = value; break;
                    }
                }
                for (String name : NAMES) if (name.equals(m.group(1))) r.ranges.put(name, v);
            }
            return r.ranges.isEmpty() ? null : r;
        }

        @Override int[] range(String name) { return ranges.get(name); }

        @Override boolean set(Map<String, Integer> values) {
            StringBuilder sb = new StringBuilder("--set-ctrl=");
            for (Map.Entry<String, Integer> e : values.entrySet()) {
                if (sb.length() > 11) sb.append(',');
                sb.append(e.getKey()).append('=').append(e.getValue());
            }
            return run("v4l2-ctl", "-d", device, sb.toString()) != null;
        }

        @Override String how() { return "root"; }

        /** Runs a command as root (su 0 ...): its output lines, or null if it failed or there's no usable su. */
        private static List<String> run(String... command) {
            List<String> cmd = new ArrayList<>();
            cmd.add("su");
            cmd.add("0");
            for (String c : command) cmd.add(c);
            try {
                Process p = new ProcessBuilder(cmd).redirectErrorStream(true).start();
                List<String> lines = new ArrayList<>();
                try (BufferedReader in = new BufferedReader(new InputStreamReader(p.getInputStream()))) {
                    String line;
                    while ((line = in.readLine()) != null) lines.add(line);
                }
                if (!p.waitFor(3, TimeUnit.SECONDS)) {
                    p.destroy();
                    return null;
                }
                return p.exitValue() == 0 ? lines : null;
            } catch (Exception e) {
                return null;
            }
        }
    }

    private static native int nativeOpen(String device);
    private static native int[] nativeQuery(int fd, int id);
    private static native int nativeSet(int fd, int id, int value);
    private static native void nativeClose(int fd);
}
