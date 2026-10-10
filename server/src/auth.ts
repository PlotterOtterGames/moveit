// Who is calling.
// - Boxes: "Authorization: Bearer <device id>.<device secret>". The secret (256 random bits) is made at enrolment
//   and only its SHA-256 is stored. Plain SHA-256 is enough for a random 256-bit secret (no password stretching
//   needed); it is compared in constant time.
// - Admin (the CLI on the laptop): "Authorization: Bearer <admin token>", checked against the SHA-256 in the
//   ADMIN_TOKEN_SHA256 secret, optionally only from ADMIN_IPS. No secret = admin API off (fails closed).

import { safeEqual, sha256Hex } from "./crypto.ts";
import { clientIp, HttpError, type Env } from "./http.ts";

export const DEVICE_ID = /^d_[2-9A-HJ-NP-Z]{20}$/;
const DEVICE_AUTH = /^Bearer (d_[2-9A-HJ-NP-Z]{20})\.([A-Za-z0-9_-]{43})$/;

export interface DeviceRow {
  id: string;
  secret_hash: string;
  pass_until: number;
  blocked: number;
  fail_count: number;
  fail_window_start: number;
  app_version: number | null;
}

/** The calling box, or 401. Blocked boxes are returned too: each endpoint decides what they may do. */
export async function authDevice(request: Request, env: Env): Promise<DeviceRow> {
  const match = DEVICE_AUTH.exec(request.headers.get("authorization") ?? "");
  if (!match) throw new HttpError(401, "unauthorized");
  const [, id, secret] = match;
  const row = await env.DB.prepare(
    "SELECT id, secret_hash, pass_until, blocked, fail_count, fail_window_start, app_version FROM devices WHERE id = ?",
  )
    .bind(id)
    .first<DeviceRow>();
  // Hash even when the device is unknown, so both cases take the same time.
  const hash = await sha256Hex(secret!);
  if (!row || !safeEqual(hash, row.secret_hash)) throw new HttpError(401, "unauthorized");
  return row;
}

export async function authAdmin(request: Request, env: Env): Promise<void> {
  const expected = (env.ADMIN_TOKEN_SHA256 ?? "").trim().toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(expected)) throw new HttpError(403, "admin_disabled");
  const allowed = (env.ADMIN_IPS ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  if (allowed.length > 0 && !allowed.includes(clientIp(request))) throw new HttpError(403, "forbidden");
  const header = request.headers.get("authorization") ?? "";
  const token = header.startsWith("Bearer ") ? header.slice(7) : "";
  if (token.length < 32 || token.length > 200 || !safeEqual(await sha256Hex(token), expected)) {
    throw new HttpError(401, "unauthorized");
  }
}
