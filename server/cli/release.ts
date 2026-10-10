// Release commands: stage an APK, point a channel at it, change the rollout or the game catalog, add firmware.
// Every change produces a new signed manifest (serial + 1) in releases/channels/<channel>.json (committed: git
// history is the release log) and uploads it to R2. Files are uploaded before the manifest that names them, so a
// box never sees a manifest whose file is missing.

import { createHash } from "node:crypto";
import { createReadStream, existsSync, statSync } from "node:fs";
import { join } from "node:path";
import type { SignedDoc } from "../src/crypto.ts";
import { DEFAULT_LIFETIME_DAYS, emptyChannel, rolloutBucket, signChannel, validateChannel, verifyChannel, type Channel, type GameEntry } from "./channel.ts";
import { loadPinned, unlockReleaseKey } from "./keys.ts";
import { config, confirm, fail, formatTime, readJsonFile, releasesDir, REPO_DIR, run, uploadToR2, writeJsonFile } from "./util.ts";

export interface StagedApk {
  package: string;
  versionCode: number;
  versionName: string;
  minSdk: number;
  sha256: string;
  size: number;
  signer: string;
  file: string;
  staged: string;
}

export interface StagedFirmware {
  model: string;
  to: string;
  sha256: string;
  size: number;
  file: string;
  staged: string;
}

const apksFile = () => join(releasesDir(), "apks.json");
const firmwareFile = () => join(releasesDir(), "firmware.json");
export const channelFile = (name: string) => join(releasesDir(), "channels", `${name}.json`);
const uploadsOn = () => process.env["TUNDU_NO_UPLOAD"] !== "1";

export async function sha256File(path: string): Promise<{ sha256: string; size: number }> {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(path)) hash.update(chunk as Buffer);
  return { sha256: hash.digest("hex"), size: statSync(path).size };
}

// ---------- APK inspection (Android build-tools from the Unity SDK) ----------

export interface ApkInfo {
  package: string;
  versionCode: number;
  versionName: string;
  minSdk: number;
  signer: string;
  signerDn: string;
}

/** Parses `aapt2 dump badging` output. */
export function parseBadging(text: string): Pick<ApkInfo, "package" | "versionCode" | "versionName" | "minSdk"> {
  const pkg = /^package: name='([^']+)' versionCode='(\d+)' versionName='([^']*)'/m.exec(text);
  const sdk = /^(?:minSdkVersion|sdkVersion):'(\d+)'/m.exec(text);
  if (!pkg || !sdk) fail("could not read package/version/minSdk from aapt2 output");
  return { package: pkg[1]!, versionCode: Number(pkg[2]), versionName: pkg[3]!, minSdk: Number(sdk[1]) };
}

/** Parses `apksigner verify --print-certs -v` output: exactly one signer, verified with v2 or v3. */
export function parseSigner(text: string): Pick<ApkInfo, "signer" | "signerDn"> {
  if (!/^Verifies$/m.test(text)) fail("apksigner says the APK does not verify");
  if (!/Verified using v[23] scheme \(APK Signature Scheme v[23]\): true/.test(text)) fail("APK is not signed with signature scheme v2/v3");
  const digests = [...text.matchAll(/^Signer #(\d+) certificate SHA-256 digest: ([0-9a-f]{64})$/gm)];
  if (digests.length !== 1) fail(`expected exactly one signer, found ${digests.length}`);
  const dn = /^Signer #1 certificate DN: (.+)$/m.exec(text)?.[1] ?? "";
  return { signer: digests[0]![2]!, signerDn: dn };
}

function tool(name: string): string {
  const exe = process.platform === "win32" ? (name === "apksigner" ? `${name}.bat` : `${name}.exe`) : name;
  const path = join(config.buildTools, exe);
  if (!existsSync(path)) fail(`${exe} not found in ${config.buildTools} (set ANDROID_BUILD_TOOLS)`);
  return path;
}

export function inspectApk(apk: string): ApkInfo {
  const badging = run(tool("aapt2"), ["dump", "badging", apk]);
  if (badging.status !== 0) fail(`aapt2 failed: ${badging.stderr}`);
  const signer = run(tool("apksigner"), ["verify", "--print-certs", "-v", apk]);
  if (signer.status !== 0) fail(`apksigner failed: ${signer.stderr || signer.stdout}`);
  return { ...parseBadging(badging.stdout), ...parseSigner(signer.stdout) };
}

function brandPackage(): string {
  return readJsonFile<{ androidPackage: string }>(join(REPO_DIR, "unity/MoveItGames/Assets/Resources/brand.json")).androidPackage;
}

// ---------- commands ----------

/** release stage <apk>: checks the APK, records it in releases/apks.json and uploads it (by hash). */
export async function stage(apk: string, info: ApkInfo = inspectApk(apk)): Promise<StagedApk> {
  const pinned = loadPinned();
  if (/CN=Android Debug/i.test(info.signerDn)) fail("this APK is signed with the DEBUG key; build with the release keystore");
  if (!pinned.apkSigner) {
    fail(`no APK signer pinned yet. If this APK is signed with the release keystore, pin it:\n  node cli/tundu.ts keys set-apk-signer ${info.signer}`);
  }
  if (info.signer !== pinned.apkSigner) fail(`APK signer ${info.signer} is not the pinned release signer ${pinned.apkSigner}`);
  if (info.package !== brandPackage()) fail(`package ${info.package} is not ${brandPackage()} (brand.json)`);
  const { sha256, size } = await sha256File(apk);
  const apks = readJsonFile<StagedApk[]>(apksFile(), []);
  const same = apks.find((a) => a.versionCode === info.versionCode);
  if (same && same.sha256 !== sha256) fail(`versionCode ${info.versionCode} was already staged with a different APK; bump the version`);
  const entry: StagedApk = {
    package: info.package,
    versionCode: info.versionCode,
    versionName: info.versionName,
    minSdk: info.minSdk,
    sha256,
    size,
    signer: info.signer,
    file: `apk/${sha256}.apk`,
    staged: new Date().toISOString(),
  };
  if (uploadsOn()) {
    console.log(`Uploading ${entry.file} (${(size / 1e6).toFixed(1)} MB)...`);
    uploadToR2(entry.file, apk, "application/vnd.android.package-archive", "public, max-age=31536000, immutable");
  }
  if (!same) writeJsonFile(apksFile(), [...apks, entry].sort((a, b) => a.versionCode - b.versionCode));
  console.log(`Staged ${entry.versionName} (${entry.versionCode}), sha256 ${sha256}.`);
  console.log(`Next: node cli/tundu.ts release app --channel beta --version ${entry.versionCode} --rollout 100`);
  return entry;
}

/** release firmware-stage <ota.zip> --model M --to FINGERPRINT: an ODM-signed OTA image, uploaded by hash. */
export async function stageFirmware(zip: string, model: string, to: string): Promise<StagedFirmware> {
  const { sha256, size } = await sha256File(zip);
  const entry: StagedFirmware = { model, to, sha256, size, file: `ota/${sha256}.zip`, staged: new Date().toISOString() };
  if (uploadsOn()) {
    console.log(`Uploading ${entry.file} (${(size / 1e6).toFixed(0)} MB)... (wrangler uploads up to ~300 MB; use rclone for bigger images)`);
    uploadToR2(entry.file, zip, "application/zip", "public, max-age=31536000, immutable");
  }
  const list = readJsonFile<StagedFirmware[]>(firmwareFile(), []);
  if (!list.some((f) => f.sha256 === sha256)) writeJsonFile(firmwareFile(), [...list, entry]);
  console.log(`Staged firmware for ${model} -> ${to}, sha256 ${sha256}.`);
  return entry;
}

export function loadChannel(name: string): { channel: Channel; doc: SignedDoc | null } {
  const path = channelFile(name);
  if (!existsSync(path)) return { channel: emptyChannel(name), doc: null };
  const doc = readJsonFile<SignedDoc>(path);
  return { channel: verifyChannel(doc, loadPinned().release), doc };
}

/** Applies a change, signs the next manifest (serial + 1) and publishes it. */
export async function publish(name: string, change: (c: Channel) => void, lifetimeDays = DEFAULT_LIFETIME_DAYS): Promise<Channel> {
  const { channel: current } = loadChannel(name);
  const next: Channel = structuredClone(current);
  change(next);
  const t = Math.floor(Date.now() / 1000);
  next.serial = current.serial + 1;
  next.issued = t;
  next.expires = t + lifetimeDays * 86400;
  validateChannel(next);

  console.log(describeChange(current, next));
  if (!(await confirm(`Sign and publish ${name} #${next.serial}?`))) fail("cancelled");

  const pinned = loadPinned();
  const { kid, key } = await unlockReleaseKey(config.releaseKey);
  if (!pinned.release[kid]) fail(`release key ${kid} is not pinned in releases/keys.json: boxes would reject it`);
  const doc = signChannel(next, kid, key);
  verifyChannel(doc, pinned.release); // the file we publish verifies with what boxes pin

  if (uploadsOn()) await assertFilesOnCdn(next);
  writeJsonFile(channelFile(name), doc);
  if (uploadsOn()) push(name);
  console.log(`Published ${name} #${next.serial} (expires ${formatTime(next.expires)}). Commit releases/ to keep the history.`);
  return next;
}

/** release push --channel X: uploads the local signed manifest (e.g. after an upload failed). */
export function push(name: string): void {
  const path = channelFile(name);
  if (!existsSync(path)) fail(`no local manifest for ${name}`);
  uploadToR2(`channels/${name}.json`, path, "application/json", "public, max-age=300");
}

async function assertFilesOnCdn(c: Channel): Promise<void> {
  const files = [...(c.app ? [{ file: c.app.file, size: c.app.size }] : []), ...c.firmware.map((f) => ({ file: f.file, size: f.size }))];
  for (const f of files) {
    const r = await fetch(`${config.cdn}/${f.file}`, { method: "HEAD" }).catch((e: Error) => fail(`CDN unreachable: ${e.message}`));
    if (!r.ok) fail(`${config.cdn}/${f.file} answers ${r.status}: stage the file first`);
    if (Number(r.headers.get("content-length")) !== f.size) fail(`${f.file} on the CDN has the wrong size`);
  }
}

function describeChange(a: Channel, b: Channel): string {
  const lines = [`Channel ${b.channel}: #${a.serial} -> #${b.serial}`];
  const app = (c: Channel) => (c.app ? `${c.app.versionName} (${c.app.versionCode}) rollout ${c.app.rollout}%${c.app.required ? ` required<${c.app.required}` : ""}` : "none");
  if (app(a) !== app(b)) lines.push(`  app: ${app(a)} -> ${app(b)}`);
  const games = (c: Channel) => JSON.stringify(c.games);
  if (games(a) !== games(b)) lines.push(`  games: ${b.games.map((g) => `${g.id}:${g.tier}${g.hidden ? ":hidden" : ""}${g.minApp ? `:>=${g.minApp}` : ""}`).join(" ")}`);
  if (JSON.stringify(a.firmware) !== JSON.stringify(b.firmware)) lines.push(`  firmware: ${b.firmware.map((f) => `${f.model}->${f.to} ${f.rollout}%`).join(", ") || "none"}`);
  if (lines.length === 1) lines.push("  (re-signed, new expiry)");
  return lines.join("\n");
}

export function setApp(c: Channel, versionCode: number, rollout: number, required: number): void {
  const apk = readJsonFile<StagedApk[]>(apksFile(), []).find((a) => a.versionCode === versionCode);
  if (!apk) fail(`versionCode ${versionCode} is not staged (release stage <apk>)`);
  if (c.app && versionCode < c.app.versionCode) {
    console.warn(`Note: going from ${c.app.versionCode} down to ${versionCode}. Boxes never downgrade: boxes that already have the newer version keep it. To undo a bad release, publish a NEW versionCode with the old code.`);
  }
  c.app = {
    package: apk.package,
    versionCode: apk.versionCode,
    versionName: apk.versionName,
    file: apk.file,
    sha256: apk.sha256,
    size: apk.size,
    signer: apk.signer,
    minSdk: apk.minSdk,
    rollout,
    required,
  };
}

export function setRollout(c: Channel, percent: number): void {
  if (!c.app) fail("this channel has no app release yet");
  c.app.rollout = percent;
}

export function setGame(c: Channel, id: string, change: { remove?: boolean; tier?: "free" | "pass"; minApp?: number; hidden?: boolean; from?: number }): void {
  const i = c.games.findIndex((g) => g.id === id);
  if (change.remove) {
    if (i < 0) fail(`game ${id} is not in the catalog`);
    c.games.splice(i, 1);
    return;
  }
  const g: GameEntry = i >= 0 ? c.games[i]! : { id, tier: change.tier ?? "free" };
  if (change.tier) g.tier = change.tier;
  if (change.minApp !== undefined) g.minApp = change.minApp;
  if (change.hidden !== undefined) {
    if (change.hidden) g.hidden = true;
    else delete g.hidden;
  }
  if (change.from !== undefined) g.from = change.from;
  if (i < 0) c.games.push(g);
}

export function setFirmware(c: Channel, sha256: string, from: string[], rollout: number): void {
  const fw = readJsonFile<StagedFirmware[]>(firmwareFile(), []).find((f) => f.sha256 === sha256);
  if (!fw) fail(`firmware ${sha256} is not staged (release firmware-stage)`);
  c.firmware = c.firmware.filter((f) => !(f.model === fw.model && f.to === fw.to));
  if (rollout >= 0) c.firmware.push({ model: fw.model, from, to: fw.to, file: fw.file, sha256: fw.sha256, size: fw.size, rollout });
}

export function show(name: string): void {
  const { channel } = loadChannel(name);
  if (channel.serial === 0) {
    console.log(`No ${name} manifest yet.`);
    return;
  }
  const days = Math.floor((channel.expires - Date.now() / 1000) / 86400);
  console.log(JSON.stringify(channel, null, 2));
  console.log(days < 30 ? `WARNING: expires in ${days} days: run  release resign --channel ${name}` : `Expires in ${days} days.`);
}

/** release check --channel X: is the published manifest the one in git, and are its files right? */
export async function check(name: string, download: boolean): Promise<void> {
  const { channel: local, doc } = loadChannel(name);
  const r = await fetch(`${config.cdn}/channels/${name}.json`, { cache: "no-store" });
  if (!r.ok) fail(`CDN answers ${r.status} for channels/${name}.json`);
  const remoteDoc = (await r.json()) as SignedDoc;
  const remote = verifyChannel(remoteDoc, loadPinned().release);
  console.log(`CDN: ${name} #${remote.serial}, signed by ${remoteDoc.kid}, expires ${formatTime(remote.expires)}`);
  if (!doc || remoteDoc.payload !== doc.payload) console.log(`WARNING: differs from releases/channels/${name}.json (#${local.serial}). Push or commit?`);
  if (remote.app && download) {
    console.log(`Downloading ${remote.app.file} to check its hash...`);
    const apk = await fetch(`${config.cdn}/${remote.app.file}`);
    const bytes = new Uint8Array(await apk.arrayBuffer());
    const sha = createHash("sha256").update(bytes).digest("hex");
    if (sha !== remote.app.sha256 || bytes.length !== remote.app.size) fail("APK on the CDN does not match the manifest!");
    console.log("APK hash and size OK.");
  }
}

export function bucket(deviceId: string, name: string): void {
  const { channel } = loadChannel(name);
  if (!channel.app) fail("no app release on this channel");
  const b = rolloutBucket(deviceId, String(channel.app.versionCode));
  console.log(`${deviceId}: bucket ${b} for ${channel.app.versionCode} -> ${b < channel.app.rollout ? "gets it" : "waits"} (rollout ${channel.app.rollout}%)`);
}
