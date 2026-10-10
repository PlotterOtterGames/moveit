// CLI tests (Node's own test runner):  npm run test:cli
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { mkdtempSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, it, before } from "node:test";
import { fromB64url } from "../../src/crypto.ts";
import vectors from "../../test/vectors/rollout.json" with { type: "json" };
import { emptyChannel, rolloutBucket, signChannel, validateChannel, verifyChannel, type Channel } from "../channel.ts";
import { decryptKeyFile, encryptKeyFile, loadPinned, newP256, savePinned, spkiOf, trustedKeysJava } from "../keys.ts";
import { loadChannel, parseBadging, parseSigner, publish, setApp, setGame, setRollout, stage } from "../release.ts";
import { parseArgs } from "../tundu.ts";
import { CliError, config } from "../util.ts";

const SHA = "ab".repeat(32);

function goodChannel(): Channel {
  return {
    ...emptyChannel("stable"),
    serial: 1,
    issued: 1_760_000_000,
    expires: 1_770_000_000,
    app: {
      package: "al.tundu.games",
      versionCode: 10203,
      versionName: "1.2.3",
      file: `apk/${SHA}.apk`,
      sha256: SHA,
      size: 1000,
      signer: "cd".repeat(32),
      minSdk: 28,
      rollout: 10,
      required: 0,
    },
    games: [{ id: "runner", tier: "free" }],
  };
}

describe("key files", () => {
  it("encrypts with a passphrase and only that passphrase opens it", () => {
    const key = newP256();
    const file = encryptKeyFile(key, "release", "r1", "correct horse battery staple");
    assert.equal(file.public, spkiOf(key));
    assert.ok(!JSON.stringify(file).includes(key.export({ format: "jwk" }).d!));
    assert.equal(spkiOf(decryptKeyFile(file, "correct horse battery staple")), spkiOf(key));
    assert.throws(() => decryptKeyFile(file, "wrong passphrase!!"), CliError);
  });

  it("can't be relabelled (role and kid are authenticated)", () => {
    const file = encryptKeyFile(newP256(), "pass", "p1", "correct horse battery staple");
    assert.throws(() => decryptKeyFile({ ...file, role: "release" }, "correct horse battery staple"), CliError);
    assert.throws(() => decryptKeyFile({ ...file, kid: "r9" }, "correct horse battery staple"), CliError);
  });

  it("generates TrustedKeys.java with each role's keys", () => {
    const java = trustedKeysJava({ release: { r1: "AAA=" }, pass: { p1: "BBB=" }, apkSigner: "ef".repeat(32) });
    assert.match(java, /package al\.moveit\.cloud;/);
    assert.match(java, /m\.put\("r1", "AAA="\);\n        RELEASE =/);
    assert.match(java, /m\.put\("p1", "BBB="\);\n        PASS =/);
    assert.match(java, new RegExp(`APK_SIGNER = "${"ef".repeat(32)}"`));
    assert.match(trustedKeysJava({ release: {}, pass: {}, apkSigner: null }), /APK_SIGNER = null;/);
  });
});

describe("channel manifests", () => {
  it("accepts a good manifest and signs it so it verifies with the pinned key only", () => {
    const key = newP256();
    const doc = signChannel(goodChannel(), "r1", key);
    assert.deepEqual(verifyChannel(doc, { r1: spkiOf(key) }), goodChannel());
    assert.throws(() => verifyChannel(doc, { r2: spkiOf(key) }), /unknown key/);
    assert.throws(() => verifyChannel(doc, { r1: spkiOf(newP256()) }), /signature/);
  });

  it("rejects every broken rule", () => {
    const cases: [string, (c: Channel) => void][] = [
      ["type", (c) => ((c as any).type = "tundu.pass")],
      ["serial 0", (c) => (c.serial = 0)],
      ["expired before issued", (c) => (c.expires = c.issued)],
      ["lifetime > 400 days", (c) => (c.expires = c.issued + 401 * 86400)],
      ["file not named by hash", (c) => (c.app!.file = "apk/latest.apk")],
      ["hash mismatch", (c) => (c.app!.sha256 = "ff".repeat(32))],
      ["rollout 101", (c) => (c.app!.rollout = 101)],
      ["required above version", (c) => (c.app!.required = 10204)],
      ["bad package", (c) => (c.app!.package = "Tundu")],
      ["duplicate game", (c) => c.games.push({ id: "runner", tier: "pass" })],
      ["bad tier", (c) => ((c.games[0] as any).tier = "premium")],
      ["firmware from includes to", (c) => c.firmware.push({ model: "TX66", from: ["a"], to: "a", file: `ota/${SHA}.zip`, sha256: SHA, size: 1, rollout: 1 })],
    ];
    for (const [name, mutate] of cases) {
      const c = goodChannel();
      mutate(c);
      assert.throws(() => validateChannel(c), CliError, name);
    }
  });

  it("spreads boxes evenly over rollout buckets and matches the shared vectors", () => {
    const counts = new Array(10).fill(0);
    for (let i = 0; i < 20000; i++) counts[Math.floor(rolloutBucket(`d_${i}`, "10203") / 10)]++;
    for (const n of counts) assert.ok(Math.abs(n - 2000) < 200, `bucket decile ${n}`);
    // Each release picks a different first 10%: the overlap between two releases' early groups is about 1%.
    let both = 0;
    for (let i = 0; i < 20000; i++) if (rolloutBucket(`d_${i}`, "10203") < 10 && rolloutBucket(`d_${i}`, "10300") < 10) both++;
    assert.ok(both > 100 && both < 300, `overlap ${both}`);
    for (const v of vectors) assert.equal(rolloutBucket(v.device, v.salt), v.bucket);
  });
});

describe("APK inspection", () => {
  it("reads aapt2 badging", () => {
    const out = `package: name='al.tundu.games' versionCode='10203' versionName='1.2.3' platformBuildVersionName='16'\nminSdkVersion:'28'\ntargetSdkVersion:'36'\n`;
    assert.deepEqual(parseBadging(out), { package: "al.tundu.games", versionCode: 10203, versionName: "1.2.3", minSdk: 28 });
  });

  it("reads apksigner and refuses unsigned, v1-only or multi-signer APKs", () => {
    const ok = `Verifies\nVerified using v1 scheme (JAR signing): false\nVerified using v2 scheme (APK Signature Scheme v2): true\nVerified using v3 scheme (APK Signature Scheme v3): true\nNumber of signers: 1\nSigner #1 certificate DN: CN=Tundu, O=Tundu, C=AL\nSigner #1 certificate SHA-256 digest: ${"12".repeat(32)}\nSigner #1 certificate SHA-1 digest: aa\n`;
    assert.deepEqual(parseSigner(ok), { signer: "12".repeat(32), signerDn: "CN=Tundu, O=Tundu, C=AL" });
    assert.throws(() => parseSigner(ok.replace("Verifies\n", "DOES NOT VERIFY\n")), CliError);
    assert.throws(() => parseSigner(ok.replace(/: true/g, ": false")), CliError);
    assert.throws(() => parseSigner(ok + `Signer #2 certificate SHA-256 digest: ${"34".repeat(32)}\n`), CliError);
  });
});

describe("release flow (no uploads)", () => {
  const dir = mkdtempSync(join(tmpdir(), "tundu-releases-"));
  const keyFile = join(dir, "release-r1.key");
  let releaseKey = newP256();

  before(() => {
    process.env["TUNDU_RELEASES_DIR"] = dir;
    process.env["TUNDU_TRUSTED_KEYS_JAVA"] = join(dir, "TrustedKeys.java");
    process.env["TUNDU_NO_UPLOAD"] = "1";
    process.env["TUNDU_YES"] = "1";
    process.env["TUNDU_RELEASE_PASSPHRASE"] = "test passphrase 123";
    config.releaseKey = keyFile;
    releaseKey = newP256();
    writeFileSync(keyFile, JSON.stringify(encryptKeyFile(releaseKey, "release", "r1", "test passphrase 123")));
    savePinned({ release: { r1: spkiOf(releaseKey) }, pass: {}, apkSigner: "12".repeat(32) });
  });

  const info = { package: "al.tundu.games", versionCode: 10203, versionName: "1.2.3", minSdk: 28, signer: "12".repeat(32), signerDn: "CN=Tundu" };

  it("stages only release-signed APKs of our package", async () => {
    const apk = join(dir, "Tundu.apk");
    writeFileSync(apk, "not really an apk");
    await assert.rejects(stage(apk, { ...info, signerDn: "CN=Android Debug,O=Android,C=US" }), /DEBUG/);
    await assert.rejects(stage(apk, { ...info, signer: "99".repeat(32) }), /not the pinned/);
    await assert.rejects(stage(apk, { ...info, package: "com.other" }), /brand\.json/);
    const staged = await stage(apk, info);
    assert.equal(staged.file, `apk/${staged.sha256}.apk`);
    writeFileSync(apk, "a different build with the same version");
    await assert.rejects(stage(apk, info), /bump the version/);
  });

  it("publishes signed manifests whose serial always goes up", async () => {
    const first = await publish("beta", (c) => setApp(c, 10203, 10, 0));
    assert.equal(first.serial, 1);
    const second = await publish("beta", (c) => setRollout(c, 50));
    assert.equal(second.serial, 2);
    assert.equal(second.app!.rollout, 50);
    const third = await publish("beta", (c) => setGame(c, "fly", { tier: "pass", minApp: 10203 }));
    assert.deepEqual(third.games, [{ id: "fly", tier: "pass", minApp: 10203 }]);
    const { channel, doc } = loadChannel("beta");
    assert.equal(channel.serial, 3);
    assert.equal(doc!.kid, "r1");
    assert.ok(statSync(join(dir, "channels", "beta.json")).size > 100);
    await assert.rejects(publish("beta", (c) => setApp(c, 99999, 10, 0)), /not staged/);
  });

  it("refuses to work with a manifest that was edited by hand", async () => {
    const path = join(dir, "channels", "beta.json");
    const doc = JSON.parse(readFileSync(path, "utf8"));
    const payload = JSON.parse(Buffer.from(fromB64url(doc.payload)).toString());
    payload.app.rollout = 100;
    doc.payload = Buffer.from(JSON.stringify(payload)).toString("base64url");
    writeFileSync(path, JSON.stringify(doc));
    assert.throws(() => loadChannel("beta"), /signature/);
  });

  it("keeps keys.json and TrustedKeys.java in step", () => {
    assert.deepEqual(Object.keys(loadPinned().release), ["r1"]);
    assert.match(readFileSync(join(dir, "TrustedKeys.java"), "utf8"), new RegExp(spkiOf(releaseKey).replace(/[+/]/g, ".")));
  });
});

describe("cards and arguments", () => {
  it("writes the batch's codes to a private CSV", async () => {
    const server = createServer((req, res) => {
      let body = "";
      req.on("data", (c) => (body += c));
      req.on("end", () => {
        assert.equal(req.headers.authorization, "Bearer test-admin-token-that-is-long-enough");
        const { count, days } = JSON.parse(body);
        res.writeHead(201, { "content-type": "application/json" });
        res.end(JSON.stringify({ batch: { id: 7, days, count }, codes: [{ code: "ABCD-EFGH-JKLM-NPQ2", qr: "https://tundu.al/kod#ABCD-EFGH-JKLM-NPQ2" }] }));
      });
    });
    await new Promise<void>((r) => server.listen(0, "127.0.0.1", () => r()));
    const port = (server.address() as { port: number }).port;
    const dir = mkdtempSync(join(tmpdir(), "tundu-cards-"));
    process.env["TUNDU_CARDS_DIR"] = dir;
    process.env["TUNDU_ADMIN_TOKEN"] = "test-admin-token-that-is-long-enough";
    config.api = `http://127.0.0.1:${port}`;
    const { newBatch } = await import("../cards.ts");
    const file = await newBatch(30, 1, "test");
    server.close();
    assert.equal(readFileSync(file, "utf8"), "batch,days,code,qr\n7,30,ABCD-EFGH-JKLM-NPQ2,https://tundu.al/kod#ABCD-EFGH-JKLM-NPQ2\n");
    if (process.platform !== "win32") assert.equal(statSync(file).mode & 0o777, 0o600);
  });

  it("parses flags and positionals", () => {
    assert.deepEqual(parseArgs(["cards", "new", "--days", "30", "--revoke", "--label", "Shop X"]), {
      positional: ["cards", "new"],
      flags: { days: "30", revoke: true, label: "Shop X" },
    });
  });
});
