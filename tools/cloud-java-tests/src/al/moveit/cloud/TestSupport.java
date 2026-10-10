package al.moveit.cloud;

import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.security.KeyPair;
import java.security.KeyPairGenerator;
import java.security.Signature;
import java.security.spec.ECGenParameterSpec;
import java.util.Base64;
import java.util.HashMap;
import java.util.Map;

/** Test helpers: P-256 keys that sign like the server, an in-memory store, a controllable clock, vectors. */
final class TestSupport {
    private TestSupport() {}

    static final class Key {
        final String kid;
        final KeyPair pair;

        Key(String kid) {
            this.kid = kid;
            try {
                KeyPairGenerator g = KeyPairGenerator.getInstance("EC");
                g.initialize(new ECGenParameterSpec("secp256r1"));
                pair = g.generateKeyPair();
            } catch (Exception e) {
                throw new IllegalStateException(e);
            }
        }

        String spki() {
            return Base64.getEncoder().encodeToString(pair.getPublic().getEncoded());
        }

        Map<String, String> asMap() {
            Map<String, String> m = new HashMap<>();
            m.put(kid, spki());
            return m;
        }

        /** {"kid","payload","sig"} with a DER signature over the exact payload bytes, like the server and CLI. */
        String sign(JSONObject payload) {
            try {
                byte[] bytes = payload.toString().getBytes(StandardCharsets.UTF_8);
                Signature s = Signature.getInstance("SHA256withECDSA");
                s.initSign(pair.getPrivate());
                s.update(bytes);
                Base64.Encoder url = Base64.getUrlEncoder().withoutPadding();
                return new JSONObject().put("kid", kid).put("payload", url.encodeToString(bytes)).put("sig", url.encodeToString(s.sign())).toString();
            } catch (Exception e) {
                throw new IllegalStateException(e);
            }
        }
    }

    static final class MemoryStore implements KeyValueStore {
        final Map<String, Object> map = new HashMap<>();

        @Override
        public String getString(String key, String fallback) {
            Object v = map.get(key);
            return v == null ? fallback : (String) v;
        }

        @Override
        public long getLong(String key, long fallback) {
            Object v = map.get(key);
            return v == null ? fallback : ((Number) v).longValue();
        }

        @Override
        public void put(Object... kv) {
            for (int i = 0; i < kv.length; i += 2) map.put((String) kv[i], kv[i + 1]);
        }

        @Override
        public void remove(String... keys) {
            for (String k : keys) map.remove(k);
        }
    }

    static final class FakeTime implements PassClock.Source {
        long wall, elapsed, boot = 1;

        @Override
        public long wallMillis() {
            return wall;
        }

        @Override
        public long elapsedMillis() {
            return elapsed;
        }

        @Override
        public long bootCount() {
            return boot;
        }

        void advance(long seconds) {
            wall += seconds * 1000;
            elapsed += seconds * 1000;
        }

        void reboot() {
            boot++;
            elapsed = 5_000;
        }
    }

    static JSONObject obj(Object... kv) {
        return CloudApi.obj(kv);
    }

    static String vectors(String name) throws Exception {
        String dir = System.getenv("TUNDU_VECTORS");
        if (dir == null) dir = "server/test/vectors";
        return new String(Files.readAllBytes(Paths.get(dir, name)), StandardCharsets.UTF_8);
    }
}
