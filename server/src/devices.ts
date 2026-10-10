// Box endpoints that aren't redemption: enrolment, status (token refresh) and health events.

import { authDevice } from "./auth.ts";
import { randomId } from "./codes.ts";
import { b64url, sha256Hex } from "./crypto.ts";
import {
  clientIp,
  HttpError,
  int,
  json,
  now,
  optInt,
  optStr,
  PLAIN_TEXT,
  rateLimit,
  readJson,
  str,
  type Env,
} from "./http.ts";
import { passToken } from "./pass.ts";

/**
 * POST /v1/devices: a box without an identity gets one.
 * Body (all optional, for support and fleet stats): {"model": "TX66", "sdk": 30, "app": 10203}
 * Answer: {"device": "d_…", "secret": "…", "pass": <signed token>}
 */
export async function enroll(request: Request, env: Env): Promise<Response> {
  await rateLimit(env.LIMIT_ENROLL, clientIp(request));
  const body = await readJson(request, 1024);
  const model = optStr(body, "model", { max: 64, pattern: PLAIN_TEXT });
  const sdk = optInt(body, "sdk", { min: 1, max: 1000 });
  const app = optInt(body, "app", { min: 0, max: 2_100_000_000 });
  const t = now();

  // Circuit breaker against scripted mass enrolment: real growth is far below this.
  const max = Number(env.ENROLL_DAILY_MAX ?? "1000");
  const recent = await env.DB.prepare("SELECT COUNT(*) AS n FROM devices WHERE created_at > ?").bind(t - 86400).first<{ n: number }>();
  if ((recent?.n ?? 0) >= max) {
    console.warn(JSON.stringify({ event: "enroll_paused", recent: recent?.n }));
    throw new HttpError(503, "enrol_paused", { retry_after: 3600 });
  }

  const id = "d_" + randomId(20);
  const secret = b64url(crypto.getRandomValues(new Uint8Array(32)));
  // Sign first: if the signing key were broken, no half-made device is left behind.
  const pass = await passToken(env, { id, pass_until: 0, blocked: 0 }, t);
  await env.DB.prepare(
    "INSERT INTO devices (id, secret_hash, created_at, last_seen_at, model, sdk, app_version) VALUES (?, ?, ?, ?, ?, ?, ?)",
  )
    .bind(id, await sha256Hex(secret), t, t, model, sdk, app)
    .run();
  console.log(JSON.stringify({ event: "enroll", device: id, model, sdk, app }));
  return json({ device: id, secret, pass }, 201);
}

/**
 * POST /v1/status: the box checks in (at start, then about every 6 hours) and gets a fresh token.
 * Body: {"app": 10203, "sdk": 30}
 */
export async function status(request: Request, env: Env): Promise<Response> {
  const device = await authDevice(request, env);
  await rateLimit(env.LIMIT_API, device.id);
  const body = await readJson(request, 1024);
  const app = int(body, "app", { min: 0, max: 2_100_000_000 });
  const sdk = optInt(body, "sdk", { min: 1, max: 1000 });
  const t = now();
  await env.DB.prepare("UPDATE devices SET last_seen_at = ?, app_version = ?, sdk = COALESCE(?, sdk) WHERE id = ?")
    .bind(t, app, sdk, device.id)
    .run();
  return json({ pass: await passToken(env, device, t) });
}

export const EVENT_KINDS = [
  "boot_ok", // reached the home screen
  "crash", // Java/C# crash (from ApplicationExitInfo on the next start)
  "crash_native", // native crash (Unity, MediaPipe, GPU driver)
  "anr", // app not responding
  "update_downloaded",
  "update_installed",
  "update_failed",
  "safe_mode", // crashed repeatedly: updater ran without starting the games
  "firmware_installed",
  "firmware_failed",
] as const;

/**
 * POST /v1/events: health reports, to watch a staged rollout ("does 1.2.3 crash more than 1.2.2?").
 * Body: {"app": 10203, "events": [{"kind": "crash_native", "at": 1760100000, "detail": "SIGSEGV libunity.so"}]}
 * No personal data: kind, time, app version and a short technical detail.
 */
export async function events(request: Request, env: Env): Promise<Response> {
  const device = await authDevice(request, env);
  await rateLimit(env.LIMIT_API, device.id);
  const body = await readJson(request, 16 * 1024);
  const app = int(body, "app", { min: 0, max: 2_100_000_000 });
  const list = body["events"];
  if (!Array.isArray(list) || list.length < 1 || list.length > 20) throw new HttpError(400, "bad_field", { field: "events" });
  const t = now();
  const statements = list.map((item: unknown) => {
    if (typeof item !== "object" || item === null) throw new HttpError(400, "bad_field", { field: "events" });
    const e = item as Record<string, unknown>;
    const kind = str(e, "kind", { max: 32 });
    if (!(EVENT_KINDS as readonly string[]).includes(kind)) throw new HttpError(400, "bad_field", { field: "kind" });
    // A box's clock can be anything: keep its time only if it's plausible.
    const at = optInt(e, "at", { min: 0, max: 4_000_000_000 });
    const when = at !== null && at > t - 90 * 86400 && at <= t + 300 ? at : t;
    const detail = optStr(e, "detail", { max: 300, pattern: PLAIN_TEXT });
    return env.DB.prepare(
      "INSERT INTO events (device_id, at, received_at, app_version, kind, detail) VALUES (?, ?, ?, ?, ?, ?)",
    ).bind(device.id, when, t, app, kind, detail);
  });
  await env.DB.batch(statements);
  return json({ ok: true, stored: statements.length });
}
