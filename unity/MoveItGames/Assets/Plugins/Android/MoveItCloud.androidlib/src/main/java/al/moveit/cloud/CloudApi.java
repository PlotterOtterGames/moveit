package al.moveit.cloud;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

/**
 * The Tundu API (server/src/index.ts) over HTTPS. Everything security-relevant in an answer (the Game Pass token)
 * is signed and verified separately, so this class only moves bytes; TLS is a second layer, not the only one.
 */
public final class CloudApi implements CloudState.Api {
    /** A non-2xx answer: status code and the server's error code ("unauthorized", "locked", ...). */
    public static final class HttpError extends IOException {
        private static final long serialVersionUID = 1L;

        public final int status;
        public final String error;
        public final long retryAfter;

        HttpError(int status, String error, long retryAfter) {
            super("HTTP " + status + " " + error);
            this.status = status;
            this.error = error;
            this.retryAfter = retryAfter;
        }
    }

    public static final class Enrollment {
        public String device, secret, passEnvelope;
    }

    public static final class RedeemAnswer {
        public String result, passEnvelope;
        public int days;
    }

    private final String base;

    public CloudApi(String base) {
        this.base = base.endsWith("/") ? base.substring(0, base.length() - 1) : base;
    }

    @Override
    public Enrollment enroll(String model, int sdk, int app) throws IOException {
        JSONObject body = obj("model", model.length() > 64 ? model.substring(0, 64) : model, "sdk", sdk, "app", app);
        JSONObject r = post("/v1/devices", body, null);
        Enrollment e = new Enrollment();
        e.device = r.optString("device");
        e.secret = r.optString("secret");
        e.passEnvelope = r.optJSONObject("pass") == null ? null : r.optJSONObject("pass").toString();
        if (e.device.isEmpty() || e.secret.isEmpty() || e.passEnvelope == null) throw new IOException("bad enrolment answer");
        return e;
    }

    /** Check-in: returns the signed Game Pass envelope. */
    @Override
    public String status(String device, String secret, int app, int sdk) throws IOException {
        JSONObject r = post("/v1/status", obj("app", app, "sdk", sdk), device + "." + secret);
        JSONObject pass = r.optJSONObject("pass");
        if (pass == null) throw new IOException("bad status answer");
        return pass.toString();
    }

    @Override
    public RedeemAnswer redeem(String device, String secret, String formattedCode, String requestId) throws IOException {
        JSONObject r = post("/v1/redeem", obj("code", formattedCode, "request", requestId), device + "." + secret);
        RedeemAnswer a = new RedeemAnswer();
        a.result = r.optString("result", "");
        a.days = r.optInt("days", 0);
        a.passEnvelope = r.optJSONObject("pass") == null ? null : r.optJSONObject("pass").toString();
        if (a.result.isEmpty() || a.passEnvelope == null) throw new IOException("bad redeem answer");
        return a;
    }

    @Override
    public void events(String device, String secret, int app, JSONArray events) throws IOException {
        post("/v1/events", obj("app", app, "events", events), device + "." + secret);
    }

    private JSONObject post(String path, JSONObject body, String auth) throws IOException {
        HttpURLConnection http = (HttpURLConnection) new URL(base + path).openConnection();
        http.setConnectTimeout(10_000);
        http.setReadTimeout(20_000);
        http.setInstanceFollowRedirects(false);
        http.setRequestMethod("POST");
        http.setDoOutput(true);
        http.setRequestProperty("content-type", "application/json");
        if (auth != null) http.setRequestProperty("authorization", "Bearer " + auth);
        byte[] bytes = body.toString().getBytes(StandardCharsets.UTF_8);
        http.setFixedLengthStreamingMode(bytes.length);
        try {
            try (OutputStream out = http.getOutputStream()) {
                out.write(bytes);
            }
            int code = http.getResponseCode();
            InputStream in = code >= 400 ? http.getErrorStream() : http.getInputStream();
            String text = in == null ? "" : readCapped(in, 64 * 1024);
            JSONObject json;
            try {
                json = text.isEmpty() ? new JSONObject() : new JSONObject(text);
            } catch (JSONException e) {
                throw new IOException("not JSON (HTTP " + code + ")");
            }
            if (code < 200 || code >= 300) {
                throw new HttpError(code, json.optString("error", "http_" + code), json.optLong("retry_after", 0));
            }
            return json;
        } finally {
            http.disconnect();
        }
    }

    private static String readCapped(InputStream in, int max) throws IOException {
        try (InputStream s = in) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int n;
            while ((n = s.read(buffer)) > 0) {
                if (out.size() + n > max) throw new IOException("answer too large");
                out.write(buffer, 0, n);
            }
            return out.toString("UTF-8");
        }
    }

    static JSONObject obj(Object... keysAndValues) {
        JSONObject o = new JSONObject();
        try {
            for (int i = 0; i < keysAndValues.length; i += 2) o.put((String) keysAndValues[i], keysAndValues[i + 1]);
        } catch (JSONException e) {
            throw new IllegalArgumentException(e);
        }
        return o;
    }
}
