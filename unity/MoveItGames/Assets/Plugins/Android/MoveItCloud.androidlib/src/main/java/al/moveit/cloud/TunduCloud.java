package al.moveit.cloud;

import android.app.Activity;
import android.app.AlarmManager;
import android.app.PendingIntent;
import android.app.job.JobInfo;
import android.app.job.JobScheduler;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.SystemClock;
import android.provider.Settings;
import android.util.Log;

import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.Calendar;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/**
 * The box's link to the Tundu cloud: identity, Game Pass, gift card codes, app and firmware updates.
 * Unity calls the static methods (Assets/Games/Common/Cloud/TunduCloud.cs); the background job and receivers use
 * {@link #get(Context)}. None of it needs Unity: if the games crash at start, the job still fetches the fix.
 *
 * When updates install (never during a game: Unity reports that with setBusy):
 * - when Unity asks (installNow): in the first minute after the box starts, or after the menu sat idle a while;
 * - at night (02:00-05:00, box time) if the box is on and nobody plays;
 * - at once in safe mode: 3 starts in a row that never reached the home screen.
 */
public final class TunduCloud {
    static final String TAG = "TunduCloud";
    static final String API = "https://api.tundu.al";
    static final String CDN = "https://cdn.tundu.al";
    static final String DEFAULT_CHANNEL = "stable";
    static final int SAFE_MODE_AFTER_FAILED_STARTS = 3;
    static final long MIN_FREE_BYTES = 200L * 1024 * 1024;
    static final String ACTION_NIGHT = "al.moveit.cloud.NIGHT";
    static final int JOB_PERIODIC = 4711, JOB_NOW = 4712;

    private static TunduCloud instance;

    final Context app;
    final CloudState state;
    final CloudApi api;
    final String cdn;
    final int appVersion;
    final ExecutorService network = Executors.newSingleThreadExecutor();
    final ExecutorService files = Executors.newSingleThreadExecutor();
    final ScheduledExecutorService ticker = Executors.newSingleThreadScheduledExecutor();
    final KeyValueStore settings;

    volatile boolean busy;
    volatile boolean safeMode;
    private boolean started;
    volatile String updateState = "none"; // none, downloading, ready, installing, failed
    volatile float updateProgress;
    volatile String updateVersion = "";
    volatile File readyApk;
    volatile ChannelManifest.App readyApp;
    volatile String redeemState = "{\"state\":\"idle\"}";

    private TunduCloud(Context context) {
        app = context.getApplicationContext();
        settings = new PrefsStore(app, "moveit_cloud");
        PassClock clock = new PassClock(new PrefsStore(app, "moveit_cloud_clock"), new PassClock.Source() {
            @Override
            public long wallMillis() {
                return System.currentTimeMillis();
            }

            @Override
            public long elapsedMillis() {
                return SystemClock.elapsedRealtime();
            }

            @Override
            public long bootCount() {
                return Settings.Global.getInt(app.getContentResolver(), Settings.Global.BOOT_COUNT, -1);
            }
        });
        String channel = settings.getString("channel", DEFAULT_CHANNEL);
        state = new CloudState(settings, clock, TrustedKeys.RELEASE, TrustedKeys.PASS, channel, app.getPackageName());
        String apiBase = API, cdnBase = CDN;
        JSONObject dev = devOverrides(app);
        if (dev != null) {
            apiBase = dev.optString("api", API);
            cdnBase = dev.optString("cdn", CDN);
            Log.w(TAG, "debug build: using " + apiBase + " and " + cdnBase);
        }
        api = new CloudApi(apiBase);
        cdn = cdnBase;
        int version = 0;
        try {
            version = (int) app.getPackageManager().getPackageInfo(app.getPackageName(), 0).getLongVersionCode();
        } catch (PackageManager.NameNotFoundException ignored) {
            // can't happen for our own package
        }
        appVersion = version;
        // The clock floor (see PassClock) every 5 minutes while the process lives.
        ticker.scheduleWithFixedDelay(() -> state.clock().persistFloor(), 1, 5, TimeUnit.MINUTES);
    }

    public static synchronized TunduCloud get(Context context) {
        if (instance == null) instance = new TunduCloud(context);
        return instance;
    }

    /** Debug builds only: files/cloud-dev.json in the app's external files dir ({"api": "...", "cdn": "..."}). */
    private static JSONObject devOverrides(Context context) {
        if ((context.getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) == 0) return null;
        File dir = context.getExternalFilesDir(null);
        File f = dir == null ? null : new File(dir, "cloud-dev.json");
        if (f == null || !f.isFile()) return null;
        try (InputStream in = new java.io.FileInputStream(f)) {
            return new JSONObject(readAll(in, 4096));
        } catch (IOException | JSONException e) {
            return null;
        }
    }

    // =====================================================================================================
    // Unity API (static, called through AndroidJavaClass). Every method returns quickly; work runs in threads.
    // =====================================================================================================

    /** Called when the Unity activity starts; later calls in the same process do nothing. */
    public static void start(Activity activity) {
        TunduCloud c = get(activity);
        synchronized (c) {
            if (c.started) return;
            c.started = true;
        }
        long failedStarts = c.state.onLaunch() - 1;
        if (failedStarts >= SAFE_MODE_AFTER_FAILED_STARTS) {
            c.safeMode = true;
            c.state.queueEvent("safe_mode", failedStarts + " starts without reaching the home screen");
            Log.w(TAG, "safe mode: " + failedStarts + " failed starts, looking for a fix");
        }
        CrashReports.collect(c.app, c.state, c.settings);
        DeviceOwner.applyPolicies(c.app);
        scheduleJobs(c.app, false);
        // An update downloaded before this start is found (and re-verified) at once, so it can install while the
        // family is just sitting down; then the network check.
        c.files.execute(c::prepareUpdates);
        c.syncSoon();
    }

    /** {"active":true,"until":<s>,"now":<s>,"device":"d_…","blocked":false} */
    public static String passState() {
        TunduCloud c = instance;
        if (c == null) return "{\"active\":false,\"until\":0,\"now\":0,\"device\":\"\",\"blocked\":false}";
        long now = c.state.clock().now();
        String device = c.state.deviceId();
        return CloudApi.obj("active", c.state.passActive(), "until", c.state.passUntil(), "now", now,
                "device", device == null ? "" : device, "blocked", c.state.blocked()).toString();
    }

    /** The verified catalog: the manifest's "games" array, or [] (then the app's built-in defaults apply). */
    public static String catalog() {
        TunduCloud c = instance;
        ChannelManifest m = c == null ? null : c.state.manifest();
        return m == null ? "[]" : m.gamesJson;
    }

    /** Starts redeeming a code; poll redeemResult(). */
    public static void redeem(final String typed) {
        final TunduCloud c = instance;
        if (c == null) return;
        c.redeemState = "{\"state\":\"working\"}";
        c.network.execute(() -> {
            try {
                c.state.ensureEnrolled(c.api, Build.MODEL, Build.VERSION.SDK_INT, c.appVersion);
            } catch (IOException e) {
                Log.w(TAG, "enrol before redeem: " + e.getMessage());
            }
            CloudState.Redeemed r = c.state.redeem(c.api, typed);
            c.redeemState = CloudApi.obj("state", "done", "result", r.result, "days", r.days, "retry_after", r.retryAfter).toString();
            Log.i(TAG, "redeem: " + r.result);
        });
    }

    /** {"state":"idle"|"working"|"done","result":"ok",...}; reading a "done" result resets it to idle. */
    public static String redeemResult() {
        TunduCloud c = instance;
        if (c == null) return "{\"state\":\"idle\"}";
        String r = c.redeemState;
        if (r.contains("\"done\"")) c.redeemState = "{\"state\":\"idle\"}";
        return r;
    }

    /** True while a game runs: no update installs then. */
    public static void setBusy(boolean busy) {
        if (instance != null) instance.busy = busy;
    }

    /** {"state":"none"|"downloading"|"ready"|"installing"|"failed","progress":0.5,"version":"1.2.3","safe_mode":false} */
    public static String updateState() {
        TunduCloud c = instance;
        if (c == null) return "{\"state\":\"none\"}";
        return CloudApi.obj("state", c.updateState, "progress", (double) c.updateProgress, "version", c.updateVersion, "safe_mode", c.safeMode).toString();
    }

    /** Unity says now is a good moment (home screen just appeared, or the menu is idle). True if an install starts. */
    public static boolean installNow() {
        TunduCloud c = instance;
        if (c == null || c.busy || c.readyApk == null || !"ready".equals(c.updateState)) return false;
        c.installReady();
        return true;
    }

    /** The home screen is showing: this version works. */
    public static void markHealthy() {
        TunduCloud c = instance;
        if (c == null) return;
        c.safeMode = false;
        c.network.execute(() -> c.state.markHealthy(c.appVersion));
    }

    /** Settings > "Check for updates". */
    public static void checkNow() {
        if (instance != null) instance.syncSoon();
    }

    /** This app's versionCode (the catalog's minApp rules compare with it). */
    public static int appVersion() {
        return instance == null ? 0 : instance.appVersion;
    }

    public static String deviceId() {
        String id = instance == null ? null : instance.state.deviceId();
        return id == null ? "" : id;
    }

    public static String channel() {
        return instance == null ? DEFAULT_CHANNEL : instance.state.channel();
    }

    /** Hidden setting for test boxes ("beta"). Takes effect at the next app start. */
    public static void setChannel(String channel) {
        if (instance != null && channel != null && channel.matches("[a-z0-9-]{1,32}")) {
            instance.settings.put("channel", channel, CloudState.SERIAL, 0L);
            instance.settings.remove(CloudState.MANIFEST);
        }
    }

    // =====================================================================================================
    // Work
    // =====================================================================================================

    void syncSoon() {
        network.execute(this::sync);
    }

    /** Check-in, events, manifest, then download what's due. Runs on the network thread. Returns false to retry. */
    boolean sync() {
        boolean ok = true;
        try {
            state.ensureEnrolled(api, Build.MODEL, Build.VERSION.SDK_INT, appVersion);
            state.checkIn(api, appVersion, Build.VERSION.SDK_INT);
            state.flushEvents(api, appVersion);
        } catch (IOException e) {
            ok = false;
            Log.w(TAG, "check-in: " + e.getMessage());
        }
        try {
            String envelope = httpGet(cdn + "/channels/" + state.channel() + ".json?serial=" + System.currentTimeMillis() / 60000, 256 * 1024);
            if (!state.acceptManifest(envelope)) Log.w(TAG, "manifest refused (signature, channel or serial)");
        } catch (IOException e) {
            ok = false;
            Log.w(TAG, "manifest: " + e.getMessage());
        }
        files.execute(this::prepareUpdates);
        return ok;
    }

    /** Downloads a due app update (and firmware on device-owner boxes); installs it if the moment is right. */
    void prepareUpdates() {
        ChannelManifest.App update = state.pendingAppUpdate(appVersion, Build.VERSION.SDK_INT);
        if (update == null) {
            // Nothing due (or the rollout was paused): forget a downloaded file.
            if (!"installing".equals(updateState)) {
                updateState = "none";
                readyApk = null;
                readyApp = null;
                cleanup(new File(app.getFilesDir(), "updates"), null);
            }
        } else if (readyApk == null || readyApp == null || !readyApp.sha256.equals(update.sha256)) {
            updateVersion = update.versionName;
            updateState = "downloading";
            try {
                File dir = new File(app.getFilesDir(), "updates");
                cleanup(dir, update.sha256);
                File apk = Downloader.fetch(cdn + "/" + update.file, dir, update.sha256, update.size, "apk", MIN_FREE_BYTES,
                        (done, total) -> updateProgress = total > 0 ? (float) done / total : 0f);
                readyApk = apk;
                readyApp = update;
                updateState = "ready";
                Log.i(TAG, "update " + update.versionName + " downloaded and verified");
            } catch (IOException e) {
                updateState = "failed";
                Log.w(TAG, "download: " + e.getMessage());
            }
        }
        if ("ready".equals(updateState) && !busy && (safeMode || isNight())) installReady();
        FirmwareUpdater.maybeUpdate(this);
    }

    void installReady() {
        final File apk = readyApk;
        final ChannelManifest.App update = readyApp;
        if (apk == null || update == null) return;
        updateState = "installing";
        files.execute(() -> {
            try {
                Log.i(TAG, "installing " + update.versionName);
                Installer.install(app, apk, update);
                // Android now replaces the app and restarts it; InstallReceiver reports failures.
            } catch (IOException e) {
                Log.w(TAG, "install: " + e.getMessage());
                state.recordInstallFailure(update.sha256, e.getMessage());
                updateState = "failed";
                readyApk = null;
                readyApp = null;
            }
        });
    }

    /** Box-local night (02:00-05:00): the box's time zone is set at provisioning (Europe/Tirane). */
    static boolean isNight() {
        int hour = Calendar.getInstance().get(Calendar.HOUR_OF_DAY);
        return hour >= 2 && hour < 5;
    }

    /** Deletes files in dir except the one for keepSha (and files being downloaded for it). */
    static void cleanup(File dir, String keepSha) {
        File[] list = dir.listFiles();
        if (list == null) return;
        for (File f : list) if (keepSha == null || !f.getName().startsWith(keepSha)) f.delete();
    }

    static String httpGet(String url, int maxBytes) throws IOException {
        HttpURLConnection http = (HttpURLConnection) new URL(url).openConnection();
        http.setConnectTimeout(10_000);
        http.setReadTimeout(20_000);
        http.setInstanceFollowRedirects(false);
        http.setUseCaches(false);
        try {
            int code = http.getResponseCode();
            if (code != 200) throw new IOException("HTTP " + code);
            return readAll(http.getInputStream(), maxBytes);
        } finally {
            http.disconnect();
        }
    }

    static String readAll(InputStream input, int maxBytes) throws IOException {
        try (InputStream in = input) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int n;
            while ((n = in.read(buffer)) > 0) {
                if (out.size() + n > maxBytes) throw new IOException("too large");
                out.write(buffer, 0, n);
            }
            return out.toString("UTF-8");
        }
    }

    /** Periodic check every 6 hours (with network), optionally one right away, and a nightly alarm for installs. */
    static void scheduleJobs(Context context, boolean runNow) {
        JobScheduler js = context.getSystemService(JobScheduler.class);
        ComponentName service = new ComponentName(context, UpdateJobService.class);
        if (js.getPendingJob(JOB_PERIODIC) == null) {
            js.schedule(new JobInfo.Builder(JOB_PERIODIC, service)
                    .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                    .setPeriodic(TimeUnit.HOURS.toMillis(6), TimeUnit.HOURS.toMillis(1))
                    .setPersisted(true)
                    .build());
        }
        if (runNow) runJobNow(context);
        AlarmManager alarms = context.getSystemService(AlarmManager.class);
        Intent intent = new Intent(context, BootReceiver.class).setAction(ACTION_NIGHT);
        PendingIntent night = PendingIntent.getBroadcast(context, 0, intent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Calendar at = Calendar.getInstance();
        at.set(Calendar.HOUR_OF_DAY, 2);
        at.set(Calendar.MINUTE, 30);
        at.set(Calendar.SECOND, 0);
        if (at.getTimeInMillis() < System.currentTimeMillis()) at.add(Calendar.DAY_OF_MONTH, 1);
        // Inexact window 02:30-04:30: needs no exact-alarm permission.
        alarms.setWindow(AlarmManager.RTC_WAKEUP, at.getTimeInMillis(), TimeUnit.HOURS.toMillis(2), night);
    }

    static void runJobNow(Context context) {
        JobScheduler js = context.getSystemService(JobScheduler.class);
        js.schedule(new JobInfo.Builder(JOB_NOW, new ComponentName(context, UpdateJobService.class))
                .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                .setOverrideDeadline(TimeUnit.MINUTES.toMillis(10))
                .build());
    }

    static Uri fileUri(File f) {
        return Uri.fromFile(f);
    }
}
