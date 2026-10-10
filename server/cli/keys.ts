// Signing keys.
//
// - release key (offline): signs the channel manifests = what boxes install. Lives only on the laptop, in a file
//   encrypted with a passphrase (scrypt + AES-256-GCM), plus an offline backup. Never uploaded anywhere.
// - pass key (online): signs Game Pass tokens. Lives only in the Worker secret PASS_SIGNING_KEY.
// Boxes pin the public keys (TrustedKeys.java, generated from releases/keys.json by this file), per role, so a
// stolen pass key can't sign an update. Rotation: add the new key (new kid) to keys.json, ship an app update
// that pins both, then start signing with the new one.

import { createPrivateKey, createPublicKey, generateKeyPairSync, randomBytes, scryptSync, createCipheriv, createDecipheriv, createHash, type KeyObject } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { askSecret, CliError, fail, REPO_DIR, releasesDir, readJsonFile, writeFileAtomic, writeJsonFile } from "./util.ts";

export type Role = "release" | "pass";

export interface PinnedKeys {
  /** Update-signing public keys (SPKI DER, base64) by kid. */
  release: Record<string, string>;
  /** Game Pass token public keys by kid. */
  pass: Record<string, string>;
  /** SHA-256 of the certificate that signs release APKs (the Android keystore), lower-case hex. */
  apkSigner: string | null;
}

export const keysFile = () => join(releasesDir(), "keys.json");
export const trustedKeysJavaFile = () =>
  process.env["TUNDU_TRUSTED_KEYS_JAVA"] ??
  join(REPO_DIR, "unity/MoveItGames/Assets/Plugins/Android/MoveItCloud.androidlib/src/main/java/al/moveit/cloud/TrustedKeys.java");

export function loadPinned(): PinnedKeys {
  return readJsonFile<PinnedKeys>(keysFile(), { release: {}, pass: {}, apkSigner: null });
}

const KID = /^[a-z][a-z0-9]{0,7}$/;

// ---------- encrypted key files ----------

interface KeyFile {
  type: "tundu.key";
  v: 1;
  role: Role;
  kid: string;
  public: string;
  kdf: { name: "scrypt"; N: number; r: number; p: number; salt: string };
  cipher: { name: "aes-256-gcm"; iv: string; tag: string };
  data: string;
}

const SCRYPT = { N: 1 << 17, r: 8, p: 1 };

function deriveKey(passphrase: string, salt: Buffer, params: { N: number; r: number; p: number }): Buffer {
  return scryptSync(passphrase.normalize("NFC"), salt, 32, { ...params, maxmem: 256 * 1024 * 1024 });
}

export function encryptKeyFile(privateKey: KeyObject, role: Role, kid: string, passphrase: string): KeyFile {
  const salt = randomBytes(16);
  const iv = randomBytes(12);
  const cipher = createCipheriv("aes-256-gcm", deriveKey(passphrase, salt, SCRYPT), iv);
  const header = `tundu.key:${role}:${kid}`;
  cipher.setAAD(Buffer.from(header));
  const der = privateKey.export({ type: "pkcs8", format: "der" });
  const data = Buffer.concat([cipher.update(der), cipher.final()]);
  return {
    type: "tundu.key",
    v: 1,
    role,
    kid,
    public: spkiOf(privateKey),
    kdf: { name: "scrypt", ...SCRYPT, salt: salt.toString("base64") },
    cipher: { name: "aes-256-gcm", iv: iv.toString("base64"), tag: cipher.getAuthTag().toString("base64") },
    data: data.toString("base64"),
  };
}

export function decryptKeyFile(file: KeyFile, passphrase: string): KeyObject {
  if (file.type !== "tundu.key" || file.v !== 1) fail("not a Tundu key file");
  const decipher = createDecipheriv(
    "aes-256-gcm",
    deriveKey(passphrase, Buffer.from(file.kdf.salt, "base64"), file.kdf),
    Buffer.from(file.cipher.iv, "base64"),
  );
  decipher.setAAD(Buffer.from(`tundu.key:${file.role}:${file.kid}`));
  decipher.setAuthTag(Buffer.from(file.cipher.tag, "base64"));
  let der: Buffer;
  try {
    der = Buffer.concat([decipher.update(Buffer.from(file.data, "base64")), decipher.final()]);
  } catch {
    throw new CliError("wrong passphrase (or the key file was changed)");
  }
  const key = createPrivateKey({ key: der, format: "der", type: "pkcs8" });
  if (spkiOf(key) !== file.public) fail("key file is inconsistent");
  return key;
}

export function readKeyFile(path: string): KeyFile {
  if (!existsSync(path)) fail(`no key file at ${path} (set TUNDU_RELEASE_KEY, or create one with: keys new-release)`);
  return JSON.parse(readFileSync(path, "utf8")) as KeyFile;
}

export async function unlockReleaseKey(path: string): Promise<{ kid: string; key: KeyObject }> {
  const file = readKeyFile(path);
  if (file.role !== "release") fail(`${path} is a ${file.role} key, not a release key`);
  const passphrase = await askSecret(`Passphrase for release key ${file.kid}`, "TUNDU_RELEASE_PASSPHRASE");
  return { kid: file.kid, key: decryptKeyFile(file, passphrase) };
}

export function spkiOf(key: KeyObject): string {
  const pub = key.type === "private" ? createPublicKey(key) : key;
  return pub.export({ type: "spki", format: "der" }).toString("base64");
}

export function newP256(): KeyObject {
  return generateKeyPairSync("ec", { namedCurve: "P-256" }).privateKey;
}

// ---------- commands ----------

export async function newReleaseKey(kid: string, out: string): Promise<void> {
  if (!KID.test(kid)) fail("kid: 1-8 lower-case letters/digits, starting with a letter (e.g. r1)");
  if (existsSync(out)) fail(`${out} exists; refusing to overwrite a key`);
  const pinned = loadPinned();
  if (pinned.release[kid] || pinned.pass[kid]) fail(`kid ${kid} is already used`);
  const passphrase = await askSecret("New passphrase (long, from your password manager)", "TUNDU_RELEASE_PASSPHRASE");
  if (passphrase.length < 12) fail("use a passphrase of at least 12 characters");
  if (!process.env["TUNDU_RELEASE_PASSPHRASE"] && (await askSecret("Repeat it")) !== passphrase) fail("passphrases differ");
  const key = newP256();
  writeFileAtomic(out, JSON.stringify(encryptKeyFile(key, "release", kid, passphrase), null, 2) + "\n", 0o600);
  pinned.release[kid] = spkiOf(key);
  savePinned(pinned);
  console.log(`Release key ${kid} written to ${out} (encrypted).`);
  console.log(`Public key pinned in ${keysFile()} and ${trustedKeysJavaFile()}.`);
  console.log("NOW: copy the key file to an offline backup (USB stick in a drawer) and the passphrase to your password manager.");
  console.log("Boxes only trust it from the next app build on.");
}

export function newPassKey(kid: string): void {
  if (!KID.test(kid)) fail("kid: 1-8 lower-case letters/digits, starting with a letter (e.g. p1)");
  const pinned = loadPinned();
  if (pinned.release[kid] || pinned.pass[kid]) fail(`kid ${kid} is already used`);
  const key = newP256();
  pinned.pass[kid] = spkiOf(key);
  savePinned(pinned);
  const jwk = { ...key.export({ format: "jwk" }), kid };
  console.error(`Public key pinned in ${keysFile()} and TrustedKeys.java. Store the private key ONLY as the Worker secret:`);
  console.error(`  node cli/tundu.ts keys new-pass --kid ${kid} | npx wrangler secret put PASS_SIGNING_KEY   (or paste the line below)`);
  console.log(JSON.stringify(jwk));
}

export function setApkSigner(digest: string): void {
  const hex = digest.toLowerCase().replace(/[^0-9a-f]/g, "");
  if (hex.length !== 64) fail("expected the 64-hex SHA-256 certificate digest (apksigner verify --print-certs)");
  const pinned = loadPinned();
  pinned.apkSigner = hex;
  savePinned(pinned);
  console.log(`APK signer pinned: ${hex}`);
}

export function pepper(): void {
  console.log(randomBytes(32).toString("base64"));
}

export function adminToken(): void {
  const token = randomBytes(32).toString("base64url");
  console.log(`Admin token (keep it in your password manager; set TUNDU_ADMIN_TOKEN when using the CLI):\n  ${token}`);
  console.log(`Its SHA-256, for the Worker:  npx wrangler secret put ADMIN_TOKEN_SHA256\n  ${createHash("sha256").update(token).digest("hex")}`);
}

export function savePinned(pinned: PinnedKeys): void {
  writeJsonFile(keysFile(), pinned);
  writeFileAtomic(trustedKeysJavaFile(), trustedKeysJava(pinned));
}

/** TrustedKeys.java, generated: the box's pinned keys. Don't edit the Java by hand. */
export function trustedKeysJava(pinned: PinnedKeys): string {
  const puts = (keys: Record<string, string>) =>
    Object.entries(keys)
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([kid, spki]) => `        m.put("${kid}", "${spki}");`);
  const lines = [
    "// GENERATED by server/cli/tundu.ts from releases/keys.json. Do not edit by hand.",
    "// The public keys this app trusts, per role. Release keys sign app updates and the game catalog (offline key);",
    "// pass keys sign Game Pass tokens (online key). A document is only accepted with a key of its own role.",
    "package al.moveit.cloud;",
    "",
    "import java.util.Collections;",
    "import java.util.HashMap;",
    "import java.util.Map;",
    "",
    "public final class TrustedKeys {",
    "    private TrustedKeys() {}",
    "",
    "    /** kid -> SPKI DER (base64) of the keys that sign channel manifests. */",
    "    public static final Map<String, String> RELEASE;",
    "    /** kid -> SPKI DER (base64) of the keys that sign Game Pass tokens. */",
    "    public static final Map<String, String> PASS;",
    "    /** SHA-256 (hex) of the certificate that signs release APKs; null until set. */",
    `    public static final String APK_SIGNER = ${pinned.apkSigner ? `"${pinned.apkSigner}"` : "null"};`,
    "",
    "    static {",
    "        Map<String, String> m = new HashMap<>();",
    ...puts(pinned.release),
    "        RELEASE = Collections.unmodifiableMap(m);",
    "        m = new HashMap<>();",
    ...puts(pinned.pass),
    "        PASS = Collections.unmodifiableMap(m);",
    "    }",
    "}",
  ];
  return lines.join("\n") + "\n";
}
