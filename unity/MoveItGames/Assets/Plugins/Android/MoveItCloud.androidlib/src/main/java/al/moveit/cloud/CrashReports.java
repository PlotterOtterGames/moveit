package al.moveit.cloud;

import android.app.ActivityManager;
import android.app.ApplicationExitInfo;
import android.content.Context;
import android.os.Build;

import java.util.List;

/**
 * Crash reporting without any SDK: Android 11+ keeps the reasons our process died (Java crash, native crash, ANR).
 * At each start the new ones become health events (kind + Android's short description, no personal data), so a
 * staged rollout shows whether a new version crashes more than the old one.
 */
final class CrashReports {
    static final String LAST = "crash.last_timestamp";

    private CrashReports() {}

    static void collect(Context context, CloudState state, KeyValueStore store) {
        if (Build.VERSION.SDK_INT < 30) return;
        ActivityManager am = context.getSystemService(ActivityManager.class);
        if (am == null) return;
        List<ApplicationExitInfo> exits;
        try {
            exits = am.getHistoricalProcessExitReasons(context.getPackageName(), 0, 16);
        } catch (RuntimeException e) {
            return;
        }
        long last = store.getLong(LAST, 0);
        long newest = last;
        for (ApplicationExitInfo e : exits) {
            if (e.getTimestamp() <= last) continue;
            newest = Math.max(newest, e.getTimestamp());
            String kind;
            switch (e.getReason()) {
                case ApplicationExitInfo.REASON_CRASH:
                    kind = "crash";
                    break;
                case ApplicationExitInfo.REASON_CRASH_NATIVE:
                    kind = "crash_native";
                    break;
                case ApplicationExitInfo.REASON_ANR:
                    kind = "anr";
                    break;
                default:
                    continue; // normal exits, low memory kills, updates
            }
            String description = e.getDescription();
            state.queueEvent(kind, (e.getProcessName() == null ? "" : e.getProcessName() + ": ") + (description == null ? "" : description));
        }
        if (newest > last) store.put(LAST, newest);
    }
}
