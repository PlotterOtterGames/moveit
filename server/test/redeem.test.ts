import { describe, expect, it } from "vitest";
import { checkChar } from "../src/codes.ts";
import { MAX_FAILS_PER_DAY, PASS_MAX_DAYS } from "../src/redeem.ts";
import { admin, call, cards, enroll, nowSeconds, passPayload, redeem, requestId, sql } from "./helpers.ts";

const DAY = 86400;

describe("redeeming gift cards", () => {
  it("adds the card's days and returns a signed token with the new end date", async () => {
    const d = await enroll();
    const { codes } = await cards(30, 1);
    const r = await redeem(d, codes[0]!);
    expect(r.status).toBe(200);
    expect(r.body).toMatchObject({ result: "ok", days: 30 });
    const pass = await passPayload(r.body.pass);
    expect(pass.device).toBe(d.id);
    expect(Math.abs(pass.pass_until - (nowSeconds() + 30 * DAY))).toBeLessThan(5);
  });

  it("refuses cards of a batch that isn't activated yet, then accepts them once it is", async () => {
    const d = await enroll();
    const { batch, codes } = await cards(7, 2, false);
    expect((await redeem(d, codes[0]!)).body.result).toBe("not_active");
    await admin("POST", `/admin/v1/batches/${batch}/activate`);
    expect((await redeem(d, codes[0]!)).body.result).toBe("ok");
  });

  it("is safe to retry: the same request id gets 'ok' again without adding days twice", async () => {
    const d = await enroll();
    const { codes } = await cards(30, 1);
    const req = requestId();
    const first = await redeem(d, codes[0]!, req);
    const again = await redeem(d, codes[0]!, req);
    expect(again.body.result).toBe("ok");
    expect((await passPayload(again.body.pass)).pass_until).toBe((await passPayload(first.body.pass)).pass_until);
    expect((await redeem(d, codes[0]!)).body.result).toBe("already_yours"); // typed in again later
  });

  it("works once only: another box gets 'used'", async () => {
    const a = await enroll();
    const b = await enroll();
    const { codes } = await cards(30, 1);
    expect((await redeem(a, codes[0]!)).body.result).toBe("ok");
    const r = await redeem(b, codes[0]!);
    expect(r.body.result).toBe("used");
    expect((await passPayload(r.body.pass)).pass_until).toBe(0);
  });

  it("lets exactly one of many boxes win a race for the same card", async () => {
    const boxes = await Promise.all(Array.from({ length: 8 }, () => enroll()));
    const { codes } = await cards(30, 1);
    const results = await Promise.all(boxes.map((b) => redeem(b, codes[0]!)));
    expect(results.filter((r) => r.body.result === "ok")).toHaveLength(1);
    expect(results.filter((r) => r.body.result === "used")).toHaveLength(7);
    const ledger = await sql("SELECT * FROM pass_ledger WHERE kind = 'redeem' AND card_id = (SELECT id FROM cards WHERE last4 = ?)", codes[0]!.slice(-4));
    expect(ledger).toHaveLength(1);
  });

  it("never loses days when one box redeems several cards at the same time", async () => {
    const d = await enroll();
    const { codes } = await cards(10, 6);
    const results = await Promise.all(codes.map((c) => redeem(d, c)));
    expect(results.every((r) => r.body.result === "ok")).toBe(true);
    const [row] = await sql<{ pass_until: number }>("SELECT pass_until FROM devices WHERE id = ?", d.id);
    expect(Math.abs(row!.pass_until - (nowSeconds() + 60 * DAY))).toBeLessThan(5);
  });

  it("stacks from the current end date while the pass is running", async () => {
    const d = await enroll();
    const { codes } = await cards(30, 2);
    const first = await passPayload((await redeem(d, codes[0]!)).body.pass);
    const second = await passPayload((await redeem(d, codes[1]!)).body.pass);
    expect(second.pass_until - first.pass_until).toBe(30 * DAY);
  });

  it("counts unknown codes and locks the box for a day after too many", async () => {
    const d = await enroll();
    const { codes } = await cards(30, 1);
    // A well-formed code that doesn't exist (what a mistyped code with a lucky check character looks like).
    const body = "2345678" + "Z".repeat(8);
    const fake = body + checkChar(body);
    for (let i = 0; i < 3; i++) expect((await redeem(d, fake)).body.result).toBe("invalid");
    expect((await sql<{ fail_count: number }>("SELECT fail_count FROM devices WHERE id = ?", d.id))[0]!.fail_count).toBe(3);
    // (The per-box rate limit allows 10 tries a minute, so jump to the edge instead of waiting.)
    await sql("UPDATE devices SET fail_count = ? WHERE id = ?", MAX_FAILS_PER_DAY - 1, d.id);
    expect((await redeem(d, fake)).body.result).toBe("invalid");
    const locked = await redeem(d, codes[0]!);
    expect(locked.status).toBe(429);
    expect(locked.body.error).toBe("locked");
    expect(Number(locked.headers.get("retry-after"))).toBeGreaterThan(86000);

    // A day later it works again.
    await sql("UPDATE devices SET fail_window_start = fail_window_start - 86401 WHERE id = ?", d.id);
    expect((await redeem(d, codes[0]!)).body.result).toBe("ok");
  });

  it("rate-limits a box that sends codes faster than a person can type", async () => {
    const d = await enroll();
    const replies = [];
    for (let i = 0; i < 12; i++) replies.push(await redeem(d, "2222-2222-2222-2222"));
    expect(replies.slice(0, 10).every((r) => r.status === 200)).toBe(true);
    expect(replies[11]!.status).toBe(429);
    expect(replies[11]!.body.error).toBe("slow_down");
  });

  it("rate-limits enrolment per address", async () => {
    const ip = "10.250.250.250";
    const statuses = [];
    for (let i = 0; i < 6; i++) statuses.push((await call("POST", "/v1/devices", { body: {}, ip })).status);
    expect(statuses).toEqual([201, 201, 201, 201, 201, 429]);
  });

  it("doesn't count used or inactive cards as wrong guesses", async () => {
    const a = await enroll();
    const b = await enroll();
    const { codes } = await cards(1, 1);
    await redeem(a, codes[0]!);
    for (let i = 0; i < 5; i++) expect((await redeem(b, codes[0]!)).body.result).toBe("used");
    const [row] = await sql<{ fail_count: number }>("SELECT fail_count FROM devices WHERE id = ?", b.id);
    expect(row!.fail_count).toBe(0);
  });

  it("counts malformed codes as wrong (our app never sends them)", async () => {
    const d = await enroll();
    const r = await redeem(d, "HELLO");
    expect(r.body.result).toBe("invalid");
    const [row] = await sql<{ fail_count: number }>("SELECT fail_count FROM devices WHERE id = ?", d.id);
    expect(row!.fail_count).toBe(1);
  });

  it("won't push the pass more than PASS_MAX_DAYS ahead; the card stays usable", async () => {
    const d = await enroll();
    await admin("POST", `/admin/v1/devices/${d.id}/grant`, { days: PASS_MAX_DAYS - 10, reason: "test" });
    const { codes } = await cards(30, 1);
    expect((await redeem(d, codes[0]!)).body.result).toBe("limit");
    const [card] = await sql<{ status: string }>("SELECT status FROM cards WHERE last4 = ?", codes[0]!.slice(-4));
    expect(card!.status).toBe("active");
  });

  it("refuses voided cards and blocked boxes", async () => {
    const d = await enroll();
    const { batch, codes } = await cards(30, 2);
    await admin("POST", `/admin/v1/batches/${batch}/void`, { reason: "stolen from the shop" });
    expect((await redeem(d, codes[0]!)).body.result).toBe("void");
    await admin("POST", `/admin/v1/devices/${d.id}/block`, { blocked: true, reason: "test" });
    const r = await redeem(d, codes[1]!);
    expect(r.status).toBe(403);
    expect(r.body.error).toBe("blocked");
  });

  it("requires a request id and the box's credentials", async () => {
    const d = await enroll();
    const { codes } = await cards(30, 1);
    expect((await call("POST", "/v1/redeem", { auth: d.auth, body: { code: codes[0] } })).status).toBe(400);
    expect((await call("POST", "/v1/redeem", { auth: d.auth, body: { code: codes[0], request: "x" } })).status).toBe(400);
    expect((await call("POST", "/v1/redeem", { body: { code: codes[0], request: requestId() } })).status).toBe(401);
  });

  it("leaves the books consistent after all of the above", async () => {
    const d = await enroll();
    const { codes } = await cards(30, 3);
    await Promise.all(codes.map((c) => redeem(d, c)));
    const check = await admin("GET", "/admin/v1/check");
    expect(check.body).toEqual({ ok: true, pass_until_mismatch: [], redeemed_without_ledger: [], stuck_claimed: [], unfinished_batches: [] });
  });
});
