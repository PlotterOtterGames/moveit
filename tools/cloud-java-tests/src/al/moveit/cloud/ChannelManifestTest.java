package al.moveit.cloud;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.fail;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

import java.util.function.Consumer;

public class ChannelManifestTest {
    static final String SHA = "ab".repeat(32);
    static final long NOW = 1_760_000_000L;
    private final TestSupport.Key release = new TestSupport.Key("r1");

    static JSONObject manifest() {
        try {
            JSONObject app = TestSupport.obj("package", "al.tundu.games", "versionCode", 10203, "versionName", "1.2.3", "file", "apk/" + SHA + ".apk",
                    "sha256", SHA, "size", 1000, "signer", "cd".repeat(32), "minSdk", 28, "rollout", 100, "required", 0);
            JSONArray games = new JSONArray().put(TestSupport.obj("id", "runner", "tier", "free")).put(TestSupport.obj("id", "fly", "tier", "pass", "minApp", 10203));
            JSONObject fw = TestSupport.obj("model", "TX66", "from", new JSONArray().put("old/fp:11/A/1:user/release-keys"), "to", "new/fp:11/A/2:user/release-keys",
                    "file", "ota/" + SHA + ".zip", "sha256", SHA, "size", 5000, "rollout", 100);
            return TestSupport.obj("type", "tundu.channel", "v", 1, "channel", "stable", "serial", 5, "issued", NOW, "expires", NOW + 180 * 86400L,
                    "app", app, "games", games, "firmware", new JSONArray().put(fw));
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    ChannelManifest parse(JSONObject m) throws SignedDoc.Invalid {
        return ChannelManifest.parse(release.sign(m), release.asMap(), "stable");
    }

    @Test
    public void parsesAGoodManifest() throws Exception {
        ChannelManifest m = parse(manifest());
        assertEquals(5, m.serial);
        assertEquals("1.2.3", m.app.versionName);
        assertEquals("[{\"id\":\"runner\",\"tier\":\"free\"},{\"id\":\"fly\",\"tier\":\"pass\",\"minApp\":10203}]", m.gamesJson);
        assertEquals(1, m.firmware.size());
    }

    @Test
    public void refusesEveryBrokenRule() throws Exception {
        Object[][] cases = {
            {"other channel", (Consumer<JSONObject>) m -> put(m, "channel", "beta")},
            {"serial 0", (Consumer<JSONObject>) m -> put(m, "serial", 0)},
            {"expires before issued", (Consumer<JSONObject>) m -> put(m, "expires", NOW)},
            {"lifetime over 400 days", (Consumer<JSONObject>) m -> put(m, "expires", NOW + 401 * 86400L)},
            {"fractional serial", (Consumer<JSONObject>) m -> put(m, "serial", 5.5)},
            {"file not named by hash", (Consumer<JSONObject>) m -> put(app(m), "file", "apk/latest.apk")},
            {"bad hash", (Consumer<JSONObject>) m -> put(app(m), "sha256", "XYZ")},
            {"rollout 101", (Consumer<JSONObject>) m -> put(app(m), "rollout", 101)},
            {"required above version", (Consumer<JSONObject>) m -> put(app(m), "required", 99999)},
            {"duplicate game", (Consumer<JSONObject>) m -> games(m).put(TestSupport.obj("id", "runner", "tier", "pass"))},
            {"unknown tier", (Consumer<JSONObject>) m -> games(m).put(TestSupport.obj("id", "x", "tier", "premium"))},
            {"firmware to in from", (Consumer<JSONObject>) m -> put(fw(m), "to", "old/fp:11/A/1:user/release-keys")},
            {"missing games", (Consumer<JSONObject>) m -> m.remove("games")},
        };
        for (Object[] c : cases) {
            JSONObject m = manifest();
            @SuppressWarnings("unchecked")
            Consumer<JSONObject> change = (Consumer<JSONObject>) c[1];
            change.accept(m);
            try {
                parse(m);
                fail("accepted: " + c[0]);
            } catch (SignedDoc.Invalid expected) {
                // refused
            }
        }
    }

    @Test
    public void refusesAManifestSignedWithAGamePassKey() {
        TestSupport.Key pass = new TestSupport.Key("p1");
        try {
            ChannelManifest.parse(pass.sign(manifest()), release.asMap(), "stable");
            fail("accepted a manifest signed by the pass key");
        } catch (SignedDoc.Invalid e) {
            assertEquals("unknown_key", e.reason);
        }
    }

    @Test
    public void offersOnlyNewerVersionsOfThisAppForThisAndroid() throws Exception {
        ChannelManifest m = parse(manifest());
        assertNotNull(m.appUpdateFor("al.tundu.games", 10202, 30, "d_A", NOW));
        assertNull("same version", m.appUpdateFor("al.tundu.games", 10203, 30, "d_A", NOW));
        assertNull("newer installed", m.appUpdateFor("al.tundu.games", 10300, 30, "d_A", NOW));
        assertNull("other package", m.appUpdateFor("com.other", 1, 30, "d_A", NOW));
        assertNull("Android too old", m.appUpdateFor("al.tundu.games", 1, 27, "d_A", NOW));
        assertNull("expired manifest", m.appUpdateFor("al.tundu.games", 1, 30, "d_A", m.expires));
    }

    @Test
    public void rolloutPicksADeterministicShareOfBoxesButRequiredUpdatesGoToAll() throws Exception {
        JSONObject json = manifest();
        put(app(json), "rollout", 20);
        ChannelManifest m = parse(json);
        int got = 0;
        for (int i = 0; i < 2000; i++) {
            ChannelManifest.App a = m.appUpdateFor("al.tundu.games", 10000, 30, "d_" + i, NOW);
            if (a != null) got++;
            assertEquals(a != null, m.appUpdateFor("al.tundu.games", 10000, 30, "d_" + i, NOW) != null);
        }
        assertEquals(400, got, 60);
        put(app(json), "rollout", 0);
        put(app(json), "required", 10100);
        m = parse(json);
        assertNotNull("below required: install even outside the rollout", m.appUpdateFor("al.tundu.games", 10000, 30, "d_1", NOW));
        assertNull("above required: wait for the rollout", m.appUpdateFor("al.tundu.games", 10150, 30, "d_1", NOW));
    }

    @Test
    public void firmwareOnlyForTheExactModelAndBuild() throws Exception {
        ChannelManifest m = parse(manifest());
        assertNotNull(m.firmwareFor("TX66", "old/fp:11/A/1:user/release-keys", "d_A", NOW));
        assertNull(m.firmwareFor("H96", "old/fp:11/A/1:user/release-keys", "d_A", NOW));
        assertNull(m.firmwareFor("TX66", "some/other:11/A/9:user/release-keys", "d_A", NOW));
        assertNull(m.firmwareFor("TX66", "old/fp:11/A/1:user/release-keys", "d_A", m.expires + 1));
    }

    static JSONObject app(JSONObject m) {
        return m.optJSONObject("app");
    }

    static JSONArray games(JSONObject m) {
        return m.optJSONArray("games");
    }

    static JSONObject fw(JSONObject m) {
        return m.optJSONArray("firmware").optJSONObject(0);
    }

    static void put(JSONObject o, String k, Object v) {
        try {
            o.put(k, v);
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }
}
