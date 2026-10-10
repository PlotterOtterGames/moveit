package al.moveit.cloud;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.fail;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Test;

import java.util.HashMap;
import java.util.Iterator;
import java.util.Map;

/** The box agrees with the server and the CLI on codes, signatures and rollout buckets (server/test/vectors). */
public class VectorsTest {
    @Test
    public void giftCodesMatchTheServer() throws Exception {
        JSONObject v = new JSONObject(TestSupport.vectors("codes.json"));
        assertEquals(GiftCode.ALPHABET, v.getString("alphabet"));
        JSONArray valid = v.getJSONArray("valid");
        for (int i = 0; i < valid.length(); i++) {
            JSONObject c = valid.getJSONObject(i);
            assertEquals(c.getString("input"), c.getString("code"), GiftCode.normalize(c.getString("input")));
        }
        JSONArray invalid = v.getJSONArray("invalid");
        for (int i = 0; i < invalid.length(); i++) assertNull(invalid.getString(i), GiftCode.normalize(invalid.getString(i)));
    }

    @Test
    public void signedDocumentsMatchTheServer() throws Exception {
        JSONObject v = new JSONObject(TestSupport.vectors("signed.json"));
        Map<String, Map<String, String>> keys = new HashMap<>();
        for (String role : new String[] {"pass", "release"}) {
            JSONObject k = v.getJSONObject("keys").getJSONObject(role);
            Map<String, String> m = new HashMap<>();
            for (Iterator<String> it = k.keys(); it.hasNext(); ) {
                String kid = it.next();
                m.put(kid, k.getString(kid));
            }
            keys.put(role, m);
        }
        JSONArray docs = v.getJSONArray("docs");
        for (int i = 0; i < docs.length(); i++) {
            JSONObject d = docs.getJSONObject(i);
            String role = d.getString("role");
            String type = role.equals("pass") ? PassToken.TYPE : ChannelManifest.TYPE;
            String expect = d.getString("expect");
            try {
                JSONObject payload = SignedDoc.verify(d.getJSONObject("doc").toString(), keys.get(role), type);
                if (!expect.equals("ok")) fail(d.getString("name") + " should be refused as " + expect);
                assertEquals(d.getJSONObject("payload").toString(), payload.toString());
            } catch (SignedDoc.Invalid e) {
                assertEquals(d.getString("name"), expect, e.reason);
            }
        }
    }

    @Test
    public void theCliManifestPassesTheBoxRules() throws Exception {
        JSONObject v = new JSONObject(TestSupport.vectors("signed.json"));
        JSONObject release = v.getJSONObject("keys").getJSONObject("release");
        Map<String, String> keys = new HashMap<>();
        keys.put(release.keys().next(), release.getString(release.keys().next()));
        JSONArray docs = v.getJSONArray("docs");
        for (int i = 0; i < docs.length(); i++) {
            JSONObject d = docs.getJSONObject(i);
            if (!d.getString("name").equals("channel_ok")) continue;
            ChannelManifest m = ChannelManifest.parse(d.getJSONObject("doc").toString(), keys, "stable");
            assertEquals(10203, m.app.versionCode);
            assertEquals(25, m.app.rollout);
            assertEquals(3, m.serial);
            return;
        }
        fail("channel_ok vector missing");
    }

    @Test
    public void rolloutBucketsMatchTheCli() throws Exception {
        JSONArray v = new JSONArray(TestSupport.vectors("rollout.json"));
        for (int i = 0; i < v.length(); i++) {
            JSONObject r = v.getJSONObject(i);
            assertEquals(r.getInt("bucket"), ChannelManifest.rolloutBucket(r.getString("device"), r.getString("salt")));
        }
    }
}
