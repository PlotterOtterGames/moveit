import { createExecutionContext, createScheduledController, env, waitOnExecutionContext } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import worker from "../src/index.ts";
import { call, enroll, nowSeconds, passPayload, sql } from "./helpers.ts";

describe("enrolment and status", () => {
  it("gives a new box an id, a secret and a signed token bound to it", async () => {
    const r = await call("POST", "/v1/devices", { body: { model: "TX66", sdk: 30, app: 10203 } });
    expect(r.status).toBe(201);
    expect(r.body.device).toMatch(/^d_[2-9A-HJ-NP-Z]{20}$/);
    expect(r.body.secret).toMatch(/^[A-Za-z0-9_-]{43}$/);
    const pass = await passPayload(r.body.pass);
    expect(pass).toMatchObject({ type: "tundu.pass", v: 1, device: r.body.device, pass_until: 0, blocked: false });
    expect(Math.abs(pass.now - nowSeconds())).toBeLessThan(5);
    const [row] = await sql<{ secret_hash: string; model: string }>("SELECT secret_hash, model FROM devices WHERE id = ?", r.body.device);
    expect(row!.model).toBe("TX66");
    expect(row!.secret_hash).not.toContain(r.body.secret); // only the hash is stored
  });

  it("refreshes the token for the right secret and refuses a wrong one", async () => {
    const d = await enroll();
    const ok = await call("POST", "/v1/status", { auth: d.auth, body: { app: 10300, sdk: 31 } });
    expect(ok.status).toBe(200);
    expect((await passPayload(ok.body.pass)).device).toBe(d.id);
    const [row] = await sql<{ app_version: number; sdk: number }>("SELECT app_version, sdk FROM devices WHERE id = ?", d.id);
    expect(row).toEqual({ app_version: 10300, sdk: 31 });

    const wrong = d.auth.slice(0, -2) + (d.auth.endsWith("AA") ? "BB" : "AA");
    expect((await call("POST", "/v1/status", { auth: wrong, body: { app: 1 } })).status).toBe(401);
    expect((await call("POST", "/v1/status", { body: { app: 1 } })).status).toBe(401);
    const unknown = "d_" + "Z".repeat(20) + d.auth.slice(22);
    expect((await call("POST", "/v1/status", { auth: unknown, body: { app: 1 } })).status).toBe(401);
  });

  it("refuses bad input: wrong type, oversize, wrong content type, broken JSON", async () => {
    const d = await enroll();
    expect((await call("POST", "/v1/status", { auth: d.auth, body: { app: "10203" } })).status).toBe(400);
    expect((await call("POST", "/v1/status", { auth: d.auth, body: { app: -1 } })).status).toBe(400);
    expect((await call("POST", "/v1/status", { auth: d.auth, raw: "{not json" })).status).toBe(400);
    expect((await call("POST", "/v1/status", { auth: d.auth, raw: "[1,2]" })).status).toBe(400);
    expect((await call("POST", "/v1/status", { auth: d.auth, raw: JSON.stringify({ app: 1, pad: "x".repeat(5000) }) })).status).toBe(413);
    const textPlain = await call("POST", "/v1/status", { auth: d.auth, raw: '{"app":1}', headers: { "content-type": "text/plain" } });
    expect(textPlain.status).toBe(415);
    expect((await call("POST", "/v1/devices", { body: { model: "bad\nname" } })).status).toBe(400);
  });

  it("answers unknown routes with 404 and never leaks internals", async () => {
    const r = await call("GET", "/v1/nothing");
    expect(r.status).toBe(404);
    expect(r.body).toEqual({ error: "not_found" });
    expect(r.headers.get("cache-control")).toBe("no-store");
    expect((await call("GET", "/v1/health")).body.ok).toBe(true);
  });

  it("pauses enrolment when far more boxes enrol in a day than real sales could explain", async () => {
    await env.DB.prepare(
      "INSERT INTO devices (id, secret_hash, created_at, last_seen_at) SELECT 'fake_' || key, 'h', ?2, ?2 FROM json_each(?1)",
    )
      .bind(JSON.stringify(new Array(1000).fill(0)), nowSeconds())
      .run();
    try {
      const r = await call("POST", "/v1/devices", { body: {} });
      expect(r.status).toBe(503);
      expect(r.headers.get("retry-after")).toBe("3600");
    } finally {
      await env.DB.prepare("DELETE FROM devices WHERE id LIKE 'fake_%'").run(); // storage is shared within this file
    }
  });
});

describe("health events", () => {
  it("stores valid events and refuses unknown kinds or too many", async () => {
    const d = await enroll();
    const t = nowSeconds();
    const r = await call("POST", "/v1/events", {
      auth: d.auth,
      body: { app: 10203, events: [{ kind: "boot_ok", at: t - 10 }, { kind: "crash_native", detail: "SIGSEGV libunity.so", at: 5 }] },
    });
    expect(r.status).toBe(200);
    const rows = await sql<{ kind: string; at: number; detail: string | null }>("SELECT kind, at, detail FROM events WHERE device_id = ? ORDER BY id", d.id);
    expect(rows.map((e) => e.kind)).toEqual(["boot_ok", "crash_native"]);
    expect(rows[0]!.at).toBe(t - 10);
    expect(Math.abs(rows[1]!.at - t)).toBeLessThan(5); // implausible box time replaced by the server's
    expect((await call("POST", "/v1/events", { auth: d.auth, body: { app: 1, events: [{ kind: "hacked" }] } })).status).toBe(400);
    const many = Array.from({ length: 21 }, () => ({ kind: "boot_ok" }));
    expect((await call("POST", "/v1/events", { auth: d.auth, body: { app: 1, events: many } })).status).toBe(400);
  });

  it("deletes events older than 90 days in the daily cron", async () => {
    const d = await enroll();
    const t = nowSeconds();
    await env.DB.prepare(
      "INSERT INTO events (device_id, at, received_at, app_version, kind) VALUES (?1, 0, ?2, 1, 'boot_ok'), (?1, 0, ?3, 1, 'boot_ok')",
    )
      .bind(d.id, t - 91 * 86400, t - 89 * 86400)
      .run();
    const ctx = createExecutionContext();
    await worker.scheduled!(createScheduledController(), env, ctx);
    await waitOnExecutionContext(ctx);
    const left = await sql<{ received_at: number }>("SELECT received_at FROM events WHERE device_id = ?", d.id);
    expect(left.map((e) => e.received_at)).toEqual([t - 89 * 86400]);
  });
});
