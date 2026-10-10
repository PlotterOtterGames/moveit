// Writes .dev.vars with the TEST secrets, for `npx wrangler dev` on this computer:  npm run dev:vars
// (Production secrets go to Cloudflare with `npx wrangler secret put`, never into a file.)
import { writeFileSync } from "node:fs";
import { TEST_SECRETS } from "./secrets.ts";

writeFileSync(".dev.vars", Object.entries(TEST_SECRETS).map(([k, v]) => `${k}='${v}'`).join("\n") + "\n");
console.log("wrote .dev.vars (test secrets). The test admin token is TEST_ADMIN_TOKEN in test/secrets.ts.");
