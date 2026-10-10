// Tundu API (Cloudflare Worker). Routes:
//   GET  /v1/health                      liveness, no auth
//   POST /v1/devices                     a new box gets its identity
//   POST /v1/status                      box check-in, fresh Game Pass token
//   POST /v1/redeem                      gift card code -> Game Pass days
//   POST /v1/events                      box health reports (crashes, updates)
//   *    /admin/v1/...                   the CLI (src/admin.ts)
// App updates and the game catalog are NOT served here: they are static files signed offline, on R2 (see
// docs/cloud.md). This Worker can't change what boxes install.

import { admin } from "./admin.ts";
import { enroll, events, status } from "./devices.ts";
import { errorResponse, HttpError, json, now, type Env } from "./http.ts";
import { redeem } from "./redeem.ts";

export type { Env } from "./http.ts";

const EVENT_RETENTION_DAYS = 90;

async function route(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url);
  const path = url.pathname;
  const method = request.method;
  if (path.startsWith("/admin/")) return admin(request, env, path);
  if (method === "GET" && path === "/v1/health") return json({ ok: true, now: now() });
  if (method === "POST") {
    if (path === "/v1/devices") return enroll(request, env);
    if (path === "/v1/status") return status(request, env);
    if (path === "/v1/redeem") return redeem(request, env);
    if (path === "/v1/events") return events(request, env);
  }
  throw new HttpError(404, "not_found");
}

export default {
  async fetch(request, env): Promise<Response> {
    try {
      return await route(request, env);
    } catch (e) {
      if (e instanceof HttpError) return errorResponse(e);
      // Never echo internals to the caller; the log has the details (no secrets are ever in errors).
      console.error(JSON.stringify({ event: "error", path: new URL(request.url).pathname, message: String(e) }));
      return json({ error: "server_error" }, 500);
    }
  },

  /** Daily cron (wrangler.jsonc "triggers"): drop old health events. */
  async scheduled(_controller, env, _ctx): Promise<void> {
    const r = await env.DB.prepare("DELETE FROM events WHERE received_at < ?")
      .bind(now() - EVENT_RETENTION_DAYS * 86400)
      .run();
    console.log(JSON.stringify({ event: "cleanup", deleted: r.meta.changes }));
  },
} satisfies ExportedHandler<Env>;
