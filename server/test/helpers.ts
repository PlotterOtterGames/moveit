import { env, SELF } from "cloudflare:test";
import { verifyDoc, type SignedDoc } from "../src/crypto.ts";
import { TEST_ADMIN_TOKEN, TEST_PASS_SPKI } from "./secrets.ts";

export interface Reply {
  status: number;
  body: any;
  headers: Headers;
}

export async function call(
  method: string,
  path: string,
  options: { body?: unknown; auth?: string; headers?: Record<string, string>; raw?: string; ip?: string } = {},
): Promise<Reply> {
  const headers: Record<string, string> = { "cf-connecting-ip": options.ip ?? randomIp(), ...options.headers };
  if (options.body !== undefined || options.raw !== undefined) headers["content-type"] ??= "application/json";
  if (options.auth) headers["authorization"] = "Bearer " + options.auth;
  const response = await SELF.fetch("https://api.tundu.al" + path, {
    method,
    headers,
    body: options.raw ?? (options.body === undefined ? undefined : JSON.stringify(options.body)),
  });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null, headers: response.headers };
}

export const admin = (method: string, path: string, body?: unknown) => call(method, path, { body, auth: TEST_ADMIN_TOKEN });

/** Every simulated box has its own address, like real households (the rate limits are per IP and per box). */
export function randomIp(): string {
  const b = crypto.getRandomValues(new Uint8Array(4));
  return `10.${b[0]}.${b[1]}.${b[2]}`;
}

export interface TestDevice {
  id: string;
  auth: string;
  ip: string;
}

export async function enroll(): Promise<TestDevice> {
  const ip = randomIp();
  const r = await call("POST", "/v1/devices", { body: { model: "TestBox", sdk: 30, app: 10000 }, ip });
  if (r.status !== 201) throw new Error("enroll failed: " + JSON.stringify(r.body));
  return { id: r.body.device, auth: `${r.body.device}.${r.body.secret}`, ip };
}

/** Creates a batch of `count` cards worth `days` days, activated unless told otherwise. Returns the codes. */
export async function cards(days: number, count: number, activate = true): Promise<{ batch: number; codes: string[] }> {
  const r = await admin("POST", "/admin/v1/batches", { label: "test", days, count });
  if (r.status !== 201) throw new Error("batch failed: " + JSON.stringify(r.body));
  if (activate) {
    const a = await admin("POST", `/admin/v1/batches/${r.body.batch.id}/activate`);
    if (a.status !== 200) throw new Error("activate failed: " + JSON.stringify(a.body));
  }
  return { batch: r.body.batch.id, codes: r.body.codes.map((c: { code: string }) => c.code) };
}

let counter = 0;
export function requestId(): string {
  return `req-${Date.now()}-${++counter}-${Math.random().toString(36).slice(2, 10)}`;
}

export async function redeem(device: TestDevice, code: string, request = requestId()): Promise<Reply> {
  return call("POST", "/v1/redeem", { auth: device.auth, body: { code, request }, ip: device.ip });
}

/** Verifies a Game Pass token with the test public key and returns its payload. */
export async function passPayload(doc: SignedDoc): Promise<{ type: string; v: number; device: string; pass_until: number; now: number; blocked: boolean }> {
  return (await verifyDoc(doc, TEST_PASS_SPKI)) as any;
}

export function nowSeconds(): number {
  return Math.floor(Date.now() / 1000);
}

export async function sql<T = Record<string, unknown>>(query: string, ...params: unknown[]): Promise<T[]> {
  return (await env.DB.prepare(query).bind(...params).all<T>()).results;
}
