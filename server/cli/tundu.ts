#!/usr/bin/env node
// Tundu admin + release CLI. Runs with plain Node >= 22.18 (built-in TypeScript type stripping), no build step:
//   cd server && node cli/tundu.ts help

import * as cards from "./cards.ts";
import * as devbox from "./devbox.ts";
import * as keys from "./keys.ts";
import * as release from "./release.ts";
import { CliError, config, fail } from "./util.ts";

const HELP = `Tundu CLI   (settings: TUNDU_API, TUNDU_CDN, TUNDU_BUCKET, TUNDU_RELEASE_KEY, TUNDU_ADMIN_TOKEN, ANDROID_BUILD_TOOLS)

Keys (once; see docs/cloud.md "Setting up")
  keys new-release --kid r1 [--out FILE]   offline update-signing key, encrypted; pins its public key for the app
  keys new-pass --kid p1                   Game Pass signing key: prints the private JWK for "wrangler secret put PASS_SIGNING_KEY"
  keys set-apk-signer <sha256>             pin the release keystore's certificate digest
  keys pepper                              random CODE_PEPPER for the Worker
  keys admin-token                         new admin token + the hash for ADMIN_TOKEN_SHA256

Gift cards
  cards new --days 30 --count 500 --label "Shop X, Oct 2026" [--note TEXT]
  cards activate <batch>                   cards work only after this (when the shop has paid)
  cards void-batch <batch> --reason TEXT   lost/stolen batch: unused cards stop working
  cards list
  cards lookup <code>
  cards void <code> --reason TEXT [--revoke]   --revoke also takes back the days if it was redeemed

Boxes (support)
  device show <id>
  device grant <id> --days N --reason TEXT    negative N takes days away
  device block <id> --reason TEXT  |  device unblock <id> --reason TEXT
  device transfer <from> <to> --reason TEXT [--block-old]
  stats                                     versions seen, crashes per version, redemptions
  check                                     database consistency check (run after a restore)

Releases (signed with the offline release key)
  release stage <apk>                       check signer/package, upload by hash, record in releases/apks.json
  release app --channel C --version N [--rollout 10] [--required 0]
  release rollout --channel C --percent 50
  release game --channel C --id fly --tier pass|free [--min-app N] [--hidden|--shown] [--from 2026-12-01] [--remove]
  release firmware-stage <ota.zip> --model M --to FINGERPRINT
  release firmware --channel C --sha256 H --from FP[,FP...] --rollout 10   (--rollout -1 removes it)
  release resign --channel C [--days 180]   new expiry (boxes refuse expired manifests)
  release show --channel C                  release check --channel C [--download]
  release push --channel C                  upload the local manifest again
  release bucket --channel C --device <id>  is this box in the current rollout?

Pretend box (try everything without hardware; --api http://127.0.0.1:8787 for "npx wrangler dev")
  devbox enroll [--api URL]   devbox status   devbox redeem <code>   devbox event <kind>   devbox channel <name>
`;

interface Args {
  positional: string[];
  flags: Record<string, string | true>;
}

export function parseArgs(argv: string[]): Args {
  const positional: string[] = [];
  const flags: Record<string, string | true> = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]!;
    if (a.startsWith("--")) {
      const name = a.slice(2);
      const next = argv[i + 1];
      if (next !== undefined && !next.startsWith("--")) {
        flags[name] = next;
        i++;
      } else flags[name] = true;
    } else positional.push(a);
  }
  return { positional, flags };
}

function flag(args: Args, name: string): string {
  const v = args.flags[name];
  if (typeof v !== "string" || !v) fail(`missing --${name}`);
  return v;
}

function optFlag(args: Args, name: string): string | undefined {
  const v = args.flags[name];
  return typeof v === "string" ? v : undefined;
}

function intFlag(args: Args, name: string, min: number, max: number, fallback?: number): number {
  const raw = args.flags[name];
  if (raw === undefined && fallback !== undefined) return fallback;
  const n = Number(raw);
  if (!Number.isSafeInteger(n) || n < min || n > max) fail(`--${name} must be a whole number from ${min} to ${max}`);
  return n;
}

function pos(args: Args, i: number, what: string): string {
  const v = args.positional[i];
  if (!v) fail(`missing ${what}`);
  return v;
}

function dateFlag(args: Args, name: string): number | undefined {
  const raw = optFlag(args, name);
  if (raw === undefined) return undefined;
  const t = Date.parse(raw.length === 10 ? `${raw}T00:00:00+01:00` : raw); // dates are Tirana time
  if (Number.isNaN(t)) fail(`--${name}: use YYYY-MM-DD`);
  return Math.floor(t / 1000);
}

async function main(argv: string[]): Promise<void> {
  const args = parseArgs(argv);
  const [group, command] = args.positional;
  const sub = `${group ?? ""} ${command ?? ""}`.trim();
  if (args.flags["api"] && group !== "devbox") config.api = flag(args, "api");

  switch (group === "stats" || group === "check" || group === "help" || group === undefined ? group ?? "help" : sub) {
    case "help":
      console.log(HELP);
      return;
    case "keys new-release":
      return keys.newReleaseKey(flag(args, "kid"), optFlag(args, "out") ?? config.releaseKey);
    case "keys new-pass":
      return keys.newPassKey(flag(args, "kid"));
    case "keys set-apk-signer":
      return keys.setApkSigner(pos(args, 2, "certificate SHA-256"));
    case "keys pepper":
      return keys.pepper();
    case "keys admin-token":
      return keys.adminToken();

    case "cards new":
      await cards.newBatch(intFlag(args, "days", 1, 3660), intFlag(args, "count", 1, 5000), flag(args, "label"), optFlag(args, "note"));
      return;
    case "cards activate":
      return cards.activate(Number(pos(args, 2, "batch id")));
    case "cards void-batch":
      return cards.voidBatch(Number(pos(args, 2, "batch id")), flag(args, "reason"));
    case "cards list":
      return cards.listBatches();
    case "cards lookup":
      return cards.lookup(pos(args, 2, "code"));
    case "cards void":
      return cards.voidCard(pos(args, 2, "code"), flag(args, "reason"), args.flags["revoke"] === true);

    case "device show":
      return cards.showDevice(pos(args, 2, "device id"));
    case "device grant":
      return cards.grant(pos(args, 2, "device id"), intFlag(args, "days", -3660, 3660), flag(args, "reason"));
    case "device block":
      return cards.block(pos(args, 2, "device id"), true, flag(args, "reason"));
    case "device unblock":
      return cards.block(pos(args, 2, "device id"), false, flag(args, "reason"));
    case "device transfer":
      return cards.transfer(pos(args, 2, "old device id"), pos(args, 3, "new device id"), flag(args, "reason"), args.flags["block-old"] === true);
    case "stats":
      return cards.stats();
    case "check":
      return cards.consistency();

    case "release stage":
      await release.stage(pos(args, 2, "APK path"));
      return;
    case "release app": {
      const version = intFlag(args, "version", 1, 2_100_000_000);
      const rollout = intFlag(args, "rollout", 0, 100, 10);
      const required = intFlag(args, "required", 0, version, 0);
      await release.publish(flag(args, "channel"), (c) => release.setApp(c, version, rollout, required));
      return;
    }
    case "release rollout": {
      const percent = intFlag(args, "percent", 0, 100);
      await release.publish(flag(args, "channel"), (c) => release.setRollout(c, percent));
      return;
    }
    case "release game": {
      const tier = optFlag(args, "tier");
      if (tier !== undefined && tier !== "free" && tier !== "pass") fail("--tier free|pass");
      const change = {
        remove: args.flags["remove"] === true,
        tier: tier as "free" | "pass" | undefined,
        minApp: args.flags["min-app"] === undefined ? undefined : intFlag(args, "min-app", 0, 2_100_000_000),
        hidden: args.flags["hidden"] === true ? true : args.flags["shown"] === true ? false : undefined,
        from: dateFlag(args, "from"),
      };
      const id = flag(args, "id");
      await release.publish(flag(args, "channel"), (c) => release.setGame(c, id, change));
      return;
    }
    case "release firmware-stage":
      await release.stageFirmware(pos(args, 2, "OTA zip"), flag(args, "model"), flag(args, "to"));
      return;
    case "release firmware": {
      const sha = flag(args, "sha256");
      const from = flag(args, "from").split(",").map((s) => s.trim()).filter(Boolean);
      const rollout = intFlag(args, "rollout", -1, 100);
      await release.publish(flag(args, "channel"), (c) => release.setFirmware(c, sha, from, rollout));
      return;
    }
    case "release resign":
      await release.publish(flag(args, "channel"), () => {}, intFlag(args, "days", 7, 400, 180));
      return;
    case "release show":
      return release.show(flag(args, "channel"));
    case "release check":
      return release.check(flag(args, "channel"), args.flags["download"] === true);
    case "release push":
      return release.push(flag(args, "channel"));
    case "release bucket":
      return release.bucket(flag(args, "device"), flag(args, "channel"));

    case "devbox enroll":
      return devbox.enroll(optFlag(args, "api"));
    case "devbox status":
      return devbox.status();
    case "devbox redeem":
      return devbox.redeem(pos(args, 2, "code"));
    case "devbox event":
      return devbox.events(pos(args, 2, "kind"));
    case "devbox channel":
      return devbox.channel(pos(args, 2, "channel"));
    default:
      fail(`unknown command "${sub}". Try: node cli/tundu.ts help`);
  }
}

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].replace(/\\/g, "/").split("/").pop()!)) {
  main(process.argv.slice(2)).catch((e: unknown) => {
    if (e instanceof CliError) {
      console.error(`Error: ${e.message}`);
      process.exit(1);
    }
    throw e;
  });
}
