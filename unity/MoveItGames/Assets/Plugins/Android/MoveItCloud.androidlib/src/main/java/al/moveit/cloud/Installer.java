package al.moveit.cloud;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageInstaller;
import android.content.pm.PackageManager;
import android.content.pm.Signature;
import android.content.pm.SigningInfo;
import android.os.Build;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.security.MessageDigest;

/**
 * Installs a downloaded, verified update of this app with PackageInstaller.
 *
 * Before anything is written: the file must be an APK of this package, with exactly the manifest's versionCode,
 * signed by the same certificate as the installed app (and the manifest's, and the pinned one when set).
 * While writing into the install session the bytes are hashed again; if they no longer match the manifest (the
 * file changed after it was checked) the session is abandoned. Android then checks the signature itself and
 * refuses downgrades. The install is atomic: the box has either the old version or the new one, never half.
 *
 * Silent (no prompt) when the app is device owner, a privileged system app, or (Android 12+) updating itself.
 * Otherwise Android shows its install dialog, which InstallReceiver opens.
 */
final class Installer {
    static final String ACTION_RESULT = "al.moveit.cloud.INSTALL_RESULT";
    static final String EXTRA_SHA256 = "al.moveit.cloud.sha256";
    static final String EXTRA_VERSION = "al.moveit.cloud.version";

    private Installer() {}

    static void install(Context context, File apk, ChannelManifest.App app) throws IOException {
        PackageManager pm = context.getPackageManager();
        String pkg = context.getPackageName();
        PackageInfo archive = pm.getPackageArchiveInfo(apk.getPath(), PackageManager.GET_SIGNING_CERTIFICATES);
        if (archive == null) throw new IOException("not an APK");
        if (!pkg.equals(archive.packageName)) throw new IOException("APK is for " + archive.packageName);
        if (archive.getLongVersionCode() != app.versionCode) throw new IOException("APK versionCode " + archive.getLongVersionCode() + " != manifest");
        String apkSigner = signer(archive.signingInfo);
        String ownSigner;
        try {
            ownSigner = signer(pm.getPackageInfo(pkg, PackageManager.GET_SIGNING_CERTIFICATES).signingInfo);
        } catch (PackageManager.NameNotFoundException e) {
            throw new IOException("own package not found");
        }
        // signingInfo can be missing when an archive is parsed on some builds; Android still checks at install.
        if (apkSigner != null && !apkSigner.equals(app.signer)) throw new IOException("APK signer differs from the manifest");
        if (apkSigner != null && ownSigner != null && !apkSigner.equals(ownSigner)) throw new IOException("APK signer differs from this app's");
        if (TrustedKeys.APK_SIGNER != null && ownSigner != null && !TrustedKeys.APK_SIGNER.equals(ownSigner)) {
            throw new IOException("this build is not signed with the release key");
        }

        PackageInstaller installer = pm.getPackageInstaller();
        PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
        params.setAppPackageName(pkg);
        params.setSize(app.size);
        if (Build.VERSION.SDK_INT >= 31) params.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED);
        if (Build.VERSION.SDK_INT >= 33) params.setPackageSource(PackageInstaller.PACKAGE_SOURCE_DOWNLOADED_FILE);
        int id = installer.createSession(params);
        boolean committed = false;
        try (PackageInstaller.Session session = installer.openSession(id)) {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            try (InputStream in = new FileInputStream(apk); OutputStream out = session.openWrite("base.apk", 0, app.size)) {
                byte[] buffer = new byte[64 * 1024];
                int n;
                while ((n = in.read(buffer)) > 0) {
                    digest.update(buffer, 0, n);
                    out.write(buffer, 0, n);
                }
                session.fsync(out);
            }
            if (!Downloader.hex(digest.digest()).equals(app.sha256)) throw new IOException("APK changed after it was verified");
            Intent intent = new Intent(context, InstallReceiver.class)
                    .setAction(ACTION_RESULT)
                    .putExtra(EXTRA_SHA256, app.sha256)
                    .putExtra(EXTRA_VERSION, app.versionCode);
            // Mutable: the installer adds the status extras. The intent is explicit (our own receiver).
            int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0);
            PendingIntent pending = PendingIntent.getBroadcast(context, id, intent, flags);
            session.commit(pending.getIntentSender());
            committed = true;
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IOException(e);
        } finally {
            if (!committed) {
                try {
                    installer.abandonSession(id);
                } catch (RuntimeException ignored) {
                    // already gone
                }
            }
        }
    }

    /** SHA-256 hex of the current signing certificate ("multiple" if several), or null if unknown. */
    static String signer(SigningInfo info) {
        if (info == null) return null;
        Signature[] signers = info.getApkContentsSigners(); // current certificate(s), not rotated-away ones
        if (signers == null || signers.length == 0) return null;
        if (signers.length != 1) return "multiple";
        try {
            return Downloader.hex(MessageDigest.getInstance("SHA-256").digest(signers[0].toByteArray()));
        } catch (java.security.NoSuchAlgorithmException e) {
            return null;
        }
    }
}
