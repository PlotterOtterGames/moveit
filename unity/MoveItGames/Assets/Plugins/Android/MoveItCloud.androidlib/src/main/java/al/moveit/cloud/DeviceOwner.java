package al.moveit.cloud;

import android.app.admin.DevicePolicyManager;
import android.app.admin.SystemUpdatePolicy;
import android.content.ComponentName;
import android.content.Context;
import android.os.Build;
import android.util.Log;

import java.util.Collections;

/**
 * What the app does when it is device owner. Deliberately little, and nothing that could lock us out:
 * - the box's own system updates (if the vendor's updater is still there) may only install 02:00-05:00;
 * - nobody can force-stop Tundu or clear its data from Android settings (that would delete the box's identity).
 * Stronger lock-down (no adb, no unknown sources) belongs in the ODM firmware for shipped units, after testing on
 * a real box: see docs/cloud.md.
 */
final class DeviceOwner {
    private DeviceOwner() {}

    static ComponentName admin(Context context) {
        return new ComponentName(context, AdminReceiver.class);
    }

    static boolean isOwner(Context context) {
        DevicePolicyManager dpm = context.getSystemService(DevicePolicyManager.class);
        return dpm != null && dpm.isDeviceOwnerApp(context.getPackageName());
    }

    static void applyPolicies(Context context) {
        if (!isOwner(context)) return;
        DevicePolicyManager dpm = context.getSystemService(DevicePolicyManager.class);
        ComponentName admin = admin(context);
        try {
            dpm.setSystemUpdatePolicy(admin, SystemUpdatePolicy.createWindowedInstallPolicy(120, 300));
        } catch (RuntimeException e) {
            Log.w(TunduCloud.TAG, "system update policy: " + e.getMessage());
        }
        if (Build.VERSION.SDK_INT >= 30) {
            try {
                dpm.setUserControlDisabledPackages(admin, Collections.singletonList(context.getPackageName()));
            } catch (RuntimeException e) {
                Log.w(TunduCloud.TAG, "user control: " + e.getMessage());
            }
        }
    }
}
