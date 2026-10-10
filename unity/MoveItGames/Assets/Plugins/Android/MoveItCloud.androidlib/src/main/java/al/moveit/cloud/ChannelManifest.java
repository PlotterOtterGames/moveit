package al.moveit.cloud;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.regex.Pattern;

/**
 * A verified channel manifest (server/cli/channel.ts): app release, game catalog, firmware. Parsing applies the
 * same rules as validateChannel in the CLI; a manifest that breaks any rule is refused as a whole.
 * Rollback, freeze and rollout decisions are here too, so they can be tested without a device.
 */
public final class ChannelManifest {
    public static final String TYPE = "tundu.channel";
    static final long MAX_LIFETIME_SECONDS = 400L * 86400;

    private static final Pattern HEX64 = Pattern.compile("[0-9a-f]{64}");
    private static final Pattern PACKAGE = Pattern.compile("[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+");
    private static final Pattern GAME_ID = Pattern.compile("[a-z0-9_]{1,32}");
    private static final Pattern CHANNEL = Pattern.compile("[a-z0-9-]{1,32}");
    private static final Pattern VERSION_NAME = Pattern.compile("[0-9A-Za-z.\\-+]{1,32}");
    private static final Pattern PLAIN = Pattern.compile("[\\x20-\\x7e]{1,200}");

    public static final class App {
        public String packageName, versionName, file, sha256, signer;
        public int versionCode, minSdk, rollout, required;
        public long size;
    }

    public static final class Firmware {
        public String model, to, file, sha256;
        public List<String> from;
        public long size;
        public int rollout;
    }

    public final String channel;
    public final long serial, issued, expires;
    public final App app;
    /** The "games" array as JSON, handed to Unity (Games/Common/GameAccess.cs applies it). */
    public final String gamesJson;
    public final List<Firmware> firmware;
    /** The signed envelope, kept for storage and re-verification. */
    public final String envelope;

    private ChannelManifest(String channel, long serial, long issued, long expires, App app, String gamesJson, List<Firmware> firmware, String envelope) {
        this.channel = channel;
        this.serial = serial;
        this.issued = issued;
        this.expires = expires;
        this.app = app;
        this.gamesJson = gamesJson;
        this.firmware = firmware;
        this.envelope = envelope;
    }

    /** Verifies with the release keys (never the pass keys) and validates every field. */
    public static ChannelManifest parse(String envelope, Map<String, String> releaseKeys, String expectedChannel) throws SignedDoc.Invalid {
        JSONObject c = SignedDoc.verify(envelope, releaseKeys, TYPE);
        try {
            String channel = c.getString("channel");
            if (!CHANNEL.matcher(channel).matches() || !channel.equals(expectedChannel)) throw new SignedDoc.Invalid("wrong_channel");
            long serial = longIn(c, "serial", 1, 1L << 31);
            long issued = longIn(c, "issued", 1_600_000_000L, 4_000_000_000L);
            long expires = longIn(c, "expires", issued + 1, issued + MAX_LIFETIME_SECONDS);

            App app = null;
            if (!c.isNull("app")) {
                JSONObject a = c.getJSONObject("app");
                app = new App();
                app.packageName = match(a, "package", PACKAGE);
                app.versionCode = (int) longIn(a, "versionCode", 1, 2_100_000_000L);
                app.versionName = match(a, "versionName", VERSION_NAME);
                app.sha256 = match(a, "sha256", HEX64);
                app.file = a.getString("file");
                if (!app.file.equals("apk/" + app.sha256 + ".apk")) throw new SignedDoc.Invalid("malformed");
                app.size = longIn(a, "size", 1, 1L << 31);
                app.signer = match(a, "signer", HEX64);
                app.minSdk = (int) longIn(a, "minSdk", 1, 1000);
                app.rollout = (int) longIn(a, "rollout", 0, 100);
                app.required = (int) longIn(a, "required", 0, app.versionCode);
            }

            JSONArray games = c.getJSONArray("games");
            if (games.length() > 200) throw new SignedDoc.Invalid("malformed");
            Set<String> ids = new HashSet<>();
            for (int i = 0; i < games.length(); i++) {
                JSONObject g = games.getJSONObject(i);
                String id = match(g, "id", GAME_ID);
                if (!ids.add(id)) throw new SignedDoc.Invalid("malformed");
                String tier = g.getString("tier");
                if (!tier.equals("free") && !tier.equals("pass")) throw new SignedDoc.Invalid("malformed");
                if (g.has("minApp")) longIn(g, "minApp", 0, 2_100_000_000L);
                if (g.has("hidden") && !(g.get("hidden") instanceof Boolean)) throw new SignedDoc.Invalid("malformed");
                if (g.has("from")) longIn(g, "from", 0, 4_000_000_000L);
            }

            JSONArray fws = c.getJSONArray("firmware");
            if (fws.length() > 50) throw new SignedDoc.Invalid("malformed");
            List<Firmware> firmware = new ArrayList<>();
            for (int i = 0; i < fws.length(); i++) {
                JSONObject f = fws.getJSONObject(i);
                Firmware fw = new Firmware();
                fw.model = match(f, "model", PLAIN);
                fw.to = match(f, "to", PLAIN);
                JSONArray from = f.getJSONArray("from");
                if (from.length() < 1 || from.length() > 20) throw new SignedDoc.Invalid("malformed");
                fw.from = new ArrayList<>();
                for (int j = 0; j < from.length(); j++) {
                    String fp = from.getString(j);
                    if (!PLAIN.matcher(fp).matches() || fp.equals(fw.to)) throw new SignedDoc.Invalid("malformed");
                    fw.from.add(fp);
                }
                fw.sha256 = match(f, "sha256", HEX64);
                fw.file = f.getString("file");
                if (!fw.file.equals("ota/" + fw.sha256 + ".zip")) throw new SignedDoc.Invalid("malformed");
                fw.size = longIn(f, "size", 1, 1L << 33);
                fw.rollout = (int) longIn(f, "rollout", 0, 100);
                firmware.add(fw);
            }
            return new ChannelManifest(channel, serial, issued, expires, app, games.toString(), Collections.unmodifiableList(firmware), envelope);
        } catch (JSONException e) {
            throw new SignedDoc.Invalid("malformed");
        }
    }

    private static long longIn(JSONObject o, String key, long min, long max) throws JSONException, SignedDoc.Invalid {
        Object v = o.get(key);
        if (!(v instanceof Integer || v instanceof Long)) throw new SignedDoc.Invalid("malformed");
        long n = ((Number) v).longValue();
        if (n < min || n > max) throw new SignedDoc.Invalid("malformed");
        return n;
    }

    private static String match(JSONObject o, String key, Pattern pattern) throws JSONException, SignedDoc.Invalid {
        String v = o.getString(key);
        if (!pattern.matcher(v).matches()) throw new SignedDoc.Invalid("malformed");
        return v;
    }

    public boolean expiredAt(long trustedNow) {
        return trustedNow >= expires;
    }

    /**
     * The app update this box should install, or null: a newer versionCode of this very package, for this Android
     * version, from an unexpired manifest, with the box inside the rollout. (Android itself also refuses
     * downgrades and APKs signed by another key.)
     */
    public App appUpdateFor(String packageName, int installedVersion, int sdk, String deviceId, long trustedNow) {
        if (app == null || expiredAt(trustedNow)) return null;
        if (!app.packageName.equals(packageName) || app.versionCode <= installedVersion || sdk < app.minSdk) return null;
        boolean required = installedVersion < app.required;
        if (!required && rolloutBucket(deviceId, Integer.toString(app.versionCode)) >= app.rollout) return null;
        return app;
    }

    /** The firmware image for exactly this model and current build, or null. */
    public Firmware firmwareFor(String model, String fingerprint, String deviceId, long trustedNow) {
        if (expiredAt(trustedNow)) return null;
        for (Firmware f : firmware) {
            if (f.model.equals(model) && f.from.contains(fingerprint) && rolloutBucket(deviceId, f.to) < f.rollout) return f;
        }
        return null;
    }

    /** bucket = (first 4 bytes of SHA-256("<device id>:<salt>") as unsigned big-endian) mod 100, as in the CLI. */
    public static int rolloutBucket(String deviceId, String salt) {
        try {
            byte[] d = MessageDigest.getInstance("SHA-256").digest((deviceId + ":" + salt).getBytes(StandardCharsets.UTF_8));
            long v = ((d[0] & 0xffL) << 24) | ((d[1] & 0xffL) << 16) | ((d[2] & 0xffL) << 8) | (d[3] & 0xffL);
            return (int) (v % 100);
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
