import { describe, expect, it } from "vitest";
import { b64url, derToP1363, fromB64url, importSigner, p1363ToDer, safeEqual, signDoc, verifyDoc } from "../src/crypto.ts";
import { TEST_PASS_JWK, TEST_PASS_SPKI } from "./secrets.ts";

describe("signed documents", () => {
  it("base64url round-trips all byte values", () => {
    const bytes = new Uint8Array(256).map((_, i) => i);
    expect(fromB64url(b64url(bytes))).toEqual(bytes);
    expect(b64url(bytes)).not.toMatch(/[+/=]/);
    expect(() => fromB64url("abc+")).toThrow();
  });

  it("DER conversion round-trips, including high-bit and leading-zero integers", () => {
    for (let i = 0; i < 500; i++) {
      const raw = crypto.getRandomValues(new Uint8Array(64));
      if (i % 5 === 0) raw.fill(0, 0, 3); // short r
      if (i % 7 === 0) raw[32] = 0xff; // s needs a 0x00 pad
      const der = p1363ToDer(raw);
      expect(der[0]).toBe(0x30);
      expect(derToP1363(der)).toEqual(raw);
    }
  });

  it("rejects malformed DER", () => {
    const der = p1363ToDer(crypto.getRandomValues(new Uint8Array(64)));
    expect(() => derToP1363(der.subarray(0, der.length - 1))).toThrow();
    expect(() => derToP1363(Uint8Array.from([...der, 0]))).toThrow();
    const bad = Uint8Array.from(der);
    bad[2] = 0x03;
    expect(() => derToP1363(bad)).toThrow();
  });

  it("signs a payload that verifies with the public key", async () => {
    const signer = await importSigner(JSON.stringify(TEST_PASS_JWK));
    const doc = await signDoc(signer, { type: "tundu.pass", v: 1, hello: "ë ç" });
    expect(doc.kid).toBe("t1");
    expect(await verifyDoc(doc, TEST_PASS_SPKI)).toEqual({ type: "tundu.pass", v: 1, hello: "ë ç" });
  });

  it("rejects a changed payload or signature", async () => {
    const signer = await importSigner(JSON.stringify(TEST_PASS_JWK));
    const doc = await signDoc(signer, { type: "tundu.pass", pass_until: 100 });
    const forged = { ...doc, payload: b64url(new TextEncoder().encode(JSON.stringify({ type: "tundu.pass", pass_until: 9e9 }))) };
    await expect(verifyDoc(forged, TEST_PASS_SPKI)).rejects.toThrow();
    const sig = fromB64url(doc.sig);
    sig[sig.length - 1]! ^= 1;
    await expect(verifyDoc({ ...doc, sig: b64url(sig) }, TEST_PASS_SPKI)).rejects.toThrow();
  });

  it("refuses keys that aren't P-256 private JWKs with a kid", async () => {
    const { kid: _kid, ...noKid } = TEST_PASS_JWK;
    await expect(importSigner(JSON.stringify(noKid))).rejects.toThrow();
    const { d: _d, ...publicOnly } = TEST_PASS_JWK;
    await expect(importSigner(JSON.stringify(publicOnly))).rejects.toThrow();
  });

  it("compares in constant time and only equal strings match", () => {
    expect(safeEqual("abc", "abc")).toBe(true);
    expect(safeEqual("abc", "abd")).toBe(false);
    expect(safeEqual("abc", "abcd")).toBe(false);
  });
});
