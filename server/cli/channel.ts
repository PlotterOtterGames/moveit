// Channel manifests: the signed file that tells boxes which app version to install, which games are free or in
// Game Pass, and which firmware to apply. One per channel (e.g. "dev", "beta", "stable"), published at
// <cdn>/channels/<name>.json. The box (Java ChannelManifest) applies the same rules as validateChannel here and
// ignores any manifest that breaks one.
//
// Protections (from The Update Framework's threat list):
// - forged update:        signed with the offline release key; the box pins the public key
// - rollback / replay:    `serial` only goes up; the box refuses a lower serial than it has seen
// - freeze (stale file):  `expires`; the box won't install from an expired manifest (it keeps running)
// - mix-and-match:        files are named by their SHA-256 and the manifest carries hash + size
// - wrong file for box:   firmware entries name the exact model and the fingerprints they apply to
// - bad release:          `rollout` % of boxes first (deterministic by device id), raise it as health stays good

import { createHash, sign, verify, createPublicKey, type KeyObject } from "node:crypto";
import { b64url, fromB64url, type SignedDoc } from "../src/crypto.ts";
import { fail } from "./util.ts";

export const CHANNEL_TYPE = "tundu.channel";
export const DEFAULT_LIFETIME_DAYS = 180;
export const MAX_LIFETIME_DAYS = 400;

export interface AppRelease {
  package: string;
  versionCode: number;
  versionName: string;
  file: string;
  sha256: string;
  size: number;
  signer: string;
  minSdk: number;
  rollout: number;
  /** Boxes below this versionCode install the update as soon as possible (even mid-session). 0 = none. */
  required: number;
}

export interface GameEntry {
  id: string;
  tier: "free" | "pass";
  /** Hide the game on app versions below this (e.g. a version where it crashes). */
  minApp?: number;
  /** Kill switch: hide everywhere. */
  hidden?: boolean;
  /** Unix seconds when it appears (scheduled launch). */
  from?: number;
}

export interface FirmwareRelease {
  /** Build.MODEL of the box this image is for. */
  model: string;
  /** Build.FINGERPRINT values that may update to this image (anything else: skip). */
  from: string[];
  /** Build.FINGERPRINT after the update (how the box knows it worked). */
  to: string;
  file: string;
  sha256: string;
  size: number;
  rollout: number;
}

export interface Channel {
  type: typeof CHANNEL_TYPE;
  v: 1;
  channel: string;
  serial: number;
  issued: number;
  expires: number;
  app: AppRelease | null;
  games: GameEntry[];
  firmware: FirmwareRelease[];
}

const HEX64 = /^[0-9a-f]{64}$/;
const PACKAGE = /^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$/;
const GAME_ID = /^[a-z0-9_]{1,32}$/;
const isInt = (v: unknown, min: number, max: number) => typeof v === "number" && Number.isSafeInteger(v) && v >= min && v <= max;
const PLAIN = /^[\x20-\x7e]{1,200}$/;

/** Throws with the first broken rule. Same rules as the box's ChannelManifest.parse. */
export function validateChannel(c: Channel): void {
  const bad = (what: string): never => fail(`invalid manifest: ${what}`);
  if (c.type !== CHANNEL_TYPE || c.v !== 1) bad("type/v");
  if (!/^[a-z0-9-]{1,32}$/.test(c.channel)) bad("channel");
  if (!isInt(c.serial, 1, 2 ** 31)) bad("serial");
  if (!isInt(c.issued, 1_600_000_000, 4_000_000_000) || !isInt(c.expires, c.issued + 1, c.issued + MAX_LIFETIME_DAYS * 86400)) bad("issued/expires");
  if (c.app !== null) {
    const a = c.app;
    if (!PACKAGE.test(a.package)) bad("app.package");
    if (!isInt(a.versionCode, 1, 2_100_000_000)) bad("app.versionCode");
    if (typeof a.versionName !== "string" || !/^[0-9A-Za-z.\-+]{1,32}$/.test(a.versionName)) bad("app.versionName");
    if (!HEX64.test(a.sha256) || a.file !== `apk/${a.sha256}.apk`) bad("app.file/sha256");
    if (!isInt(a.size, 1, 2 ** 31)) bad("app.size");
    if (!HEX64.test(a.signer)) bad("app.signer");
    if (!isInt(a.minSdk, 1, 1000)) bad("app.minSdk");
    if (!isInt(a.rollout, 0, 100)) bad("app.rollout");
    if (!isInt(a.required, 0, a.versionCode)) bad("app.required");
  }
  if (!Array.isArray(c.games) || c.games.length > 200) bad("games");
  const ids = new Set<string>();
  for (const g of c.games) {
    if (!GAME_ID.test(g.id) || ids.has(g.id)) bad(`games: id ${g.id}`);
    ids.add(g.id);
    if (g.tier !== "free" && g.tier !== "pass") bad(`games.${g.id}.tier`);
    if (g.minApp !== undefined && !isInt(g.minApp, 0, 2_100_000_000)) bad(`games.${g.id}.minApp`);
    if (g.hidden !== undefined && typeof g.hidden !== "boolean") bad(`games.${g.id}.hidden`);
    if (g.from !== undefined && !isInt(g.from, 0, 4_000_000_000)) bad(`games.${g.id}.from`);
  }
  if (!Array.isArray(c.firmware) || c.firmware.length > 50) bad("firmware");
  for (const f of c.firmware) {
    if (!PLAIN.test(f.model) || !PLAIN.test(f.to)) bad("firmware.model/to");
    if (!Array.isArray(f.from) || f.from.length < 1 || f.from.length > 20 || !f.from.every((x) => PLAIN.test(x))) bad("firmware.from");
    if (f.from.includes(f.to)) bad("firmware: from includes to");
    if (!HEX64.test(f.sha256) || f.file !== `ota/${f.sha256}.zip`) bad("firmware.file/sha256");
    if (!isInt(f.size, 1, 2 ** 33)) bad("firmware.size");
    if (!isInt(f.rollout, 0, 100)) bad("firmware.rollout");
  }
}

/**
 * Which of 100 rollout buckets a box is in for a given release. Deterministic, different boxes go first for each
 * release (the salt is the versionCode or the firmware fingerprint). Box rule: eligible if bucket < rollout.
 * bucket = (first 4 bytes of SHA-256("<device id>:<salt>") as unsigned big-endian) mod 100
 */
export function rolloutBucket(deviceId: string, salt: string): number {
  const digest = createHash("sha256").update(`${deviceId}:${salt}`, "utf8").digest();
  return digest.readUInt32BE(0) % 100;
}

export function signChannel(channel: Channel, kid: string, key: KeyObject): SignedDoc {
  validateChannel(channel);
  const bytes = Buffer.from(JSON.stringify(channel), "utf8");
  return { kid, payload: b64url(bytes), sig: b64url(sign("sha256", bytes, { key, dsaEncoding: "der" })) };
}

/** Verifies with the pinned release keys and returns the manifest (throws if anything is off). */
export function verifyChannel(doc: SignedDoc, releaseKeys: Record<string, string>): Channel {
  const spki = releaseKeys[doc.kid];
  if (!spki) fail(`manifest signed with unknown key "${doc.kid}"`);
  const bytes = Buffer.from(fromB64url(doc.payload));
  const key = createPublicKey({ key: Buffer.from(spki, "base64"), format: "der", type: "spki" });
  if (!verify("sha256", bytes, { key, dsaEncoding: "der" }, Buffer.from(fromB64url(doc.sig)))) fail("manifest signature is wrong");
  const channel = JSON.parse(bytes.toString("utf8")) as Channel;
  validateChannel(channel);
  return channel;
}

export function emptyChannel(name: string): Channel {
  return { type: CHANNEL_TYPE, v: 1, channel: name, serial: 0, issued: 0, expires: 0, app: null, games: [], firmware: [] };
}
