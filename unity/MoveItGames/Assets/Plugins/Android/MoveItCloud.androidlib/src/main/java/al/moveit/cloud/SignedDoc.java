package al.moveit.cloud;

import org.json.JSONException;
import org.json.JSONObject;

import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.security.KeyFactory;
import java.security.PublicKey;
import java.security.Signature;
import java.security.spec.X509EncodedKeySpec;
import java.util.Base64;
import java.util.Map;

/**
 * The only way anything from the network is trusted on the box: a JSON envelope
 * {"kid": "...", "payload": "<base64url JSON>", "sig": "<base64url DER ECDSA P-256 SHA-256>"} whose signature is
 * checked with a pinned public key of the expected role (TrustedKeys.RELEASE for manifests, TrustedKeys.PASS for
 * Game Pass tokens) and whose payload has the expected "type" and version. A Game Pass key can never sign an update.
 */
public final class SignedDoc {
    public static final int MAX_PAYLOAD_BYTES = 256 * 1024;

    /** Why a document was refused: unknown_key, bad_signature, wrong_type, unsupported_version, malformed. */
    public static final class Invalid extends Exception {
        private static final long serialVersionUID = 1L;

        public final String reason;

        public Invalid(String reason) {
            super(reason);
            this.reason = reason;
        }
    }

    private SignedDoc() {}

    public static JSONObject verify(String envelopeJson, Map<String, String> roleKeys, String expectedType) throws Invalid {
        String kid, payload, sig;
        try {
            JSONObject envelope = new JSONObject(envelopeJson);
            kid = envelope.getString("kid");
            payload = envelope.getString("payload");
            sig = envelope.getString("sig");
        } catch (JSONException | NullPointerException e) {
            throw new Invalid("malformed");
        }
        String spki = roleKeys.get(kid);
        if (spki == null) throw new Invalid("unknown_key");
        byte[] bytes;
        byte[] signature;
        try {
            bytes = Base64.getUrlDecoder().decode(payload);
            signature = Base64.getUrlDecoder().decode(sig);
        } catch (IllegalArgumentException e) {
            throw new Invalid("malformed");
        }
        if (bytes.length > MAX_PAYLOAD_BYTES || signature.length > 80) throw new Invalid("malformed");
        try {
            PublicKey key = KeyFactory.getInstance("EC").generatePublic(new X509EncodedKeySpec(Base64.getDecoder().decode(spki)));
            Signature verifier = Signature.getInstance("SHA256withECDSA");
            verifier.initVerify(key);
            verifier.update(bytes);
            if (!verifier.verify(signature)) throw new Invalid("bad_signature");
        } catch (Invalid e) {
            throw e;
        } catch (Exception e) {
            // A malformed DER signature makes some providers throw instead of returning false.
            throw new Invalid("bad_signature");
        }
        JSONObject doc;
        try {
            String text = StandardCharsets.UTF_8.newDecoder()
                    .onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT)
                    .decode(ByteBuffer.wrap(bytes)).toString();
            doc = new JSONObject(text);
        } catch (CharacterCodingException | JSONException e) {
            throw new Invalid("malformed");
        }
        if (!expectedType.equals(doc.optString("type"))) throw new Invalid("wrong_type");
        if (doc.optInt("v", -1) != 1) throw new Invalid("unsupported_version");
        return doc;
    }
}
