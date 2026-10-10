package al.moveit.cloud;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;
import android.os.Build;
import android.util.Log;

/** PackageInstaller's answer to an install we started (Installer). */
public final class InstallReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (!Installer.ACTION_RESULT.equals(intent.getAction())) return;
        int status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
        String sha = intent.getStringExtra(Installer.EXTRA_SHA256);
        String message = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
        TunduCloud cloud = TunduCloud.get(context);
        if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
            // Not device owner / system app: Android asks the user once (OK on the remote).
            Intent confirm = Build.VERSION.SDK_INT >= 33
                    ? intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent.class)
                    : legacyIntentExtra(intent);
            if (confirm != null) {
                confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                context.startActivity(confirm);
            }
            return;
        }
        if (status == PackageInstaller.STATUS_SUCCESS) {
            Log.i(TunduCloud.TAG, "install succeeded"); // usually not seen: the app restarts as the new version
            return;
        }
        Log.w(TunduCloud.TAG, "install failed (" + status + "): " + message);
        if (sha != null) cloud.state.recordInstallFailure(sha, "status " + status + ": " + message);
        cloud.updateState = "failed";
        cloud.readyApk = null;
        cloud.readyApp = null;
    }

    @SuppressWarnings("deprecation")
    private static Intent legacyIntentExtra(Intent intent) {
        return intent.getParcelableExtra(Intent.EXTRA_INTENT);
    }
}
