package al.moveit.cloud;

import android.app.admin.DeviceAdminReceiver;

/**
 * Device-admin component. Inactive unless provisioning makes the app device owner:
 *   adb shell dpm set-device-owner al.tundu.games/al.moveit.cloud.AdminReceiver
 * (fresh box, no accounts; tools/provision/provision.ps1 does it). Then app updates install without a prompt and
 * firmware images can be applied (DeviceOwner, FirmwareUpdater).
 */
public final class AdminReceiver extends DeviceAdminReceiver {
}
