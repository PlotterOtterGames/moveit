// A pretend box, to try the whole flow without hardware: against `npx wrangler dev` (local) or the real API.
//   node cli/tundu.ts devbox enroll --api http://127.0.0.1:8787
//   node cli/tundu.ts devbox redeem ABCD-EFGH-JKLM-NPQ7
//   node cli/tundu.ts devbox channel stable
// Its identity is kept in .devbox.json (current folder, gitignored).

import { createPublicKey, verify } from "node:crypto";
import { randomUUID } from "node:crypto";
import { existsSync } from "node:fs";
import { formatCode, normalizeCode } from "../src/codes.ts";
import { fromB64url, type SignedDoc } from "../src/crypto.ts";
import { rolloutBucket, verifyChannel } from "./channel.ts";
import { loadPinned } from "./keys.ts";
import { config, fail, formatTime, readJsonFile, writeJsonFile } from "./util.ts";

interface DevBox {
  api: string;
  device: string;
  secret: string;
  pendingRedeem?: { code: string; request: string };
}

const stateFile = () => process.env["TUNDU_DEVBOX"] ?? ".devbox.json";

async function post(api: string, path: string, body: unknown, auth?: string): Promise<{ status: number; data: any }> {
  const r = await fetch(api + path, {
    method: "POST",
    headers: { "content-type": "application/json", ...(auth ? { authorization: `Bearer ${auth}` } : {}) },
    body: JSON.stringify(body),
  }).catch((e: Error) => fail(`${api} unreachable: ${e.message}`));
  return { status: r.status, data: await r.json() };
}

/** Like the box: only trusts a token signed by a pinned pass key and made for this device. */
function readPass(doc: SignedDoc, device: string, passKeys: Record<string, string>): { pass_until: number; now: number; blocked: boolean } {
  const spki = passKeys[doc.kid];
  if (!spki) {
    console.warn(`(token signed by "${doc.kid}", which releases/keys.json doesn't pin: a real box would ignore it)`);
  } else {
    const key = createPublicKey({ key: Buffer.from(spki, "base64"), format: "der", type: "spki" });
    const bytes = Buffer.from(fromB64url(doc.payload));
    if (!verify("sha256", bytes, { key, dsaEncoding: "der" }, Buffer.from(fromB64url(doc.sig)))) fail("token signature is WRONG");
  }
  const p = JSON.parse(Buffer.from(fromB64url(doc.payload)).toString("utf8"));
  if (p.type !== "tundu.pass" || p.device !== device) fail("token is not a pass for this device");
  return p;
}

function printPass(p: { pass_until: number; now: number; blocked: boolean }): void {
  console.log(p.blocked ? "Box is BLOCKED." : p.pass_until > p.now ? `Game Pass active until ${formatTime(p.pass_until)}` : "No Game Pass.");
}

export async function enroll(api = config.api): Promise<void> {
  if (existsSync(stateFile())) fail(`${stateFile()} exists: this pretend box is already enrolled`);
  const r = await post(api, "/v1/devices", { model: "devbox", sdk: 30, app: 1 });
  if (r.status !== 201) fail(`${r.status} ${JSON.stringify(r.data)}`);
  writeJsonFile(stateFile(), { api, device: r.data.device, secret: r.data.secret } satisfies DevBox);
  console.log(`Enrolled as ${r.data.device}.`);
  printPass(readPass(r.data.pass, r.data.device, loadPinned().pass));
}

function box(): DevBox {
  return readJsonFile<DevBox>(stateFile());
}

export async function status(): Promise<void> {
  const b = box();
  const r = await post(b.api, "/v1/status", { app: 1, sdk: 30 }, `${b.device}.${b.secret}`);
  if (r.status !== 200) fail(`${r.status} ${JSON.stringify(r.data)}`);
  printPass(readPass(r.data.pass, b.device, loadPinned().pass));
}

export async function redeem(raw: string): Promise<void> {
  const b = box();
  const code = normalizeCode(raw);
  if (!code) fail("the box would say: check the code, a character is wrong (no network call made)");
  // Same request id until an answer arrives, so a retry after a lost answer can't double-count.
  if (!b.pendingRedeem || b.pendingRedeem.code !== code) b.pendingRedeem = { code, request: randomUUID() };
  writeJsonFile(stateFile(), b);
  const r = await post(b.api, "/v1/redeem", { code: formatCode(code), request: b.pendingRedeem.request }, `${b.device}.${b.secret}`);
  delete b.pendingRedeem;
  writeJsonFile(stateFile(), b);
  if (r.status !== 200) fail(`${r.status} ${JSON.stringify(r.data)}`);
  console.log(`Result: ${r.data.result}${r.data.days ? ` (+${r.data.days} days)` : ""}`);
  printPass(readPass(r.data.pass, b.device, loadPinned().pass));
}

export async function events(kind: string): Promise<void> {
  const b = box();
  const r = await post(b.api, "/v1/events", { app: 1, events: [{ kind, at: Math.floor(Date.now() / 1000) }] }, `${b.device}.${b.secret}`);
  console.log(r.status, JSON.stringify(r.data));
}

/** What a box on this channel would do right now, from the published manifest. */
export async function channel(name: string, versionCode = 1): Promise<void> {
  const b = existsSync(stateFile()) ? box() : null;
  const r = await fetch(`${config.cdn}/channels/${name}.json`, { cache: "no-store" });
  if (!r.ok) fail(`CDN answers ${r.status}`);
  const c = verifyChannel((await r.json()) as SignedDoc, loadPinned().release);
  console.log(`${name} #${c.serial}, expires ${formatTime(c.expires)}`);
  if (c.app) {
    const bucket = b ? rolloutBucket(b.device, String(c.app.versionCode)) : null;
    const wants = c.app.versionCode > versionCode;
    console.log(`App ${c.app.versionName} (${c.app.versionCode}), rollout ${c.app.rollout}%: ${!wants ? "already up to date" : bucket === null ? "(enroll to see the bucket)" : bucket < c.app.rollout ? `bucket ${bucket}: would install` : `bucket ${bucket}: would wait`}`);
  }
  for (const g of c.games) console.log(`  ${g.id}: ${g.tier}${g.hidden ? " (hidden)" : ""}${g.minApp ? ` (app >= ${g.minApp})` : ""}`);
}
