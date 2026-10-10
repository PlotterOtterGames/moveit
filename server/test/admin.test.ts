import { env } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import { admin as adminRoutes } from "../src/admin.ts";
import { HttpError } from "../src/http.ts";
import { admin, call, cards, enroll, nowSeconds, passPayload, redeem, sql } from "./helpers.ts";
import { TEST_ADMIN_TOKEN } from "./secrets.ts";

const DAY = 86400;

describe("admin access", () => {
  it("needs the admin token", async () => {
    expect((await call("GET", "/admin/v1/stats")).status).toBe(401);
    expect((await call("GET", "/admin/v1/stats", { auth: "x".repeat(43) })).status).toBe(401);
    expect((await admin("GET", "/admin/v1/stats")).status).toBe(200);
  });

  it("is off when no token hash is configured, and can be limited to listed IPs", async () => {
    const request = (ip: string) =>
      new Request("https://api.tundu.al/admin/v1/stats", { headers: { authorization: "Bearer " + TEST_ADMIN_TOKEN, "cf-connecting-ip": ip } });
    await expect(adminRoutes(request("1.2.3.4"), { ...env, ADMIN_TOKEN_SHA256: "" }, "/admin/v1/stats")).rejects.toMatchObject({
      status: 403,
      code: "admin_disabled",
    });
    const limited = { ...env, ADMIN_IPS: "5.6.7.8, 9.9.9.9" };
    await expect(adminRoutes(request("1.2.3.4"), limited, "/admin/v1/stats")).rejects.toBeInstanceOf(HttpError);
    expect((await adminRoutes(request("9.9.9.9"), limited, "/admin/v1/stats")).status).toBe(200);
  });
});

describe("gift card batches", () => {
  it("creates inactive cards, shows each code once and stores only hashes", async () => {
    const r = await admin("POST", "/admin/v1/batches", { label: "Shop Tirana Oct", days: 30, count: 50, note: "first run" });
    expect(r.status).toBe(201);
    expect(r.body.batch).toMatchObject({ days: 30, count: 50, status: "created" });
    const codes: string[] = r.body.codes.map((c: { code: string }) => c.code);
    expect(new Set(codes).size).toBe(50);
    expect(codes[0]).toMatch(/^[2-9A-HJ-NP-Z]{4}(-[2-9A-HJ-NP-Z]{4}){3}$/);
    expect(r.body.codes[0].qr).toBe("https://tundu.al/kod#" + codes[0]);

    const rows = await sql<{ code_hash: string; last4: string; status: string }>("SELECT code_hash, last4, status FROM cards WHERE batch_id = ?", r.body.batch.id);
    expect(rows).toHaveLength(50);
    const dump = JSON.stringify(rows);
    for (const c of codes) expect(dump).not.toContain(c.replace(/-/g, ""));
    expect(rows.every((row) => row.status === "created")).toBe(true);

    const log = await sql<{ action: string; detail: string }>("SELECT action, detail FROM admin_log WHERE action = 'batch_create'");
    expect(log.some((l) => l.detail.includes("Shop Tirana Oct"))).toBe(true);
    expect(JSON.stringify(log)).not.toContain(codes[0]!.replace(/-/g, ""));
  });

  it("activates a batch once and voids it later without touching redeemed cards", async () => {
    const d = await enroll();
    const { batch, codes } = await cards(30, 3, false);
    expect((await admin("POST", `/admin/v1/batches/${batch}/activate`)).body.activated).toBe(3);
    expect((await admin("POST", `/admin/v1/batches/${batch}/activate`)).status).toBe(409);
    await redeem(d, codes[0]!);
    const v = await admin("POST", `/admin/v1/batches/${batch}/void`, { reason: "lost parcel" });
    expect(v.body.voided).toBe(2);
    const statuses = (await sql<{ status: string }>("SELECT status FROM cards WHERE batch_id = ? ORDER BY status", batch)).map((r) => r.status);
    expect(statuses).toEqual(["redeemed", "void", "void"]);
    const list = await admin("GET", "/admin/v1/batches");
    expect(list.body.batches.find((b: { id: number }) => b.id === batch)).toMatchObject({ status: "void", redeemed: 1, voided: 2 });
  });

  it("creates a big print run in one go", async () => {
    const r = await admin("POST", "/admin/v1/batches", { label: "big run", days: 365, count: 1200 });
    expect(r.status).toBe(201);
    expect(new Set(r.body.codes.map((c: { code: string }) => c.code)).size).toBe(1200);
    const [row] = await sql<{ n: number }>("SELECT COUNT(*) AS n FROM cards WHERE batch_id = ?", r.body.batch.id);
    expect(row!.n).toBe(1200);
  });

  it("validates batch requests", async () => {
    expect((await admin("POST", "/admin/v1/batches", { label: "x", days: 0, count: 1 })).status).toBe(400);
    expect((await admin("POST", "/admin/v1/batches", { label: "x", days: 30, count: 5001 })).status).toBe(400);
    expect((await admin("POST", "/admin/v1/batches", { label: "", days: 30, count: 1 })).status).toBe(400);
    expect((await admin("POST", "/admin/v1/batches/999999/activate")).status).toBe(409);
  });
});

describe("support tools", () => {
  it("looks up a card by its code", async () => {
    const d = await enroll();
    const { codes } = await cards(90, 1);
    await redeem(d, codes[0]!);
    const r = await admin("POST", "/admin/v1/cards/lookup", { code: codes[0]!.toLowerCase() });
    expect(r.body.card).toMatchObject({ status: "redeemed", redeemed_by: d.id, days: 90, last4: codes[0]!.slice(-4) });
    expect((await admin("POST", "/admin/v1/cards/lookup", { code: "2222-2222-2222-2223" })).status).toBe(400); // bad check character
    expect((await admin("POST", "/admin/v1/cards/lookup", { code: "2222-2222-2222-2222" })).body.card).toBeNull(); // valid form, no such card
  });

  it("voids a redeemed card and takes its days back, only once", async () => {
    const d = await enroll();
    const { codes } = await cards(30, 2);
    await redeem(d, codes[0]!);
    await redeem(d, codes[1]!);
    const r = await admin("POST", "/admin/v1/cards/void", { code: codes[1], reason: "refunded", revoke: true });
    expect(r.body).toMatchObject({ voided: true, revoked: true });
    const again = await admin("POST", "/admin/v1/cards/void", { code: codes[1], reason: "refunded", revoke: true });
    expect(again.status).toBe(409);
    const [row] = await sql<{ pass_until: number }>("SELECT pass_until FROM devices WHERE id = ?", d.id);
    expect(Math.abs(row!.pass_until - (nowSeconds() + 30 * DAY))).toBeLessThan(5);
    expect((await call("POST", "/v1/status", { auth: d.auth, body: { app: 1 } })).status).toBe(200);
  });

  it("grants and removes days, within the cap", async () => {
    const d = await enroll();
    let r = await admin("POST", `/admin/v1/devices/${d.id}/grant`, { days: 30, reason: "box includes a month" });
    expect(Math.abs(r.body.pass_until - (nowSeconds() + 30 * DAY))).toBeLessThan(5);
    r = await admin("POST", `/admin/v1/devices/${d.id}/grant`, { days: -10, reason: "correction" });
    expect(Math.abs(r.body.pass_until - (nowSeconds() + 20 * DAY))).toBeLessThan(5);
    expect((await admin("POST", `/admin/v1/devices/${d.id}/grant`, { days: 3000, reason: "too much" })).status).toBe(409);
    expect((await admin("POST", `/admin/v1/devices/d_${"Z".repeat(20)}/grant`, { days: 3, reason: "x" })).status).toBe(404);
    const show = await admin("GET", `/admin/v1/devices/${d.id}`);
    expect(show.body.ledger.map((l: { kind: string }) => l.kind)).toEqual(["revoke", "grant"]);
    expect(show.body.device).not.toHaveProperty("secret_hash");
  });

  it("blocks a box: Game Pass closes at its next check-in", async () => {
    const d = await enroll();
    await admin("POST", `/admin/v1/devices/${d.id}/grant`, { days: 30, reason: "test" });
    await admin("POST", `/admin/v1/devices/${d.id}/block`, { blocked: true, reason: "chargeback" });
    const r = await call("POST", "/v1/status", { auth: d.auth, body: { app: 1 } });
    const pass = await passPayload(r.body.pass);
    expect(pass.blocked).toBe(true);
    expect(pass.pass_until).toBeLessThanOrEqual(pass.now);
    await admin("POST", `/admin/v1/devices/${d.id}/block`, { blocked: false, reason: "resolved" });
    const after = await passPayload((await call("POST", "/v1/status", { auth: d.auth, body: { app: 1 } })).body.pass);
    expect(after.pass_until - after.now).toBeGreaterThan(29 * DAY);
  });

  it("moves the remaining time to a replacement box and blocks the old one", async () => {
    const oldBox = await enroll();
    const newBox = await enroll();
    await admin("POST", `/admin/v1/devices/${oldBox.id}/grant`, { days: 40, reason: "test" });
    await admin("POST", `/admin/v1/devices/${newBox.id}/grant`, { days: 5, reason: "test" });
    const r = await admin("POST", `/admin/v1/devices/${oldBox.id}/transfer`, { to: newBox.id, block: true, reason: "warranty" });
    expect(r.status).toBe(200);
    const [o] = await sql<{ pass_until: number; blocked: number }>("SELECT pass_until, blocked FROM devices WHERE id = ?", oldBox.id);
    const [n] = await sql<{ pass_until: number }>("SELECT pass_until FROM devices WHERE id = ?", newBox.id);
    expect(o!.blocked).toBe(1);
    expect(o!.pass_until).toBeLessThanOrEqual(nowSeconds());
    expect(Math.abs(n!.pass_until - (nowSeconds() + 45 * DAY))).toBeLessThan(5);
    expect((await admin("POST", `/admin/v1/devices/${oldBox.id}/transfer`, { to: "d_" + "Z".repeat(20), reason: "x" })).status).toBe(404);
    expect((await admin("GET", "/admin/v1/check")).body.ok).toBe(true);
  });

  it("reports fleet stats for watching a rollout", async () => {
    const d = await enroll();
    await call("POST", "/v1/status", { auth: d.auth, body: { app: 10400 } });
    await call("POST", "/v1/events", { auth: d.auth, body: { app: 10400, events: [{ kind: "crash_native" }, { kind: "boot_ok" }] } });
    const r = await admin("GET", "/admin/v1/stats");
    expect(r.status).toBe(200);
    expect(r.body.versions_seen_7d).toContainEqual({ app_version: 10400, devices: 1 });
    expect(r.body.events_24h).toContainEqual({ app_version: 10400, kind: "crash_native", n: 1, devices: 1 });
  });
});
