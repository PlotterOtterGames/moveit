import { applyD1Migrations, env } from "cloudflare:test";

// Each test file gets its own storage; apply the schema to it (applyD1Migrations skips already-applied ones).
await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
