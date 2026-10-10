# Tundu cloud: gift cards, Game Pass, app/firmware updates, new games

Researched and built on 2026-10-10 (cloud session). What runs where and what is tested is listed at the end. The
user's requirements: gift card codes that switch on Game Pass for X days; system updates for the boxes; pushing new
free and Game Pass games; no hacks, no version mismatches, crashes, corruption or exploits.

## The recommendation in one page

1. **Two separate halves, so a hacked server can't change what boxes install.**
   - **Updates and the game catalog are static files signed on your laptop** with an *offline* release key (never
     on any server), hosted on Cloudflare R2. The box only installs what that key signed. The API server isn't
     involved at all.
   - **A small API** (Cloudflare Worker + D1 database) does only gift cards, Game Pass and box check-ins. Its
     worst case if hacked is free Game Pass, never malware on boxes.
2. **Cloudflare Workers + D1 + R2**, not a rented server: nothing to patch, DDoS protection and TLS included,
   point-in-time restore of the database, no download fees for APKs. About **$5/month** (Workers Paid) until tens of
   thousands of boxes. The API has **zero runtime dependencies** (no npm packages in what's deployed).
3. **All games stay in the one APK.** A new game (free or Game Pass) ships in an app update; the signed catalog
   decides when it appears, whether it's free or in Game Pass, and can hide a broken game instantly. No
   downloadable code: Unity's IL2CPP can't load new code, and downloaded asset bundles are the classic source of
   version-mismatch crashes.
4. **Game Pass belongs to the box, no accounts.** No names, emails or passwords: nothing personal to leak, simple
   under Albania's GDPR-style law. A replaced box gets its remaining time moved by support (`device transfer`).
5. **Codes**: 16 characters (`XXXX-XXXX-XXXX-XXXX`, no 0/1/I/O) with a check character, 75 random bits, stored
   only as a keyed hash, **inactive until the batch is paid for**, and each card works once, ever. Printed with a
   scratch-off panel and a QR code.
6. **Offline-proof**: the box keeps a signed token (`pass until`) and a trusted clock, so Game Pass works without
   internet and setting the clock back doesn't extend it.
7. **Updates**: staged rollouts (10% → 50% → 100%, watched with crash reports from Android itself, no SDK),
   never during a game, atomic installs, automatic recovery from a crash loop, no downgrades (fix = new version).
   **Make the app device owner** at provisioning (one adb command) so updates install silently; ask the box maker
   for A/B firmware with their own release keys and verified boot.
8. **The biggest real-world risk is the box itself**: cheap Android TV boxes ship with malware (BADBOX 2.0: over
   10 million devices; Vo1d: 1.6 million) and test-key firmware that anyone can sign. Clean firmware from the ODM
   with release keys, locked bootloader and adb off is part of security, not an extra (checklist in
   [box-os.md](box-os.md)).

## What we defend against

| Threat | Defence | Where |
|---|---|---|
| Fake or malicious update pushed to boxes | Manifest signed with the offline release key, pinned in the app; APK must also carry our Android signing certificate (Android enforces it too). Two independent keys, neither on a server | `cli/channel.ts`, `ChannelManifest.java`, `Installer.java` |
| Server or Cloudflare account hacked | Can't sign updates (offline key). Can mint Game Pass (online pass key): rotate it. Database copy is useless for finding codes (keyed hashes) | design |
| Old (vulnerable) version replayed to boxes | `serial` only goes up; boxes refuse lower serials; Android refuses downgrades | `CloudState.acceptManifest` |
| Stale manifest served forever (freeze) | `expires` (max 400 days, default 180); expired manifests install nothing | `ChannelManifest.appUpdateFor` |
| Damaged or swapped download | File named by SHA-256, size + hash checked, then hashed **again while streaming into the installer** (no check-then-use gap) | `Downloader.java`, `Installer.java` |
| Wrong firmware bricks a box | Entry names the exact `Build.MODEL` and the exact `Build.FINGERPRINT`s it updates from; the ODM's OTA signature is checked by the system; A/B falls back on a failed boot | `FirmwareUpdater.java` |
| Bad release crashes boxes | Staged rollout + crash/ANR reports per version; rollout can be paused; installs never during a game; 3 failed starts = safe mode that fetches the fix; the updater runs without Unity | `TunduCloud.java`, `stats` |
| A broken update loops downloads | A file that failed to install 3 times is never tried again | `CloudState.installAllowed` |
| Guessing codes | 75 random bits: with a million cards in circulation each guess has a 1 in 38 quadrillion chance; plus max 10 wrong codes per box per day, rate limits per box and IP, enrolment circuit breaker | `codes.ts`, `redeem.ts` |
| Same card used twice / two boxes racing | Claim is one conditional UPDATE in one D1 transaction; unique ledger row per card; tested with 8 boxes racing | `redeem.ts` |
| Retried request adds days twice | Box keeps the same request id until it gets an answer; same id = same answer | `redeem.ts`, `CloudState.redeem` |
| Cards stolen from shop/printer/warehouse | Batches inactive until activated (when paid); a lost batch is voided; scratch-off; codes never stored in clear | `admin.ts` |
| Database leak reveals codes | Only HMAC-SHA256(pepper, code); pepper is a Worker secret, not in the database | `codes.ts` |
| Setting the box clock back | Trusted time = last signed server time + monotonic elapsed time; floor never goes back | `PassClock.java` |
| Token copied to another box / old token replayed | Token names the device; older tokens (lower server time) refused | `PassToken.java`, `CloudState.acceptPass` |
| Online key signs an update (role confusion) | Each role has its own pinned keys; a pass key is never accepted for a manifest (test vector) | `SignedDoc.java` |
| Server bug wipes boxes' identities | Box re-enrols only after 3 refused check-ins, and keeps honouring its old token until it ends | `CloudState.checkIn` |
| Database corruption / mistakes | D1 Time Travel (restore to any minute, up to 30 days), audit ledger with before/after values, consistency check command, weekly export | `admin.ts check`, runbook |
| npm supply-chain worm (Shai-Hulud, Sept 2025) | Deployed Worker has no dependencies; dev tools pinned exactly from a lockfile, versions at least 2 weeks old, install scripts disabled | `server/.npmrc` |
| Admin API abuse | Long random token, only its hash on the server, optional IP allowlist, every action logged; no admin web page | `auth.ts` |

**Accepted risks** (cost more to fix than they're worth at this stage): a rooted box can patch the app (that's why
production firmware must not be rootable); an offline box that's switched off doesn't advance its clock, so
Game Pass "pauses" while it's off and offline (it needs the internet to get a pass anyway); a blocked box keeps its
last token until its next check-in.

## How it fits together

```
 Your laptop (offline keys)                    Cloudflare                                   Tundu box
 ┌─────────────────────────┐   upload    ┌──────────────────────────────┐   HTTPS   ┌─────────────────────────────┐
 │ server/cli/tundu.ts     │───────────▶│ R2 "tundu-updates"            │◀──────────│ UpdateJobService (Java,     │
 │  release key (encrypted)│            │  cdn.tundu.al/channels/*.json │           │  runs without Unity)        │
 │  Android keystore       │            │  .../apk/<sha256>.apk         │           │  verify → download → install│
 │  releases/ (git log)    │            │  .../ota/<sha256>.zip         │           │                             │
 │                         │  admin API  ├──────────────────────────────┤   HTTPS   │ TunduCloud (Java)           │
 │  cards, devices, stats  │───────────▶│ Worker "tundu-api"            │◀──────────│  enrol, check-in, redeem,   │
 └─────────────────────────┘            │  api.tundu.al  (no deps)      │           │  health events              │
                                        │ D1 "tundu": devices, cards,   │           │ PassClock + signed token    │
                                        │  batches, pass_ledger, events │           │ Unity: TunduCloud.cs,       │
                                        │ secrets: pass key, pepper,    │           │  GameAccess, GamePassPanel  │
                                        │  admin token hash             │           └─────────────────────────────┘
                                        └──────────────────────────────┘
```

**Signed documents** (one format for everything the box trusts):
`{"kid":"p1","payload":"<base64url JSON>","sig":"<base64url DER ECDSA P-256 SHA-256>"}`. The signature covers the
exact payload bytes (no JSON canonicalisation pitfalls). ECDSA P-256 because every Android version verifies it with
the platform's own code; Ed25519 would need Android 13 and the RK3566 boxes run Android 11. The box pins public keys
per role in `TrustedKeys.java`, generated from `releases/keys.json` by the CLI.

**Game Pass token** (signed by the Worker, online key `p1`):
`{"type":"tundu.pass","v":1,"device":"d_…","pass_until":<s>,"now":<s>,"blocked":false}`.

**Channel manifest** (signed on the laptop, offline key `r1`), one per channel (`dev`, `beta`, `stable`):
`{"type":"tundu.channel","v":1,"channel":"stable","serial":12,"issued":…,"expires":…,"app":{package, versionCode,
versionName, file:"apk/<sha256>.apk", sha256, size, signer, minSdk, rollout, required}, "games":[{id, tier, minApp?,
hidden?, from?}], "firmware":[{model, from:[fingerprints], to, file, sha256, size, rollout}]}`.

## Gift cards and Game Pass

**The card.** Code `XXXX-XXXX-XXXX-XXXX` from `23456789ABCDEFGHJKLMNPQRSTUVWXYZ` (no 0, 1, I, O), the 16th character
a Luhn mod 32 check, so the box catches a typo instantly without a network call or a "wrong code" strike. The QR code
holds `https://tundu.al/kod#XXXX-XXXX-XXXX-XXXX`: the code is in the fragment, which browsers never send to a server, so
a phone scanning it can show a help page without the code being logged. Print it under a **scratch-off panel**, card
in tamper-evident packaging; never put the code on the outside of the packaging.

**The batch lifecycle.** `cards new` creates a batch **inactive** and returns the codes once (CSV saved with owner-only
permissions in `~/.tundu/cards/`; the server never stores them). Send the CSV to the printer over a private channel,
keep one encrypted backup, delete other copies. `cards activate <batch>` when the shop has paid / the cards are handed
over (cards stolen before that are worthless; that's the cheap version of the point-of-sale activation big retailers
use against rack tampering). `cards void-batch` for a lost parcel. Gift card fraud in retail is mostly cards
tampered with on the rack and codes read before purchase; activation-on-sale and scratch-off panels are the
standard answers.

**Redeeming.** Settings → Game Pass (or choosing a locked game) opens the code screen: an on-screen grid of the 32
characters for the remote (a USB keyboard works too); the check character is verified as the 16th character is
typed. The box then sends `{code, request}`; the server answers `ok` (+days), `already_yours`, `used`,
`not_active` ("ask the shop"), `void`, `invalid`, or `limit`, always with a fresh signed token. Days stack from the
current end date. A pass can't be pushed more than 5 years ahead. Next step for the UI: read the QR with the box's
own camera instead of typing (the camera is already there).

**Which games are in Game Pass** is your decision and lives in the signed catalog
(`release game --channel stable --id fly --tier pass`). Until Game Pass launches every game is free
(`GameAccess.DefaultTier`). At launch, set the built-in defaults in the same release, so a box that was never online
can't open Game Pass games for free.

**Support tools**: `cards lookup <code>`, `cards void <code> --revoke` (refund: takes the days back),
`device show <id>` (the id is shown on the Game Pass screen), `device grant <id> --days 30` (e.g. a month included
with the box), `device transfer <old> <new> --block-old` (warranty swap), `device block`.

**Selling, legally** (ask your accountant, these are the questions): Albania requires a fiscal (e-)invoice for every
sale (Law 87/2019, fiscalisation), so cards are sold with a fiscal receipt like any product; check whether VAT is due
when the card is sold or when it's redeemed (EU-style "single-purpose voucher" rules would tax at sale). Data: the
server keeps a random device id, model, Android and app version, and redemption history; no names or contact data.
That keeps the obligations under Law 124/2024 (Albania's GDPR-aligned law) small; still publish a short privacy note.

## Updates

**Channels.** `dev` (your own box), `beta` (pilot families who agreed), `stable` (everyone). A box's channel is
`stable` unless changed (`TunduCloud.setChannel`, for a future hidden setting).

**A release, step by step** (all commands from `server/`):
1. Set the version in Unity (Project Settings → Player → Version, `MAJOR.MINOR.PATCH`); versionCode is computed
   (`1.2.3` → `10203`) and must go up every release.
2. *MoveIt → Box → Build Release APK (signed)* (needs the keystore variables, see "Setting up").
3. `node cli/tundu.ts release stage ../builds/android/Tundu.apk`: checks package, versionCode, that it's signed with
   the pinned release certificate (not debug), uploads it as `apk/<sha256>.apk`.
4. `node cli/tundu.ts release app --channel beta --version 10203 --rollout 100`: signs and publishes the beta
   manifest (asks for the release key passphrase). Commit `releases/` (git history = release log).
5. Watch `node cli/tundu.ts stats` for a day or two: versions seen, `boot_ok` vs `crash`/`crash_native`/`anr` per
   version.
6. `release app --channel stable --version 10203 --rollout 10`, then `release rollout --channel stable --percent 50`,
   then `100`, a day or two apart while the numbers stay good. The same boxes are in the first 10% for a release (by
   device id), a different 10% for the next.

**When the box installs.** Downloads happen in the background (resumable, verified). Installs happen only when no
game is running: in the first minute after the box starts (the family is just sitting down and sees "Updating
Tundu…" for a few seconds; never after returning from a game), when the menu has been idle for 10 minutes with nobody
in front of the camera, at night (02:00-05:00), or at once in safe mode. `release app --required N` makes boxes below versionCode N skip the rollout queue (for urgent fixes).

**Before installing**, the box checks: signature and channel of the manifest, serial not lower than seen, not
expired, newer versionCode of this very package, Android version ≥ minSdk, inside the rollout, file hash and size,
APK package/versionCode/signing certificate equal to the manifest, the installed app and the pinned certificate; the
bytes are hashed again while written into the install session. Android then verifies the signature itself and
installs atomically (old or new, never half).

**Silent installs.** Android installs without asking when the installer is **device owner** (one adb command at
provisioning: `provision.ps1 -DeviceOwner`, on a reset box without accounts; only a factory reset removes it), a
privileged system app with `INSTALL_PACKAGES` (ODM firmware), or, on Android 12+, an app updating itself with
`USER_ACTION_NOT_REQUIRED`, but that last path needs the app to target a recent API level, and that floor rises with
every Android version. Without any of these, Android shows its install dialog once (OK on the remote). Recommendation:
device owner on every box (it also blocks "clear data", which would delete the box's identity). Check on the sample
boxes that they support it (`pm list features` must list `android.software.device_admin`; some TV builds don't).

**A bad release** can't be rolled back (Android refuses downgrades): `release rollout --percent 0` stops new installs
at once, then publish a **new** versionCode with the fix (the old code if needed), `--required` if the bad one
crashes. If a build crashes at start, boxes enter safe mode after 3 failed starts and the background job (which
doesn't load Unity) installs the fix.

**Firmware.** Rare (security or driver fixes from the box maker). Ask the ODM for **A/B partitions** (seamless
updates that fall back by themselves when the new system doesn't boot), **verified boot** with a locked
bootloader, and OTA images signed with **their release keys**. Upload with `release firmware-stage <ota.zip> --model
M --to FINGERPRINT`, publish with `release firmware --channel beta --sha256 … --from FP1,FP2 --rollout 10`. On
device-owner boxes the app applies it at night with Android's `installSystemUpdate` (the system checks the ODM's
signature); after the reboot the fingerprint says whether it worked. Untested until we have the ODM's images.

**Version mismatches, all the places they could happen.**
- App vs game content: one APK, one version: content can't mismatch the code.
- App vs catalog: unknown game ids are ignored; `minApp` hides a game on versions where it misbehaves.
- App vs API: `/v1/` only ever gains optional fields; the box ignores unknown fields; a breaking change becomes
  `/v2/` while `/v1/` keeps working until no box uses it (`stats` shows versions in use).
- App vs manifest format: `"v":1`; boxes refuse versions they don't know (keep running, don't install); a new
  format is published next to the old one until old boxes have updated.
- App vs saved data: write save files atomically (temp file + rename) with a schema version and forward-only
  migrations (the cloud client already writes with `commit()`, the CLI with temp + rename).

## Pushing new games

A new game (free or Game Pass) = an app update that contains it + a catalog entry. Ship the code "dark" if you like
(`release game --id newgame --hidden`), then switch it on for everyone at once with a catalog change, or schedule it
(`--from 2026-12-01`, e.g. a Christmas surprise). The kill switch (`--hidden`) removes a game that crashes from every
box within hours, without an app update. Content-only additions (new songs, levels) could later use downloadable
Unity asset bundles, built in the same pipeline and pinned to the app version, but not before there is a need: it is
the usual source of version-mismatch crashes (Unity's own docs: bundle content and player scripts must match).

## Setting up (one time, about an hour)

1. **Cloudflare account** with a **hardware security key or authenticator 2FA**; add the domain (`tundu.al`) to
   Cloudflare. Use scoped API tokens, never the global key.
2. In `server/`: `npm ci`, then `npx wrangler login`, `npx wrangler d1 create tundu` (put the printed id in
   `wrangler.jsonc`), `npm run migrate:remote`.
3. **R2**: create bucket `tundu-updates`, connect custom domain `cdn.tundu.al` (public read). Nothing secret goes there.
4. **Keys** (from `server/`):
   - `node cli/tundu.ts keys new-release --kid r1` → encrypted key file `~/.tundu/release-r1.key` + passphrase in
     your password manager. **Copy the key file to an offline USB stick** (and a second one somewhere else).
   - `node cli/tundu.ts keys new-pass --kid p1 | npx wrangler secret put PASS_SIGNING_KEY`
   - `node cli/tundu.ts keys pepper | npx wrangler secret put CODE_PEPPER`. **Back up the pepper** (password
     manager): without it no existing card can ever be redeemed.
   - `node cli/tundu.ts keys admin-token` → keep the token in the password manager, put its hash with
     `npx wrangler secret put ADMIN_TOKEN_SHA256`. Use it as `TUNDU_ADMIN_TOKEN` when running admin commands.
   - **Android release keystore** (once, ever; losing it means boxes can never be updated again):
     `keytool -genkeypair -v -keystore tundu-release.keystore -alias tundu -keyalg RSA -keysize 4096 -validity 36500`.
     Back it up offline like the release key. Set `TUNDU_KEYSTORE`, `TUNDU_KEYSTORE_PASS`, `TUNDU_KEY_ALIAS`,
     `TUNDU_KEY_PASS` as user environment variables before starting Unity. After the first signed build:
     `apksigner verify --print-certs Tundu.apk` and `node cli/tundu.ts keys set-apk-signer <SHA-256 digest>`.
   - Commit `releases/keys.json` and the regenerated `TrustedKeys.java` (public keys only).
5. `npx wrangler deploy`. Check: `curl https://api.tundu.al/v1/health`.
6. Build the APK (now it trusts your keys), provision boxes with `-DeviceOwner`, publish the first manifest.
7. Optional extra lock: a Cloudflare WAF rule that only allows `/admin/*` from your IP, or set `ADMIN_IPS`.

**Back up**: release key file + passphrase; keystore + passwords; pepper; admin token. Database: D1 Time Travel
covers mistakes for up to 30 days; also run `npx wrangler d1 export tundu --remote --output backup.sql` weekly and keep it
on the laptop (it contains no codes and no personal data).

**Try everything without hardware**: `npm run dev:vars && npm run migrate:local && npx wrangler dev`, then in another
terminal (with `TUNDU_API=http://127.0.0.1:8787` and `TUNDU_ADMIN_TOKEN` = the test token in `test/secrets.ts`):
`node cli/tundu.ts devbox enroll --api http://127.0.0.1:8787`, `cards new …`, `cards activate 1`,
`devbox redeem <code from the CSV>`. In the Unity editor, Settings → Game Pass simulates a box: any well-formed code
adds 30 days, e.g. `TUND-UTES-TKAR-TE2R`.

## Runbooks

| Situation | Do |
|---|---|
| Selling cards to a shop | `cards new --days 30 --count 200 --label "Shop X 2026-11"` → printer → `cards activate N` when paid |
| Customer: "the code doesn't work" | `cards lookup <code>` (typo? not activated? used by which box?) |
| Lost/stolen parcel of cards | `cards void-batch N --reason "..."` (unused cards stop working; used ones keep working) |
| Refund | `cards void <code> --reason "refund" --revoke` |
| Box replaced under warranty | `device transfer <old> <new> --reason "warranty" --block-old` |
| New release | the 6 steps under "A release" |
| Release crashes | `release rollout --channel stable --percent 0`, fix, new versionCode, publish (`--required` if it crash-loops) |
| A game misbehaves | `release game --channel stable --id X --hidden` (back with `--shown`) |
| Manifest expiring (`release show` warns under 30 days) | `release resign --channel stable` (twice a year) |
| Pass key leaked | new pass key `p2` (`keys new-pass --kid p2`), app update pinning p1+p2, then swap the Worker secret, later drop p1 |
| Release key file lost or leaked | Updates are still protected by the APK keystore. Make `r2`, ship an app update signed with r1 that pins r1+r2, then use r2. If lost before that: boxes keep working but can't be updated until reflashed |
| Admin token leaked | `keys admin-token`, update `ADMIN_TOKEN_SHA256`, check `admin_log` |
| Data mistake / corruption | `npx wrangler d1 time-travel restore tundu --timestamp <before>`, then `node cli/tundu.ts check` |
| Wrong codes spike in `stats` | Normal: none. Many: someone is guessing (harmless at 75 bits); block the boxes involved |

## Costs

Cloudflare Workers Paid is about **$5/month** and includes 10 million requests, and for D1 25 billion rows read,
50 million rows written and 5 GB per month. R2: 10 GB storage free, then $0.015/GB-month, and **no download (egress)
fees**: 1,000 boxes downloading a 54 MB update twice a month is about 108 GB of traffic, which costs nothing. A
thousand boxes checking in four times a day use well under 1% of the included requests. Development can stay on the
free plan; switch to Paid before selling (the free plan's 10 ms of CPU per request is too little for creating card
batches of more than a few dozen cards). Plus the domain.

## Built, tested, and not yet

| Part | State |
|---|---|
| `server/` Worker + D1 schema | Built. **49 tests in the real Workers runtime** (workerd + local D1): codes, signatures, enrolment, races (8 boxes, 1 card; 1 box, 6 cards), retries, lockout, rate limits, caps, void/refund/transfer, admin auth, consistency check, cron. Run end to end with `wrangler dev` + CLI + pretend box |
| `server/cli/` | Built. 14 tests (key files, manifests, rollout buckets, APK checks, publish flow, card CSV) |
| Box Java (`MoveItCloud.androidlib`) | Built. **Compiles against Android API 36 with all warnings as errors; 33 JUnit tests** on the pure classes (shared vectors with the server: codes, signatures, role separation, rollout; trusted clock; manifest rules; state; resumable verified downloads). **Not run on a device yet**: PackageInstaller install, job scheduling, receivers, device owner policies, crash reports, firmware |
| Unity C# | Built. Compile check OK (player + editor); 7 new EditMode tests (code vectors, game access) pass outside Unity. **Not seen in Unity yet**: the Game Pass screen and locks (IMGUI), the changed `HomeScreenTests`, `BoxBuild.cs` (editor code can't be compile-checked here) |
| Not built | QR scanning with the box camera; a web page for `tundu.al/kod`; payments/online sales; alerts (check `stats` by hand for now) |

Your next steps, in order: run the Unity tests and look at Settings → Game Pass in the editor; set up Cloudflare and the
keys ("Setting up"); decide which games go into Game Pass, the prices and card durations; ask the accountant the
two questions above; on the sample boxes, check device-owner support and do a real silent update via the `beta`
channel.

## Sources

- Device owner installs without user action: [PackageInstaller.Session](https://developer.android.com/reference/android/content/pm/PackageInstaller.Session);
  Android 12+ self-update conditions and the rising target-API floor: [SessionParams.setRequireUserAction](https://developer.android.com/reference/android/content/pm/PackageInstaller.SessionParams);
  `installSystemUpdate`, `setSystemUpdatePolicy`, `setUserControlDisabledPackages`: [DevicePolicyManager](https://developer.android.com/reference/android/app/admin/DevicePolicyManager);
  [Enterprise OTA updates (AOSP)](https://source.android.com/docs/devices/admin/ota-updates);
  setting device owner over adb without a factory reset when no accounts exist: [Bayton](https://bayton.org/android/android-enterprise-faq/can-i-set-device-owner-without-factory-reset/),
  [F-Droid device-owner installs](https://gitlab.com/fdroid/fdroidclient/-/merge_requests/258); a signage vendor's claim that some TV builds lack provisioning: [Garlic](https://garlic-player.com/garlic-launcher/docs/installation) (check on samples).
- Crash reports without an SDK: [ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo).
  Ed25519 only from API 33: [Signature](https://developer.android.google.cn/reference/kotlin/java/security/Signature).
  Check-then-install race in old installers: [Unit 42](https://unit42.paloaltonetworks.com/android-installer-hijacking-vulnerability-could-expose-android-users-to-malware/).
- Malware on cheap TV boxes: [FBI on BADBOX 2.0](https://www.bleepingcomputer.com/news/security/fbi-badbox-20-android-malware-infects-millions-of-consumer-devices/),
  [Google's lawsuit, 10M+ devices](https://tech.yahoo.com/cybersecurity/articles/google-malware-spread-over-10-175846557.html),
  [T95: test keys and open adb](https://www.darkreading.com/threat-intelligence/malware-standard-android-tv-box-amazon), [EFF](https://www.eff.org/node/108378),
  [Vo1d, 1.6M boxes](https://www.securityweek.com/vo1d-botnet-evolves-as-it-ensnares-1-6-million-android-tv-boxes/).
  Boxes losing the date without a clock battery: [DroiX forum](https://forum.droix.net/threads/wrong-clock-display-afer-power-up-not-droidbox.4266/).
- Update security: [The Update Framework specification](https://theupdateframework.io/specification) and [threat list](https://theupdateframework.io/security/).
- Cloudflare: [D1 pricing](https://developers.cloudflare.com/d1/platform/pricing/), [D1 Time Travel](https://developers.cloudflare.com/d1/reference/time-travel/),
  [Workers rate limiting GA](https://developers.cloudflare.com/changelog/2025-09-19-ratelimit-workers-ga/), [rate limit binding](https://developers.cloudflare.com/workers/runtime-apis/bindings/rate-limit/),
  [Workers and R2 pricing](https://workers.cloudflare.com/pricing), [D1 batch = transaction](https://event-driven.io/en/cloudflare_d1_transactions_and_tradeoffs/),
  [Access plans](https://community.cloudflare.com/t/cloudflare-access-plan-differences/199471).
- Gift card fraud: [Packetlabs](https://www.packetlabs.net/gift-and-loyalty-card-security), [F5 on card cracking](https://www.f5.com/go/solution/gift-card-cracking),
  [ICMA on activation tampering](https://icma.com/2026/05/28/securing-gift-card-activation-for-the-era-of-organized-fraud), [Schneier](https://www.schneier.com/blog/archives/2024/12/gift-card-fraud.html).
- npm worm: [Krebs on Shai-Hulud](https://krebsonsecurity.com/2025/09/self-replicating-worm-hits-180-software-packages/).
  Unity bundles must match player scripts: [Addressables content updates](https://docs.unity3d.com/Packages/com.unity.addressables@2.7/manual/builds-update-build.html).
- Albania: [fiscalisation, Law 87/2019](https://eurofast.eu/albania-new-fiscalization-tax-reform-requires-e-invoicing/);
  [data protection, Law 124/2024](https://iapp.org/news/a/albania-s-personal-data-protection-law-a-legal-framework-harmonized-with-the-gdpr), [KPMG summary](https://kpmg.com/al/en/insights/2025/02/new-law-on--personal-data-protection-.html).
