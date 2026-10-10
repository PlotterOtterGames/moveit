package al.moveit.cloud;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

import java.io.File;

/**
 * Boot, "this app was just updated", and the nightly alarm. Re-arms the background check, reports a finished
 * update or firmware install, and lets the night window try a pending install.
 */
public final class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        String action = intent.getAction();
        if (action == null) return;
        TunduCloud cloud = TunduCloud.get(context);
        switch (action) {
            case Intent.ACTION_BOOT_COMPLETED:
                FirmwareUpdater.checkResultAfterBoot(cloud);
                TunduCloud.scheduleJobs(context, true);
                break;
            case Intent.ACTION_MY_PACKAGE_REPLACED:
                Log.i(TunduCloud.TAG, "updated to " + cloud.appVersion);
                cloud.state.queueEvent("update_installed", "now " + cloud.appVersion);
                TunduCloud.cleanup(new File(context.getFilesDir(), "updates"), null);
                TunduCloud.scheduleJobs(context, true);
                break;
            case TunduCloud.ACTION_NIGHT:
                TunduCloud.scheduleJobs(context, true); // re-arms tomorrow's alarm and runs a check (installs if ready)
                break;
            default:
                break;
        }
    }
}
