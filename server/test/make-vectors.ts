// Writes the test vectors that the box (Java) and Unity (C#) tests check against, so all three implementations
// agree on codes and signed documents. Run after changing a format:  node test/make-vectors.ts
// The files are committed; the TypeScript tests verify them too.

import { createPrivateKey, sign } from "node:crypto";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { rolloutBucket } from "../cli/channel.ts";
import { ALPHABET, checkChar, formatCode, generateCode, QR_PREFIX, randomId } from "../src/codes.ts";
import { b64url } from "../src/crypto.ts";
import { TEST_PASS_JWK, TEST_PASS_SPKI } from "./secrets.ts";
import { TEST_RELEASE_JWK, TEST_RELEASE_SPKI } from "./secrets-release.ts";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "vectors");
mkdirSync(out, { recursive: true });

// ---------- codes ----------
const valid: { input: string; code: string }[] = [];
for (let i = 0; i < 20; i++) {
  const code = generateCode();
  const pretty = formatCode(code);
  const forms = [code, pretty, pretty.toLowerCase(), ` ${pretty.replace(/-/g, " ")} `, QR_PREFIX + pretty, pretty.replace(/-/g, ".")];
  valid.push({ input: forms[i % forms.length]!, code });
}
// Fixed cases that pin the check-character algorithm (Luhn mod 32 over ALPHABET).
for (const body of ["222222222222222", "ZZZZZZZZZZZZZZZ", "23456789ABCDEFG", "HJKLMNPQRSTUVWX"]) {
  const code = body + checkChar(body);
  valid.push({ input: formatCode(code), code });
}
const base = generateCode();
const wrongCheck = base.slice(0, 15) + ALPHABET[(ALPHABET.indexOf(base[15]!) + 1) % 32];
const invalid = [
  "",
  base.slice(0, 15),
  base + "2",
  wrongCheck,
  "O" + base.slice(1),
  "0" + base.slice(1),
  "I" + base.slice(1),
  "1" + base.slice(1),
  base.slice(0, 8) + "_" + base.slice(9),
  "ABCD-EFGH",
  "x".repeat(101),
];
writeFileSync(join(out, "codes.json"), JSON.stringify({ alphabet: ALPHABET, valid, invalid }, null, 1) + "\n");

// ---------- signed documents ----------
// Signed with node:crypto (DER signatures) exactly like the release CLI; pass tokens like the Worker.
function signed(jwk: object & { kid: string }, payload: unknown, kid = jwk.kid) {
  const key = createPrivateKey({ key: jwk as any, format: "jwk" });
  const bytes = Buffer.from(JSON.stringify(payload), "utf8");
  return { kid, payload: b64url(bytes), sig: b64url(sign("sha256", bytes, { key, dsaEncoding: "der" })) };
}

const device = "d_" + "A".repeat(20);
const pass = { type: "tundu.pass", v: 1, device, pass_until: 1_800_000_000, now: 1_760_000_000, blocked: false };
const channel = {
  type: "tundu.channel",
  v: 1,
  channel: "stable",
  serial: 3,
  issued: 1_760_000_000,
  expires: 1_775_000_000,
  app: {
    package: "al.tundu.games",
    versionCode: 10203,
    versionName: "1.2.3",
    file: "apk/" + "ab".repeat(32) + ".apk",
    sha256: "ab".repeat(32),
    size: 56_789_012,
    signer: "cd".repeat(32),
    minSdk: 28,
    rollout: 25,
    required: 0,
  },
  games: [
    { id: "runner", tier: "free" },
    { id: "fly", tier: "pass", minApp: 10203 },
  ],
  firmware: [],
};

const passDoc = signed(TEST_PASS_JWK, pass);
const tamperedPayload = { ...passDoc, payload: b64url(Buffer.from(JSON.stringify({ ...pass, pass_until: 4_000_000_000 }))) };
const sig = Buffer.from(passDoc.sig.replace(/-/g, "+").replace(/_/g, "/"), "base64");
sig[sig.length - 1]! ^= 1;

const docs = [
  { name: "pass_ok", role: "pass", expect: "ok", doc: passDoc, payload: pass },
  { name: "channel_ok", role: "release", expect: "ok", doc: signed(TEST_RELEASE_JWK, channel), payload: channel },
  { name: "pass_tampered_payload", role: "pass", expect: "bad_signature", doc: tamperedPayload },
  { name: "pass_tampered_signature", role: "pass", expect: "bad_signature", doc: { ...passDoc, sig: b64url(sig) } },
  // A Game Pass key (online) must never be accepted for an app update (role separation).
  { name: "channel_signed_with_pass_key", role: "release", expect: "unknown_key", doc: signed(TEST_PASS_JWK, channel) },
  { name: "pass_unknown_kid", role: "pass", expect: "unknown_key", doc: signed(TEST_PASS_JWK, pass, "zz") },
  // Right key, wrong type inside: a pass token presented as a manifest (same role keys in a misconfigured box).
  { name: "pass_payload_as_channel", role: "release", expect: "wrong_type", doc: signed(TEST_RELEASE_JWK, pass) },
];
writeFileSync(
  join(out, "signed.json"),
  JSON.stringify({ keys: { pass: { [TEST_PASS_JWK.kid]: TEST_PASS_SPKI }, release: { [TEST_RELEASE_JWK.kid]: TEST_RELEASE_SPKI } }, docs }, null, 1) +
    "\n",
);
// ---------- rollout buckets ----------
const rollout = [];
for (let i = 0; i < 30; i++) {
  const device = "d_" + randomId(20);
  const salt = i % 3 === 0 ? "rk3566/tx66:11/RQ1A.210105.003/20261101:user/release-keys" : String(10000 + i * 7);
  rollout.push({ device, salt, bucket: rolloutBucket(device, salt) });
}
writeFileSync(join(out, "rollout.json"), JSON.stringify(rollout, null, 1) + "\n");
console.log("wrote codes.json, signed.json and rollout.json in", out);
