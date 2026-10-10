package al.moveit.cloud;

import org.json.JSONObject;

import java.util.Map;

/** A verified Game Pass token (server/src/pass.ts): which box, until when, and the server's clock when signed. */
public final class PassToken {
    public static final String TYPE = "tundu.pass";

    public final String device;
    public final long passUntil;
    public final long serverNow;
    public final boolean blocked;
    /** The signed envelope, kept so the token can be re-verified after loading it from storage. */
    public final String envelope;

    private PassToken(String device, long passUntil, long serverNow, boolean blocked, String envelope) {
        this.device = device;
        this.passUntil = passUntil;
        this.serverNow = serverNow;
        this.blocked = blocked;
        this.envelope = envelope;
    }

    /** Verifies the envelope with the pass keys and checks it was made for this device. */
    public static PassToken parse(String envelope, Map<String, String> passKeys, String expectedDevice) throws SignedDoc.Invalid {
        JSONObject p = SignedDoc.verify(envelope, passKeys, TYPE);
        String device = p.optString("device", "");
        long until = p.optLong("pass_until", -1);
        long now = p.optLong("now", -1);
        if (!device.equals(expectedDevice)) throw new SignedDoc.Invalid("wrong_device");
        if (until < 0 || now <= 0) throw new SignedDoc.Invalid("malformed");
        return new PassToken(device, until, now, p.optBoolean("blocked", false), envelope);
    }

    public boolean activeAt(long trustedNow) {
        return !blocked && trustedNow < passUntil;
    }
}
