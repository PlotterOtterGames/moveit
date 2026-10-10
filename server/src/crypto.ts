// Signed documents: the one format the box trusts, for Game Pass tokens (signed here, online key) and channel
// manifests (signed on the laptop, offline key).
//
//   {"kid":"p1","payload":"<base64url of the UTF-8 JSON>","sig":"<base64url DER ECDSA P-256 SHA-256 signature>"}
//
// The signature covers the exact payload bytes, so there is no JSON canonicalisation to get wrong. ECDSA P-256
// because every Android version verifies it with the platform's own code (Ed25519 needs Android 13). DER because
// that is what Android's Signature("SHA256withECDSA") expects; WebCrypto produces raw r||s, converted below.
// The kid tells the box which pinned key to use, and the box only accepts each document type from its own role's
// keys: a stolen online Game Pass key can never sign an app update.

const encoder = new TextEncoder();
const decoder = new TextDecoder();

export interface SignedDoc {
  kid: string;
  payload: string;
  sig: string;
}

export function b64url(bytes: Uint8Array): string {
  let bin = "";
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function fromB64url(text: string): Uint8Array<ArrayBuffer> {
  if (!/^[A-Za-z0-9_-]*$/.test(text)) throw new Error("not base64url");
  const b64 = text.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((text.length + 3) % 4);
  return Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
}

/** Raw r||s (WebCrypto) to DER SEQUENCE { INTEGER r, INTEGER s }. */
export function p1363ToDer(sig: Uint8Array): Uint8Array<ArrayBuffer> {
  if (sig.length !== 64) throw new Error("expected a 64-byte P-256 signature");
  const int = (bytes: Uint8Array): number[] => {
    let i = 0;
    while (i < bytes.length - 1 && bytes[i] === 0) i++;
    const v = Array.from(bytes.subarray(i));
    if (v[0]! & 0x80) v.unshift(0);
    return [0x02, v.length, ...v];
  };
  const body = [...int(sig.subarray(0, 32)), ...int(sig.subarray(32))];
  return Uint8Array.from([0x30, body.length, ...body]);
}

/** DER to raw r||s, strict: exactly the encoding p1363ToDer produces (minimal integers, no trailing bytes). */
export function derToP1363(der: Uint8Array): Uint8Array<ArrayBuffer> {
  const fail = (): never => {
    throw new Error("bad DER signature");
  };
  if (der.length < 8 || der[0] !== 0x30 || der[1] !== der.length - 2) fail();
  const out = new Uint8Array(64);
  let pos = 2;
  for (let n = 0; n < 2; n++) {
    if (der[pos] !== 0x02) fail();
    const len = der[pos + 1]!;
    if (len < 1 || len > 33 || pos + 2 + len > der.length) fail();
    let v = der.subarray(pos + 2, pos + 2 + len);
    if (v[0]! & 0x80) fail(); // negative
    if (len > 1 && v[0] === 0 && !(v[1]! & 0x80)) fail(); // not minimal
    if (v.length === 33) v = v.subarray(1);
    out.set(v, n * 32 + (32 - v.length));
    pos += 2 + len;
  }
  if (pos !== der.length) fail();
  return out;
}

export async function sha256Hex(data: string | Uint8Array<ArrayBuffer>): Promise<string> {
  const bytes = typeof data === "string" ? encoder.encode(data) : data;
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
  let s = "";
  for (const b of digest) s += b.toString(16).padStart(2, "0");
  return s;
}

/** Constant-time string comparison (for secrets' hashes; lengths are public). */
export function safeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

export interface Signer {
  kid: string;
  key: CryptoKey;
}

/** The PASS_SIGNING_KEY secret: a P-256 private JWK with a "kid" field (made by `tundu keys new --role pass`). */
export async function importSigner(jwkJson: string): Promise<Signer> {
  const jwk = JSON.parse(jwkJson) as JsonWebKey & { kid?: string };
  if (jwk.kty !== "EC" || jwk.crv !== "P-256" || !jwk.d || !jwk.kid) {
    throw new Error("PASS_SIGNING_KEY must be a P-256 private JWK with a kid");
  }
  const { kid, ...keyData } = jwk;
  const key = await crypto.subtle.importKey("jwk", keyData, { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
  return { kid, key };
}

export async function signDoc(signer: Signer, payload: unknown): Promise<SignedDoc> {
  const bytes = encoder.encode(JSON.stringify(payload));
  const raw = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, signer.key, bytes));
  return { kid: signer.kid, payload: b64url(bytes), sig: b64url(p1363ToDer(raw)) };
}

/** Verifies with a public key (SPKI base64) and returns the payload. Used by the tests and the CLI. */
export async function verifyDoc(doc: SignedDoc, spkiBase64: string): Promise<unknown> {
  const spki = Uint8Array.from(atob(spkiBase64), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("spki", spki, { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
  const bytes = fromB64url(doc.payload);
  const ok = await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, derToP1363(fromB64url(doc.sig)), bytes);
  if (!ok) throw new Error("bad signature");
  return JSON.parse(decoder.decode(bytes));
}
