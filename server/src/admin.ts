// Admin API, used only by the CLI on the laptop (server/cli/tundu.ts). Every write is recorded in admin_log.
// Gift card codes appear in exactly one place: the answer to "create batch", which the CLI writes to a CSV for
// the printer. They are never stored or logged here.

import { authAdmin, DEVICE_ID } from "./auth.ts";
import { codeHash, formatCode, generateCode, normalizeCode, QR_PREFIX } from "./codes.ts";
import { bool, clientIp, HttpError, int, json, now, optStr, PLAIN_TEXT, readJson, str, type Env } from "./http.ts";
import { MAX_FAILS_PER_DAY, PASS_MAX_DAYS, pepper } from "./redeem.ts";

const LABEL = { max: 100, pattern: PLAIN_TEXT };
const REASON = { max: 300, pattern: PLAIN_TEXT };
export const MAX_BATCH = 5000;

export async function admin(request: Request, env: Env, path: string): Promise<Response> {
  await authAdmin(request, env);
  const method = request.method;
  let m: RegExpExecArray | null;

  if (method === "GET" && path === "/admin/v1/batches") return listBatches(env);
  if (method === "POST" && path === "/admin/v1/batches") return createBatch(request, env);
  if (method === "POST" && (m = /^\/admin\/v1\/batches\/(\d{1,9})\/(activate|void)$/.exec(path))) {
    return m[2] === "activate" ? activateBatch(request, env, Number(m[1])) : voidBatch(request, env, Number(m[1]));
  }
  if (method === "POST" && path === "/admin/v1/cards/lookup") return lookupCard(request, env);
  if (method === "POST" && path === "/admin/v1/cards/void") return voidCard(request, env);
  if ((m = /^\/admin\/v1\/devices\/(d_[2-9A-HJ-NP-Z]{20})(?:\/(grant|block|transfer))?$/.exec(path))) {
    const id = m[1]!;
    if (method === "GET" && !m[2]) return showDevice(env, id);
    if (method === "POST" && m[2] === "grant") return grant(request, env, id);
    if (method === "POST" && m[2] === "block") return block(request, env, id);
    if (method === "POST" && m[2] === "transfer") return transfer(request, env, id);
  }
  if (method === "GET" && path === "/admin/v1/stats") return stats(env);
  if (method === "GET" && path === "/admin/v1/check") return check(env);
  throw new HttpError(404, "not_found");
}

async function log(request: Request, env: Env, action: string, detail: unknown): Promise<void> {
  await env.DB.prepare("INSERT INTO admin_log (at, action, detail, ip) VALUES (?, ?, ?, ?)")
    .bind(now(), action, JSON.stringify(detail), clientIp(request))
    .run();
}

function opId(): string {
  return "op_" + crypto.randomUUID();
}

// ---------- gift card batches ----------

async function listBatches(env: Env): Promise<Response> {
  const { results } = await env.DB.prepare(
    `SELECT b.id, b.label, b.days, b.count, b.status, b.created_at, b.activated_at, b.voided_at, b.note,
            SUM(c.status = 'redeemed') AS redeemed, SUM(c.status = 'void') AS voided
     FROM batches b LEFT JOIN cards c ON c.batch_id = b.id GROUP BY b.id ORDER BY b.id DESC LIMIT 200`,
  ).all();
  return json({ batches: results });
}

/**
 * POST /admin/v1/batches {"label": "Shop X, Oct 2026", "days": 30, "count": 500, "note": "…"}
 * Creates the cards inactive. Answers with the codes, once.
 */
async function createBatch(request: Request, env: Env): Promise<Response> {
  const body = await readJson(request, 2048);
  const label = str(body, "label", LABEL);
  const days = int(body, "days", { min: 1, max: 3660 });
  const count = int(body, "count", { min: 1, max: MAX_BATCH });
  const note = optStr(body, "note", REASON);
  const t = now();

  const batch = await env.DB.prepare(
    "INSERT INTO batches (label, days, count, status, created_at, note) VALUES (?, ?, ?, 'creating', ?, ?) RETURNING id",
  )
    .bind(label, days, count, t, note)
    .first<{ id: number }>();
  const batchId = batch!.id;

  const key = await pepper(env);
  const codes = new Set<string>();
  while (codes.size < count) codes.add(generateCode());
  const rows: [string, string][] = [];
  for (const code of codes) rows.push([await codeHash(key, code), code.slice(-4)]);

  // All cards and the status change in one transaction (500 cards per statement keeps each bound value small).
  // If anything fails, the batch stays 'creating' with no cards, can never be activated, and the codes were never
  // shown to anyone.
  const inserts = [];
  for (let i = 0; i < rows.length; i += 500) {
    inserts.push(
      env.DB.prepare(
        `INSERT INTO cards (code_hash, last4, batch_id, days, status)
         SELECT json_extract(value, '$[0]'), json_extract(value, '$[1]'), ?2, ?3, 'created' FROM json_each(?1)`,
      ).bind(JSON.stringify(rows.slice(i, i + 500)), batchId, days),
    );
  }
  await env.DB.batch([
    ...inserts,
    env.DB.prepare("UPDATE batches SET status = 'created' WHERE id = ? AND status = 'creating'").bind(batchId),
  ]);
  await log(request, env, "batch_create", { batch: batchId, label, days, count });
  return json(
    {
      batch: { id: batchId, label, days, count, status: "created" },
      codes: [...codes].map((c) => ({ code: formatCode(c), qr: QR_PREFIX + formatCode(c) })),
    },
    201,
  );
}

async function activateBatch(request: Request, env: Env, id: number): Promise<Response> {
  const t = now();
  const [b, c] = await env.DB.batch([
    env.DB.prepare("UPDATE batches SET status = 'active', activated_at = ? WHERE id = ? AND status = 'created'").bind(t, id),
    env.DB.prepare(
      `UPDATE cards SET status = 'active' WHERE batch_id = ?1 AND status = 'created'
         AND EXISTS (SELECT 1 FROM batches WHERE id = ?1 AND status = 'active')`,
    ).bind(id),
  ]);
  if (b!.meta.changes !== 1) throw new HttpError(409, "batch_not_created");
  await log(request, env, "batch_activate", { batch: id, cards: c!.meta.changes });
  return json({ ok: true, batch: id, activated: c!.meta.changes });
}

async function voidBatch(request: Request, env: Env, id: number): Promise<Response> {
  const body = await readJson(request, 1024);
  const reason = str(body, "reason", REASON);
  const t = now();
  const [b, c] = await env.DB.batch([
    env.DB.prepare(
      "UPDATE batches SET status = 'void', voided_at = ?, note = COALESCE(note || ' | ', '') || ? WHERE id = ? AND status != 'void'",
    ).bind(t, "void: " + reason, id),
    env.DB.prepare(
      "UPDATE cards SET status = 'void', voided_at = ?, void_reason = ? WHERE batch_id = ? AND status IN ('created', 'active')",
    ).bind(t, reason, id),
  ]);
  if (b!.meta.changes !== 1) throw new HttpError(409, "batch_missing_or_void");
  await log(request, env, "batch_void", { batch: id, cards: c!.meta.changes, reason });
  return json({ ok: true, batch: id, voided: c!.meta.changes });
}

// ---------- single cards (support) ----------

async function cardByCode(env: Env, raw: unknown) {
  const code = normalizeCode(typeof raw === "string" ? raw : "");
  if (!code) throw new HttpError(400, "bad_code");
  const hash = await codeHash(await pepper(env), code);
  const card = await env.DB.prepare(
    `SELECT c.id, c.last4, c.batch_id, c.days, c.status, c.redeemed_by, c.redeemed_at, c.voided_at, c.void_reason,
            b.label AS batch_label, b.status AS batch_status
     FROM cards c JOIN batches b ON b.id = c.batch_id WHERE c.code_hash = ?`,
  )
    .bind(hash)
    .first<{ id: number; status: string; days: number; redeemed_by: string | null }>();
  return card;
}

async function lookupCard(request: Request, env: Env): Promise<Response> {
  const body = await readJson(request, 1024);
  const card = await cardByCode(env, body["code"]);
  return json({ card: card ?? null });
}

/** POST /admin/v1/cards/void {"code": "…", "reason": "…", "revoke": true}: revoke = also take back its days. */
async function voidCard(request: Request, env: Env): Promise<Response> {
  const body = await readJson(request, 1024);
  const reason = str(body, "reason", REASON);
  const revoke = body["revoke"] === undefined ? false : bool(body, "revoke");
  const card = await cardByCode(env, body["code"]);
  if (!card) throw new HttpError(404, "card_not_found");
  const t = now();
  const statements = [
    env.DB.prepare("UPDATE cards SET status = 'void', voided_at = ?, void_reason = ? WHERE id = ? AND status != 'void'").bind(
      t,
      reason,
      card.id,
    ),
  ];
  // A card that was redeemed keeps redeemed_by after being voided, so its days can still be taken back later,
  // but never twice.
  if (revoke && card.redeemed_by) {
    const op = opId();
    const secs = card.days * 86400;
    statements.push(
      env.DB.prepare(
        `INSERT INTO pass_ledger (device_id, kind, card_id, seconds, pass_until_before, pass_until_after, at, op, actor, note)
         SELECT id, 'revoke', ?2, -?3, pass_until, MAX(pass_until - ?3, 0), ?4, ?5, 'admin', ?6 FROM devices
         WHERE id = ?1 AND NOT EXISTS (SELECT 1 FROM pass_ledger WHERE card_id = ?2 AND kind = 'revoke')`,
      ).bind(card.redeemed_by, card.id, secs, t, op, reason),
      env.DB.prepare(
        `UPDATE devices SET pass_until = (SELECT pass_until_after FROM pass_ledger WHERE op = ?2)
         WHERE id = ?1 AND EXISTS (SELECT 1 FROM pass_ledger WHERE op = ?2)`,
      ).bind(card.redeemed_by, op),
    );
  }
  const [voidResult, revokeResult] = await env.DB.batch(statements);
  const voided = voidResult!.meta.changes === 1;
  const revoked = (revokeResult?.meta.changes ?? 0) === 1;
  if (!voided && !revoked) throw new HttpError(409, "already_void");
  await log(request, env, "card_void", { card: card.id, reason, voided, revoked, device: card.redeemed_by });
  return json({ ok: true, card: card.id, voided, revoked });
}

// ---------- devices (support) ----------

async function showDevice(env: Env, id: string): Promise<Response> {
  const [device, ledger, cards, events] = await env.DB.batch([
    env.DB.prepare(
      `SELECT id, created_at, last_seen_at, model, sdk, app_version, pass_until, blocked, blocked_reason,
              fail_count, fail_window_start FROM devices WHERE id = ?`,
    ).bind(id),
    env.DB.prepare("SELECT * FROM pass_ledger WHERE device_id = ? ORDER BY id DESC LIMIT 50").bind(id),
    env.DB.prepare("SELECT id, last4, batch_id, days, status, redeemed_at FROM cards WHERE redeemed_by = ? ORDER BY id DESC LIMIT 50").bind(id),
    env.DB.prepare("SELECT at, received_at, app_version, kind, detail FROM events WHERE device_id = ? ORDER BY id DESC LIMIT 30").bind(id),
  ]);
  if (device!.results.length === 0) throw new HttpError(404, "device_not_found");
  return json({ device: device!.results[0], ledger: ledger!.results, cards: cards!.results, events: events!.results, now: now() });
}

/** POST …/grant {"days": 30, "reason": "box came with a month"}; negative days take time away. */
async function grant(request: Request, env: Env, id: string): Promise<Response> {
  const body = await readJson(request, 1024);
  const days = int(body, "days", { min: -3660, max: 3660 });
  if (days === 0) throw new HttpError(400, "bad_field", { field: "days" });
  const reason = str(body, "reason", REASON);
  const t = now();
  const secs = days * 86400;
  const op = opId();
  const [ins] = await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO pass_ledger (device_id, kind, card_id, seconds, pass_until_before, pass_until_after, at, op, actor, note)
       SELECT id, CASE WHEN ?2 > 0 THEN 'grant' ELSE 'revoke' END, NULL, ?2, pass_until,
              CASE WHEN ?2 > 0 THEN MAX(pass_until, ?3) + ?2 ELSE MAX(pass_until + ?2, 0) END, ?3, ?4, 'admin', ?5
       FROM devices WHERE id = ?1 AND (?2 <= 0 OR MAX(pass_until, ?3) + ?2 <= ?3 + ?6)`,
    ).bind(id, secs, t, op, reason, PASS_MAX_DAYS * 86400),
    env.DB.prepare(
      `UPDATE devices SET pass_until = (SELECT pass_until_after FROM pass_ledger WHERE op = ?2)
       WHERE id = ?1 AND EXISTS (SELECT 1 FROM pass_ledger WHERE op = ?2)`,
    ).bind(id, op),
  ]);
  if (ins!.meta.changes !== 1) {
    const exists = await env.DB.prepare("SELECT 1 FROM devices WHERE id = ?").bind(id).first();
    throw new HttpError(exists ? 409 : 404, exists ? "limit" : "device_not_found");
  }
  await log(request, env, "grant", { device: id, days, reason });
  const row = await env.DB.prepare("SELECT pass_until FROM devices WHERE id = ?").bind(id).first<{ pass_until: number }>();
  return json({ ok: true, device: id, pass_until: row!.pass_until });
}

async function block(request: Request, env: Env, id: string): Promise<Response> {
  const body = await readJson(request, 1024);
  const blocked = bool(body, "blocked");
  const reason = str(body, "reason", REASON);
  const r = await env.DB.prepare("UPDATE devices SET blocked = ?, blocked_reason = ? WHERE id = ?")
    .bind(blocked ? 1 : 0, blocked ? reason : null, id)
    .run();
  if (r.meta.changes !== 1) throw new HttpError(404, "device_not_found");
  await log(request, env, blocked ? "block" : "unblock", { device: id, reason });
  return json({ ok: true, device: id, blocked });
}

/**
 * POST …/transfer {"to": "d_…", "block": true, "reason": "replaced under warranty"}: moves the remaining pass
 * time to another box (the remaining time is added on top of the other box's own).
 */
async function transfer(request: Request, env: Env, from: string): Promise<Response> {
  const body = await readJson(request, 1024);
  const to = str(body, "to", { max: 22, pattern: DEVICE_ID });
  const blockOld = body["block"] === undefined ? false : bool(body, "block");
  const reason = str(body, "reason", REASON);
  if (to === from) throw new HttpError(400, "same_device");
  const t = now();
  const op = opId();
  const statements = [
    env.DB.prepare(
      `INSERT INTO pass_ledger (device_id, kind, card_id, seconds, pass_until_before, pass_until_after, at, op, actor, note)
       SELECT t.id, 'transfer_in', NULL, MAX(f.pass_until - ?3, 0), t.pass_until, MAX(t.pass_until, ?3) + MAX(f.pass_until - ?3, 0),
              ?3, ?4, 'admin', 'from ' || f.id || ': ' || ?5
       FROM devices f JOIN devices t ON t.id = ?2 WHERE f.id = ?1`,
    ).bind(from, to, t, op, reason),
    env.DB.prepare(
      `INSERT INTO pass_ledger (device_id, kind, card_id, seconds, pass_until_before, pass_until_after, at, op, actor, note)
       SELECT f.id, 'transfer_out', NULL, -MAX(f.pass_until - ?3, 0), f.pass_until, MIN(f.pass_until, ?3), ?3, ?4, 'admin', 'to ' || ?2 || ': ' || ?5
       FROM devices f WHERE f.id = ?1 AND EXISTS (SELECT 1 FROM pass_ledger WHERE op = ?4)`,
    ).bind(from, to, t, op, reason),
    env.DB.prepare(
      "UPDATE devices SET pass_until = (SELECT pass_until_after FROM pass_ledger WHERE op = ?2 AND kind = 'transfer_in') WHERE id = ?1 AND EXISTS (SELECT 1 FROM pass_ledger WHERE op = ?2)",
    ).bind(to, op),
    env.DB.prepare(
      "UPDATE devices SET pass_until = (SELECT pass_until_after FROM pass_ledger WHERE op = ?2 AND kind = 'transfer_out') WHERE id = ?1 AND EXISTS (SELECT 1 FROM pass_ledger WHERE op = ?2)",
    ).bind(from, op),
  ];
  if (blockOld) {
    statements.push(
      env.DB.prepare(
        "UPDATE devices SET blocked = 1, blocked_reason = ?2 WHERE id = ?1 AND EXISTS (SELECT 1 FROM pass_ledger WHERE op = ?3)",
      ).bind(from, "transferred to " + to + ": " + reason, op),
    );
  }
  const [inserted] = await env.DB.batch(statements);
  if (inserted!.meta.changes !== 1) throw new HttpError(404, "device_not_found");
  await log(request, env, "transfer", { from, to, reason, blocked: blockOld });
  const rows = await env.DB.prepare("SELECT id, pass_until, blocked FROM devices WHERE id IN (?, ?)").bind(from, to).all();
  return json({ ok: true, devices: rows.results });
}

// ---------- fleet health ----------

async function stats(env: Env): Promise<Response> {
  const t = now();
  const [totals, versions, events, redemptions, locked] = await env.DB.batch([
    env.DB.prepare(
      `SELECT COUNT(*) AS devices, SUM(last_seen_at > ?1 - 7 * 86400) AS seen_7d, SUM(created_at > ?1 - 86400) AS enrolled_24h,
              SUM(pass_until > ?1) AS pass_active, SUM(blocked) AS blocked FROM devices`,
    ).bind(t),
    env.DB.prepare(
      "SELECT app_version, COUNT(*) AS devices FROM devices WHERE last_seen_at > ? GROUP BY app_version ORDER BY app_version DESC",
    ).bind(t - 7 * 86400),
    env.DB.prepare(
      `SELECT app_version, kind, COUNT(*) AS n, COUNT(DISTINCT device_id) AS devices FROM events
       WHERE received_at > ? GROUP BY app_version, kind ORDER BY app_version DESC, kind`,
    ).bind(t - 86400),
    env.DB.prepare("SELECT COUNT(*) AS n, COALESCE(SUM(seconds), 0) / 86400 AS days FROM pass_ledger WHERE kind = 'redeem' AND at > ?").bind(
      t - 7 * 86400,
    ),
    env.DB.prepare(
      "SELECT COALESCE(SUM(fail_count), 0) AS wrong_codes_24h, SUM(fail_count >= ?2) AS boxes_locked FROM devices WHERE fail_window_start > ?1 - 86400",
    ).bind(t, MAX_FAILS_PER_DAY),
  ]);
  return json({
    now: t,
    totals: totals!.results[0],
    versions_seen_7d: versions!.results,
    events_24h: events!.results,
    redemptions_7d: redemptions!.results[0],
    wrong_codes: locked!.results[0],
  });
}

/**
 * GET /admin/v1/check: consistency check (run it after a restore, or weekly). Every list should be empty.
 */
async function check(env: Env): Promise<Response> {
  const [pass, orphanCards, claimed, creating] = await env.DB.batch([
    env.DB.prepare(
      `SELECT d.id, d.pass_until, l.pass_until_after FROM devices d
       LEFT JOIN pass_ledger l ON l.id = (SELECT MAX(id) FROM pass_ledger WHERE device_id = d.id)
       WHERE d.pass_until != COALESCE(l.pass_until_after, 0) LIMIT 100`,
    ),
    env.DB.prepare(
      `SELECT c.id FROM cards c WHERE c.status = 'redeemed'
       AND NOT EXISTS (SELECT 1 FROM pass_ledger l WHERE l.card_id = c.id AND l.kind = 'redeem') LIMIT 100`,
    ),
    env.DB.prepare("SELECT id FROM cards WHERE status = 'claimed' LIMIT 100"),
    env.DB.prepare("SELECT id, label, created_at FROM batches WHERE status = 'creating' LIMIT 100"),
  ]);
  const report = {
    pass_until_mismatch: pass!.results,
    redeemed_without_ledger: orphanCards!.results,
    stuck_claimed: claimed!.results,
    unfinished_batches: creating!.results,
  };
  const ok = Object.values(report).every((list) => list.length === 0);
  return json({ ok, ...report });
}
