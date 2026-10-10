package al.moveit.cloud;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.IOException;
import java.util.Map;
import java.util.UUID;

/**
 * Everything the box remembers about the cloud, and the rules for changing it. No Android classes, so all of it
 * is tested on a PC (src/test). TunduCloud (the Android side) only wires it to SharedPreferences, the network and
 * the installer.
 *
 * Rules worth knowing:
 * - Identity: made once (enrolment). Only after 3 check-ins in a row are refused as "unauthorized" does the box
 *   enrol again (a server bug must not wipe every box), and it keeps its old token: time already paid for keeps
 *   working until it ends, and support can move it with `device transfer`.
 * - Tokens: only verified ones, only for this device, never older than the one stored (no replay of a token with
 *   more days). Only a token that just arrived from the server sets the trusted clock.
 * - Manifests: only verified ones for our channel, never a lower serial than seen before (rollback). An expired
 *   manifest still names the games but installs nothing (freeze).
 * - Codes: the check character is checked here first (a typo costs no network call and no "wrong code" strike);
 *   the request id stays the same until an answer arrives, so retrying after a lost answer can't double-count.
 */
public final class CloudState {
    /** The server calls, so tests can fake them (CloudApi on the box). */
    public interface Api {
        CloudApi.Enrollment enroll(String model, int sdk, int app) throws IOException;

        String status(String device, String secret, int app, int sdk) throws IOException;

        CloudApi.RedeemAnswer redeem(String device, String secret, String formattedCode, String requestId) throws IOException;

        void events(String device, String secret, int app, JSONArray events) throws IOException;
    }

    static final String DEVICE = "id.device", SECRET = "id.secret", UNAUTHORIZED = "id.unauthorized";
    static final String OLD_DEVICE = "id.old_device", OLD_PASS = "pass.old";
    static final String PASS = "pass.token", MANIFEST = "manifest.doc", SERIAL = "manifest.serial";
    static final String EVENTS = "events.queue", PENDING_REDEEM = "redeem.pending";
    static final String LAUNCHES = "health.launches", HEALTHY_VERSION = "health.version";
    static final String FAILED_INSTALLS = "install.failed";
    static final int MAX_EVENTS = 50;
    static final int REENROLL_AFTER_UNAUTHORIZED = 3;
    static final int MAX_INSTALL_FAILURES = 3;

    private final KeyValueStore store;
    private final PassClock clock;
    // Last verified documents: Unity asks every second, no need to check the same signature again.
    private String verifiedPassEnvelope, verifiedPassDevice;
    private PassToken verifiedPass;
    private String verifiedManifestEnvelope;
    private ChannelManifest verifiedManifest;
    private final Map<String, String> releaseKeys, passKeys;
    private final String channel, packageName;

    public CloudState(KeyValueStore store, PassClock clock, Map<String, String> releaseKeys, Map<String, String> passKeys, String channel, String packageName) {
        this.store = store;
        this.clock = clock;
        this.releaseKeys = releaseKeys;
        this.passKeys = passKeys;
        this.channel = channel;
        this.packageName = packageName;
    }

    public PassClock clock() {
        return clock;
    }

    public String channel() {
        return channel;
    }

    public synchronized String deviceId() {
        return store.getString(DEVICE, null);
    }

    // ---------- identity and check-in ----------

    /** Enrols if this box has no identity yet. Returns true if it has one now. */
    public synchronized boolean ensureEnrolled(Api api, String model, int sdk, int app) throws IOException {
        if (deviceId() != null) return true;
        CloudApi.Enrollment e = api.enroll(model, sdk, app);
        store.put(DEVICE, e.device, SECRET, e.secret, UNAUTHORIZED, 0L);
        store.remove(PASS);
        acceptPass(e.passEnvelope, true);
        return true;
    }

    /** Check-in: a fresh token. Handles a refused identity as described above. */
    public synchronized void checkIn(Api api, int app, int sdk) throws IOException {
        String device = deviceId();
        if (device == null) return;
        try {
            String envelope = api.status(device, store.getString(SECRET, ""), app, sdk);
            store.put(UNAUTHORIZED, 0L);
            acceptPass(envelope, true);
        } catch (CloudApi.HttpError e) {
            if (e.status != 401) throw e;
            long count = store.getLong(UNAUTHORIZED, 0) + 1;
            if (count < REENROLL_AFTER_UNAUTHORIZED) {
                store.put(UNAUTHORIZED, count);
                throw e;
            }
            // Give up this identity, but keep its token so paid time isn't lost.
            String oldPass = store.getString(PASS, null);
            store.put(OLD_DEVICE, device, OLD_PASS, oldPass == null ? "" : oldPass);
            store.remove(DEVICE, SECRET, PASS, UNAUTHORIZED);
            throw e;
        }
    }

    // ---------- Game Pass ----------

    /** Stores a token if it verifies, is for this device and isn't older than the stored one. */
    public synchronized boolean acceptPass(String envelope, boolean fresh) {
        String device = deviceId();
        if (envelope == null || device == null) return false;
        PassToken token;
        token = parsePass(envelope, device);
        if (token == null) return false;
        PassToken current = storedPass();
        if (current != null && token.serverNow < current.serverNow) return false;
        store.put(PASS, envelope);
        if (fresh) clock.onServerTime(token.serverNow);
        return true;
    }

    /** The stored token, verified (null if none or not valid). */
    public synchronized PassToken storedPass() {
        String device = deviceId();
        String envelope = store.getString(PASS, null);
        if (device == null || envelope == null) return null;
        return parsePass(envelope, device);
    }

    private PassToken oldPass() {
        String device = store.getString(OLD_DEVICE, null);
        String envelope = store.getString(OLD_PASS, "");
        if (device == null || envelope.isEmpty()) return null;
        try {
            return PassToken.parse(envelope, passKeys, device);
        } catch (SignedDoc.Invalid e) {
            return null;
        }
    }

    private PassToken parsePass(String envelope, String device) {
        if (envelope.equals(verifiedPassEnvelope) && device.equals(verifiedPassDevice)) return verifiedPass;
        try {
            PassToken token = PassToken.parse(envelope, passKeys, device);
            verifiedPassEnvelope = envelope;
            verifiedPassDevice = device;
            verifiedPass = token;
            return token;
        } catch (SignedDoc.Invalid e) {
            return null;
        }
    }

    /** Game Pass end (unix seconds) the box honours now: its own token, or an older identity's until that ends. */
    public synchronized long passUntil() {
        long now = clock.now();
        PassToken p = storedPass();
        PassToken old = oldPass();
        long until = p != null && p.activeAt(now) ? p.passUntil : 0;
        if (old != null && old.activeAt(now)) until = Math.max(until, old.passUntil);
        return until;
    }

    public synchronized boolean passActive() {
        return passUntil() > clock.now();
    }

    public synchronized boolean blocked() {
        PassToken p = storedPass();
        return p != null && p.blocked;
    }

    // ---------- gift card codes ----------

    /** Redeem result for the UI: the server's result, or check_code, network, locked, slow_down, blocked, no_identity. */
    public static final class Redeemed {
        public final String result;
        public final int days;
        public final long retryAfter;

        Redeemed(String result, int days, long retryAfter) {
            this.result = result;
            this.days = days;
            this.retryAfter = retryAfter;
        }
    }

    public synchronized Redeemed redeem(Api api, String typed) {
        String code = GiftCode.normalize(typed);
        if (code == null) return new Redeemed("check_code", 0, 0);
        String device = deviceId();
        if (device == null) return new Redeemed("no_identity", 0, 0);
        String request = pendingRequestFor(code);
        try {
            CloudApi.RedeemAnswer a = api.redeem(device, store.getString(SECRET, ""), GiftCode.format(code), request);
            store.remove(PENDING_REDEEM);
            acceptPass(a.passEnvelope, true);
            return new Redeemed(a.result, a.days, 0);
        } catch (CloudApi.HttpError e) {
            if (e.status == 429) return new Redeemed(e.error.equals("locked") ? "locked" : "slow_down", 0, e.retryAfter);
            if (e.status == 403) return new Redeemed("blocked", 0, 0);
            return new Redeemed("network", 0, 0);
        } catch (IOException e) {
            return new Redeemed("network", 0, 0); // the request id is kept: a retry is safe
        }
    }

    private String pendingRequestFor(String code) {
        try {
            JSONObject pending = new JSONObject(store.getString(PENDING_REDEEM, "{}"));
            if (code.equals(pending.optString("code"))) return pending.getString("request");
        } catch (JSONException ignored) {
            // corrupt: start a new request
        }
        String request = UUID.randomUUID().toString();
        store.put(PENDING_REDEEM, CloudApi.obj("code", code, "request", request).toString());
        return request;
    }

    // ---------- channel manifest ----------

    /** Stores a manifest if it verifies for our channel and its serial isn't lower than seen before. */
    public synchronized boolean acceptManifest(String envelope) {
        ChannelManifest m;
        try {
            m = ChannelManifest.parse(envelope, releaseKeys, channel);
        } catch (SignedDoc.Invalid e) {
            return false;
        }
        if (m.serial < store.getLong(SERIAL, 0)) return false;
        store.put(MANIFEST, envelope, SERIAL, m.serial);
        return true;
    }

    public synchronized ChannelManifest manifest() {
        String envelope = store.getString(MANIFEST, null);
        if (envelope == null) return null;
        if (envelope.equals(verifiedManifestEnvelope)) return verifiedManifest;
        try {
            verifiedManifest = ChannelManifest.parse(envelope, releaseKeys, channel);
            verifiedManifestEnvelope = envelope;
            return verifiedManifest;
        } catch (SignedDoc.Invalid e) {
            return null;
        }
    }

    public synchronized ChannelManifest.App pendingAppUpdate(int installedVersion, int sdk) {
        ChannelManifest m = manifest();
        String device = deviceId();
        if (m == null || device == null) return null;
        ChannelManifest.App app = m.appUpdateFor(packageName, installedVersion, sdk, device, clock.now());
        return app != null && installAllowed(app.sha256) ? app : null;
    }

    // ---------- install bookkeeping ----------

    /** A file that failed to install 3 times is not tried again (a broken release must not loop downloads). */
    public synchronized boolean installAllowed(String sha256) {
        try {
            return new JSONObject(store.getString(FAILED_INSTALLS, "{}")).optInt(sha256, 0) < MAX_INSTALL_FAILURES;
        } catch (JSONException e) {
            return true;
        }
    }

    public synchronized void recordInstallFailure(String sha256, String detail) {
        try {
            JSONObject failed = new JSONObject(store.getString(FAILED_INSTALLS, "{}"));
            failed.put(sha256, failed.optInt(sha256, 0) + 1);
            store.put(FAILED_INSTALLS, failed.toString());
        } catch (JSONException ignored) {
            store.put(FAILED_INSTALLS, "{}");
        }
        queueEvent("update_failed", detail);
    }

    // ---------- health ----------

    /** Called at every app start; returns how many starts in a row haven't reached the home screen. */
    public synchronized long onLaunch() {
        long launches = store.getLong(LAUNCHES, 0) + 1;
        store.put(LAUNCHES, launches);
        return launches;
    }

    /** The home screen is up: the app works. Reports boot_ok once per version (that's how a rollout is judged). */
    public synchronized void markHealthy(int appVersion) {
        store.put(LAUNCHES, 0L);
        if (store.getLong(HEALTHY_VERSION, -1) != appVersion) {
            store.put(HEALTHY_VERSION, (long) appVersion);
            queueEvent("boot_ok", null);
        }
    }

    public synchronized void queueEvent(String kind, String detail) {
        try {
            JSONArray queue = new JSONArray(store.getString(EVENTS, "[]"));
            JSONObject e = CloudApi.obj("kind", kind, "at", clock.now());
            if (detail != null) e.put("detail", detail.length() > 300 ? detail.substring(0, 300) : detail);
            queue.put(e);
            JSONArray trimmed = new JSONArray();
            for (int i = Math.max(0, queue.length() - MAX_EVENTS); i < queue.length(); i++) trimmed.put(queue.get(i));
            store.put(EVENTS, trimmed.toString());
        } catch (JSONException e) {
            store.put(EVENTS, "[]");
        }
    }

    /** Sends queued events (20 per request); keeps them if sending fails. */
    public synchronized void flushEvents(Api api, int app) throws IOException {
        String device = deviceId();
        if (device == null) return;
        JSONArray queue;
        try {
            queue = new JSONArray(store.getString(EVENTS, "[]"));
        } catch (JSONException e) {
            store.put(EVENTS, "[]");
            return;
        }
        try {
            while (queue.length() > 0) {
                JSONArray batch = new JSONArray();
                JSONArray rest = new JSONArray();
                for (int i = 0; i < queue.length(); i++) (i < 20 ? batch : rest).put(queue.get(i));
                try {
                    api.events(device, store.getString(SECRET, ""), app, batch);
                } catch (CloudApi.HttpError e) {
                    if (e.status != 400) throw e; // refused as invalid: drop it rather than retry forever
                }
                queue = rest;
                store.put(EVENTS, queue.toString());
            }
        } catch (JSONException e) {
            store.put(EVENTS, "[]");
        }
    }
}
