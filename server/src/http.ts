// Small HTTP helpers: JSON responses, a size-capped JSON body reader, input validation. No dependencies.

export interface Env {
  DB: D1Database;
  /** Rate limiters (wrangler.jsonc "ratelimits"). Optional so a missing binding never takes the API down. */
  LIMIT_ENROLL?: RateLimit;
  LIMIT_REDEEM?: RateLimit;
  LIMIT_API?: RateLimit;
  /** Secret: P-256 private JWK with a kid, signs Game Pass tokens. */
  PASS_SIGNING_KEY: string;
  /** Secret: base64 of >= 32 random bytes, the HMAC key for code hashes. Never change it: old codes stop working. */
  CODE_PEPPER: string;
  /** Secret: hex SHA-256 of the admin token. Unset = admin API off. */
  ADMIN_TOKEN_SHA256?: string;
  /** Optional comma-separated IPs allowed to use the admin API. Empty = any IP (the token still applies). */
  ADMIN_IPS?: string;
  /** Circuit breaker: new devices per 24 h before enrolment pauses (default 1000). */
  ENROLL_DAILY_MAX?: string;
}

export class HttpError extends Error {
  status: number;
  code: string;
  extra: Record<string, unknown> | undefined;
  constructor(status: number, code: string, extra?: Record<string, unknown>) {
    super(code);
    this.status = status;
    this.code = code;
    this.extra = extra;
  }
}

const SECURITY_HEADERS = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
  "x-content-type-options": "nosniff",
};

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { ...SECURITY_HEADERS, ...headers } });
}

export function errorResponse(e: HttpError): Response {
  const headers: Record<string, string> = {};
  if (typeof e.extra?.["retry_after"] === "number") headers["retry-after"] = String(e.extra["retry_after"]);
  return json({ error: e.code, ...e.extra }, e.status, headers);
}

export function now(): number {
  return Math.floor(Date.now() / 1000);
}

export function clientIp(request: Request): string {
  return request.headers.get("cf-connecting-ip") ?? "unknown";
}

export type Body = Record<string, unknown>;

/** Reads a JSON object body, refusing anything over maxBytes (checked while streaming, not just Content-Length). */
export async function readJson(request: Request, maxBytes = 4096): Promise<Body> {
  const type = request.headers.get("content-type") ?? "";
  if (!type.toLowerCase().startsWith("application/json")) throw new HttpError(415, "json_required");
  const declared = Number(request.headers.get("content-length") ?? "0");
  if (declared > maxBytes) throw new HttpError(413, "too_large");
  if (!request.body) throw new HttpError(400, "empty_body");
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > maxBytes) {
      await reader.cancel();
      throw new HttpError(413, "too_large");
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const c of chunks) {
    bytes.set(c, offset);
    offset += c.byteLength;
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(bytes));
  } catch {
    throw new HttpError(400, "bad_json");
  }
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) throw new HttpError(400, "bad_json");
  return parsed as Body;
}

interface IntRule {
  min: number;
  max: number;
}

export function int(body: Body, key: string, rule: IntRule): number {
  const v = body[key];
  if (typeof v !== "number" || !Number.isSafeInteger(v) || v < rule.min || v > rule.max) {
    throw new HttpError(400, "bad_field", { field: key });
  }
  return v;
}

export function optInt(body: Body, key: string, rule: IntRule): number | null {
  return body[key] === undefined || body[key] === null ? null : int(body, key, rule);
}

interface StrRule {
  max: number;
  min?: number;
  pattern?: RegExp;
}

export function str(body: Body, key: string, rule: StrRule): string {
  const v = body[key];
  if (typeof v !== "string" || v.length < (rule.min ?? 1) || v.length > rule.max || (rule.pattern && !rule.pattern.test(v))) {
    throw new HttpError(400, "bad_field", { field: key });
  }
  return v;
}

export function optStr(body: Body, key: string, rule: StrRule): string | null {
  return body[key] === undefined || body[key] === null ? null : str(body, key, rule);
}

export function bool(body: Body, key: string): boolean {
  const v = body[key];
  if (typeof v !== "boolean") throw new HttpError(400, "bad_field", { field: key });
  return v;
}

/** Printable text without control characters (labels, notes, reasons, device model names). */
export const PLAIN_TEXT = /^[^\u0000-\u001f\u007f]*$/;

export async function rateLimit(limiter: RateLimit | undefined, key: string): Promise<void> {
  if (!limiter) return;
  const { success } = await limiter.limit({ key });
  if (!success) throw new HttpError(429, "slow_down", { retry_after: 60 });
}
