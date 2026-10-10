// POST /v1/redeem: a box turns a gift card code into Game Pass days.
//
// Body: {"code": "ABCD-EFGH-JKLM-NPQ7", "request": "<random id the box keeps until it gets an answer>"}
// Answer: {"result": "ok" | "already_yours" | "used" | "not_active" | "void" | "invalid" | "limit",
//          "days": 30, "pass": <signed token>}
//
// Safety properties (each has a test in test/redeem.test.ts):
// - A card is redeemed once, ever: claiming it is a single conditional UPDATE inside one D1 transaction, so two
//   boxes racing for the same card can't both win, and a box redeeming two cards at once can't lose days.
// - Retrying is safe: the same request id gets the same "ok" again without adding days twice (lost answers on
//   bad Wi-Fi are normal).
// - Guessing is pointless: 75-bit codes, at most 10 wrong codes per box per 24 h, plus per-box and per-IP rate
//   limits. Codes that exist but can't be used ("used", "not_active") don't count as wrong.
// - Days stack: a new card extends from the current end date, never from today if the pass is still running.
// - The pass can't be pushed more than PASS_MAX_DAYS ahead (stops mistakes and abuse; the card stays unused).

import { authDevice } from "./auth.ts";
import { codeHash, importPepper, normalizeCode } from "./codes.ts";
import { clientIp, HttpError, json, now, rateLimit, readJson, str, type Env } from "./http.ts";
import { passToken } from "./pass.ts";

export const MAX_FAILS_PER_DAY = 10;
export const PASS_MAX_DAYS = 1830; // about 5 years
const REQUEST_ID = /^[A-Za-z0-9_-]{8,64}$/;

let pepperCache: { secret: string; key: Promise<CryptoKey> } | null = null;

export function pepper(env: Env): Promise<CryptoKey> {
  if (!pepperCache || pepperCache.secret !== env.CODE_PEPPER) {
    pepperCache = { secret: env.CODE_PEPPER, key: importPepper(env.CODE_PEPPER) };
  }
  return pepperCache.key;
}

type Result = "ok" | "already_yours" | "used" | "not_active" | "void" | "invalid" | "limit";

interface Outcome {
  status: string | null;
  redeemed_by: string | null;
  redeem_request: string | null;
  days: number | null;
  last4: string | null;
  pass_until: number;
  blocked: number;
}

export async function redeem(request: Request, env: Env): Promise<Response> {
  const device = await authDevice(request, env);
  if (device.blocked) throw new HttpError(403, "blocked");
  await rateLimit(env.LIMIT_REDEEM, device.id);
  await rateLimit(env.LIMIT_API, "ip:" + clientIp(request));
  const body = await readJson(request, 1024);
  const requestId = str(body, "request", { max: 64, pattern: REQUEST_ID });
  const t = now();

  if (device.fail_count >= MAX_FAILS_PER_DAY && t - device.fail_window_start < 86400) {
    throw new HttpError(429, "locked", { retry_after: device.fail_window_start + 86400 - t });
  }

  const code = normalizeCode(typeof body["code"] === "string" ? body["code"] : "");
  if (!code) {
    // The box checks the check character before sending, so this is not our app: count it.
    await countFailure(env, device.id, t);
    return json({ result: "invalid" satisfies Result, pass: await passToken(env, device, t) });
  }
  const hash = await codeHash(await pepper(env), code);
  const cap = PASS_MAX_DAYS * 86400;

  // One transaction. 'claimed' only exists between the first and fourth statement.
  const results = await env.DB.batch([
    env.DB.prepare(
      `UPDATE cards SET status = 'claimed', redeemed_by = ?1, redeemed_at = ?2, redeem_request = ?3
       WHERE code_hash = ?4 AND status = 'active'
         AND (SELECT MAX(pass_until, ?2) FROM devices WHERE id = ?1) + days * 86400 <= ?2 + ?5`,
    ).bind(device.id, t, requestId, hash, cap),
    env.DB.prepare(
      `INSERT INTO pass_ledger (device_id, kind, card_id, seconds, pass_until_before, pass_until_after, at, op, actor)
       SELECT d.id, 'redeem', c.id, c.days * 86400, d.pass_until, MAX(d.pass_until, ?2) + c.days * 86400, ?2, ?3, 'device'
       FROM cards c JOIN devices d ON d.id = c.redeemed_by
       WHERE c.code_hash = ?4 AND c.status = 'claimed' AND c.redeemed_by = ?1`,
    ).bind(device.id, t, requestId, hash),
    env.DB.prepare(
      `UPDATE devices SET pass_until = (
         SELECT l.pass_until_after FROM pass_ledger l JOIN cards c ON c.id = l.card_id
         WHERE c.code_hash = ?2 AND c.status = 'claimed' AND l.kind = 'redeem')
       WHERE id = ?1 AND EXISTS (SELECT 1 FROM cards WHERE code_hash = ?2 AND status = 'claimed' AND redeemed_by = ?1)`,
    ).bind(device.id, hash),
    env.DB.prepare(`UPDATE cards SET status = 'redeemed' WHERE code_hash = ?1 AND status = 'claimed'`).bind(hash),
    env.DB.prepare(
      `SELECT c.status, c.redeemed_by, c.redeem_request, c.days, c.last4, d.pass_until, d.blocked
       FROM devices d LEFT JOIN cards c ON c.code_hash = ?2 WHERE d.id = ?1`,
    ).bind(device.id, hash),
  ]);
  const row = results[4]!.results[0] as Outcome;

  let result: Result;
  if (row.status === null) result = "invalid";
  else if (row.status === "redeemed" && row.redeemed_by === device.id) result = row.redeem_request === requestId ? "ok" : "already_yours";
  else if (row.status === "redeemed") result = "used";
  else if (row.status === "created") result = "not_active";
  else if (row.status === "void") result = "void";
  else result = "limit"; // still 'active': the claim was refused by the PASS_MAX_DAYS cap

  if (result === "invalid") await countFailure(env, device.id, t);
  console.log(JSON.stringify({ event: "redeem", device: device.id, result, card: row.last4 }));

  const pass = await passToken(env, { id: device.id, pass_until: row.pass_until, blocked: row.blocked }, t);
  return json(result === "ok" ? { result, days: row.days, pass } : { result, pass });
}

async function countFailure(env: Env, deviceId: string, t: number): Promise<void> {
  await env.DB.prepare(
    `UPDATE devices SET
       fail_count = CASE WHEN ?2 - fail_window_start >= 86400 THEN 1 ELSE fail_count + 1 END,
       fail_window_start = CASE WHEN ?2 - fail_window_start >= 86400 THEN ?2 ELSE fail_window_start END
     WHERE id = ?1`,
  )
    .bind(deviceId, t)
    .run();
}
