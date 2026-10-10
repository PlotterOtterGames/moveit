# Tundu server

The cloud side of Tundu: gift cards, Game Pass and box check-ins (a Cloudflare Worker with a D1 database), and the
CLI you run on the laptop for cards, support and releases. **Design, threat model, setup and runbooks:
[docs/cloud.md](../docs/cloud.md).**

| Path | What |
|---|---|
| `src/` | The Worker. No runtime dependencies. `index.ts` routes, `devices.ts` enrol/status/events, `redeem.ts` codes → days, `admin.ts` the CLI's API, `codes.ts` code format + hashing, `crypto.ts` signed documents, `pass.ts` Game Pass tokens, `auth.ts`, `http.ts` |
| `migrations/` | D1 schema. Never edit an applied migration: add `0002_….sql` (`npx wrangler d1 migrations create tundu <name>`) |
| `wrangler.jsonc` | Worker config: route `api.tundu.al`, D1 binding, rate limits, daily cron. Secrets are NOT here |
| `cli/` | `node cli/tundu.ts help`: keys, cards, devices, stats, releases (signed manifests), a pretend box |
| `test/` | Worker tests (run inside workerd with a local D1), test secrets (public, never trusted by builds), shared test vectors for the box's Java and Unity's C# (`test/vectors`, regenerate with `npm run vectors`) |

Needs Node ≥ 22.18 (runs the TypeScript CLI directly). Install with `npm ci` (`.npmrc` disables install scripts).

```bash
npm run test:all        # typecheck + Worker tests (49) + CLI tests (14)
npm run dev:vars && npm run migrate:local && npx wrangler dev    # local API on :8787 with test secrets
node cli/tundu.ts help
```

API (all JSON; boxes authenticate with `Authorization: Bearer <device id>.<secret>`):

| Route | Who | What |
|---|---|---|
| `GET /v1/health` | anyone | `{ok, now}` |
| `POST /v1/devices` | new box | `{model?, sdk?, app?}` → `{device, secret, pass}` |
| `POST /v1/status` | box | `{app, sdk?}` → `{pass}` |
| `POST /v1/redeem` | box | `{code, request}` → `{result, days?, pass}` |
| `POST /v1/events` | box | `{app, events: [{kind, at?, detail?}]}` (health: boot_ok, crash, crash_native, anr, update_*, firmware_*, safe_mode) |
| `/admin/v1/...` | CLI | batches (create, activate, void, list), cards (lookup, void), devices (show, grant, block, transfer), stats, check |

App updates and the game catalog are not served by this Worker: they are static files on R2, signed offline by
`cli/tundu.ts release …`.

Dev tool versions are pinned exactly and were at least two weeks old when chosen. `npm audit` reports advisories
for `undici` inside wrangler/miniflare 4.140 (local dev and tests only, nothing deployed): upgrade `wrangler` to
≥ 4.149 and `@cloudflare/vitest-plugin` to ≥ 1.4.0 once they are two weeks old (after 2026-10-22).
