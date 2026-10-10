package al.moveit.cloud;

import android.app.admin.DevicePolicyManager;
import android.os.Build;
import android.util.Log;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.IOException;
import java.util.concurrent.Executors;

/**
 * Firmware (Android system) updates, for boxes where the app is device owner (Android 10+). The image is the box
 * maker's signed OTA file: the system verifies that signature itself, we only choose when and which one. A
 * manifest entry names the exact Build.MODEL and the Build.FINGERPRINTs it applies to, so a box never gets an image
 * meant for another board. Installs only at night with nobody playing; the box reboots to finish. After the
 * reboot the fingerprint tells whether it worked (A/B boxes fall back to the old system by themselves if not).
 * Needs a real box and the ODM's OTA images to test: not run yet.
 */
final class FirmwareUpdater {
    static final String PENDING = "firmware.pending";

    private FirmwareUpdater() {}

    /** Runs on the files thread after the manifest check. */
    static void maybeUpdate(TunduCloud cloud) {
        if (Build.VERSION.SDK_INT < 29 || !DeviceOwner.isOwner(cloud.app)) return;
        ChannelManifest m = cloud.state.manifest();
        String device = cloud.state.deviceId();
        if (m == null || device == null) return;
        ChannelManifest.Firmware fw = m.firmwareFor(Build.MODEL, Build.FINGERPRINT, device, cloud.state.clock().now());
        if (fw == null || !cloud.state.installAllowed(fw.sha256)) return;
        File file;
        try {
            File dir = new File(cloud.app.getFilesDir(), "ota");
            TunduCloud.cleanup(dir, fw.sha256);
            file = Downloader.fetch(cloud.cdn + "/" + fw.file, dir, fw.sha256, fw.size, "zip", 500L * 1024 * 1024, null);
        } catch (IOException e) {
            Log.w(TunduCloud.TAG, "firmware download: " + e.getMessage());
            return;
        }
        if (cloud.busy || !TunduCloud.isNight()) return; // downloaded; installs on a later night
        cloud.settings.put(PENDING, CloudApi.obj("sha256", fw.sha256, "to", fw.to, "from", Build.FINGERPRINT).toString());
        DevicePolicyManager dpm = cloud.app.getSystemService(DevicePolicyManager.class);
        Log.i(TunduCloud.TAG, "installing firmware " + fw.to);
        dpm.installSystemUpdate(DeviceOwner.admin(cloud.app), TunduCloud.fileUri(file), Executors.newSingleThreadExecutor(),
                new DevicePolicyManager.InstallSystemUpdateCallback() {
                    @Override
                    public void onInstallUpdateError(int errorCode, String errorMessage) {
                        Log.w(TunduCloud.TAG, "firmware install error " + errorCode + ": " + errorMessage);
                        cloud.settings.remove(PENDING);
                        cloud.state.recordInstallFailure(fw.sha256, "firmware error " + errorCode + ": " + errorMessage);
                        cloud.state.queueEvent("firmware_failed", "error " + errorCode);
                    }
                });
    }

    /** After a reboot: did the pending firmware install take? */
    static void checkResultAfterBoot(TunduCloud cloud) {
        String pending = cloud.settings.getString(PENDING, null);
        if (pending == null) return;
        cloud.settings.remove(PENDING);
        try {
            JSONObject p = new JSONObject(pending);
            if (Build.FINGERPRINT.equals(p.getString("to"))) {
                cloud.state.queueEvent("firmware_installed", p.getString("to"));
                TunduCloud.cleanup(new File(cloud.app.getFilesDir(), "ota"), null);
            } else {
                cloud.state.recordInstallFailure(p.getString("sha256"), "still " + Build.FINGERPRINT);
                cloud.state.queueEvent("firmware_failed", "still " + Build.FINGERPRINT);
            }
        } catch (JSONException ignored) {
            // corrupt record: nothing to report
        }
    }
}
