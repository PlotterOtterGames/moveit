// Gift cards and support commands: talk to the admin API with the admin token (TUNDU_ADMIN_TOKEN or a prompt).

import { homedir } from "node:os";
import { join } from "node:path";
import { normalizeCode, formatCode } from "../src/codes.ts";
import { askSecret, config, fail, formatTime, writeFileAtomic } from "./util.ts";

let token: string | null = null;

export async function adminApi(method: string, path: string, body?: unknown): Promise<any> {
  token ??= await askSecret("Admin token", "TUNDU_ADMIN_TOKEN");
  const r = await fetch(config.api + path, {
    method,
    headers: { authorization: `Bearer ${token}`, ...(body === undefined ? {} : { "content-type": "application/json" }) },
    body: body === undefined ? undefined : JSON.stringify(body),
  }).catch((e: Error) => fail(`${config.api} unreachable: ${e.message}`));
  const text = await r.text();
  const data = text ? JSON.parse(text) : null;
  if (!r.ok) fail(`${r.status} ${data?.error ?? text}${data?.field ? ` (${data.field})` : ""}`);
  return data;
}

const cardsDir = () => process.env["TUNDU_CARDS_DIR"] ?? join(homedir(), ".tundu", "cards");

/** cards new --days 30 --count 500 --label "Shop X": creates the batch (inactive) and saves the codes for printing. */
export async function newBatch(days: number, count: number, label: string, note?: string): Promise<string> {
  const r = await adminApi("POST", "/admin/v1/batches", { days, count, label, ...(note ? { note } : {}) });
  const rows = ["batch,days,code,qr", ...r.codes.map((c: { code: string; qr: string }) => `${r.batch.id},${days},${c.code},${c.qr}`)];
  const file = join(cardsDir(), `batch-${r.batch.id}.csv`);
  writeFileAtomic(file, rows.join("\n") + "\n", 0o600);
  console.log(`Batch ${r.batch.id}: ${count} cards of ${days} days, NOT active yet.`);
  console.log(`Codes: ${file}`);
  console.log("This is the only copy of the codes. Send it to the printer over a private channel, keep an encrypted backup,");
  console.log(`and activate the batch when the cards are paid for / handed over:  node cli/tundu.ts cards activate ${r.batch.id}`);
  return file;
}

export async function activate(batch: number): Promise<void> {
  const r = await adminApi("POST", `/admin/v1/batches/${batch}/activate`);
  console.log(`Batch ${batch}: ${r.activated} cards active.`);
}

export async function voidBatch(batch: number, reason: string): Promise<void> {
  const r = await adminApi("POST", `/admin/v1/batches/${batch}/void`, { reason });
  console.log(`Batch ${batch}: ${r.voided} unused cards voided (redeemed ones keep working).`);
}

export async function listBatches(): Promise<void> {
  const r = await adminApi("GET", "/admin/v1/batches");
  console.log("id   status    days  count  redeemed  void  created            label");
  for (const b of r.batches) {
    console.log(
      `${String(b.id).padEnd(4)} ${b.status.padEnd(9)} ${String(b.days).padStart(4)}  ${String(b.count).padStart(5)}  ${String(b.redeemed ?? 0).padStart(8)}  ${String(b.voided ?? 0).padStart(4)}  ${formatTime(b.created_at).slice(0, 16)}  ${b.label}`,
    );
  }
}

function code(raw: string): string {
  const c = normalizeCode(raw);
  if (!c) fail("that is not a valid code (check the characters; 0, 1, I and O are never used)");
  return formatCode(c);
}

export async function lookup(raw: string): Promise<void> {
  const r = await adminApi("POST", "/admin/v1/cards/lookup", { code: code(raw) });
  if (!r.card) {
    console.log("No such card. (Typed correctly? Every character must match.)");
    return;
  }
  const c = r.card;
  console.log(`Card ...${c.last4}: ${c.status}, ${c.days} days, batch ${c.batch_id} "${c.batch_label}" (${c.batch_status})`);
  if (c.redeemed_by) console.log(`Redeemed by ${c.redeemed_by} at ${formatTime(c.redeemed_at)}`);
  if (c.voided_at) console.log(`Voided at ${formatTime(c.voided_at)}: ${c.void_reason}`);
}

export async function voidCard(raw: string, reason: string, revoke: boolean): Promise<void> {
  const r = await adminApi("POST", "/admin/v1/cards/void", { code: code(raw), reason, revoke });
  console.log(`Card ${r.card}: ${r.voided ? "voided" : "was already void"}${r.revoked ? ", its days were taken back" : ""}.`);
}

export async function showDevice(id: string): Promise<void> {
  const r = await adminApi("GET", `/admin/v1/devices/${id}`);
  const d = r.device;
  const active = d.pass_until > r.now;
  console.log(`${d.id}  ${d.model ?? "?"}  Android API ${d.sdk ?? "?"}  app ${d.app_version ?? "?"}${d.blocked ? `  BLOCKED: ${d.blocked_reason}` : ""}`);
  console.log(`Enrolled ${formatTime(d.created_at)}, last seen ${formatTime(d.last_seen_at)}`);
  console.log(`Game Pass: ${active ? `active until ${formatTime(d.pass_until)}` : "not active"}${d.fail_count ? `; wrong codes today: ${d.fail_count}` : ""}`);
  for (const l of r.ledger) {
    console.log(`  ${formatTime(l.at)}  ${l.kind.padEnd(12)} ${String(Math.round(l.seconds / 86400)).padStart(5)} d  -> ${formatTime(l.pass_until_after)}  ${l.note ?? ""}`);
  }
  for (const e of r.events.slice(0, 10)) console.log(`  event ${formatTime(e.at)}  ${e.kind}  app ${e.app_version}  ${e.detail ?? ""}`);
}

export async function grant(id: string, days: number, reason: string): Promise<void> {
  const r = await adminApi("POST", `/admin/v1/devices/${id}/grant`, { days, reason });
  console.log(`${id}: Game Pass until ${formatTime(r.pass_until)}.`);
}

export async function block(id: string, blocked: boolean, reason: string): Promise<void> {
  await adminApi("POST", `/admin/v1/devices/${id}/block`, { blocked, reason });
  console.log(`${id}: ${blocked ? "blocked (Game Pass closes at its next check-in)" : "unblocked"}.`);
}

export async function transfer(from: string, to: string, reason: string, blockOld: boolean): Promise<void> {
  const r = await adminApi("POST", `/admin/v1/devices/${from}/transfer`, { to, reason, block: blockOld });
  for (const d of r.devices) console.log(`${d.id}: pass until ${formatTime(d.pass_until)}${d.blocked ? " (blocked)" : ""}`);
}

export async function stats(): Promise<void> {
  console.log(JSON.stringify(await adminApi("GET", "/admin/v1/stats"), null, 2));
}

export async function consistency(): Promise<void> {
  const r = await adminApi("GET", "/admin/v1/check");
  console.log(r.ok ? "Database consistent." : JSON.stringify(r, null, 2));
  if (!r.ok) process.exitCode = 1;
}
