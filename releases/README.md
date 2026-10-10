# Releases

What the boxes trust and install, kept in git so the history is the release log. Written by the CLI
(`server/cli/tundu.ts`), never by hand (a hand-edited manifest no longer verifies and the CLI refuses it).

| File | What |
|---|---|
| `keys.json` | Public keys the app pins, per role (`release`: signs manifests, offline; `pass`: signs Game Pass tokens, the Worker) and the release APK signing certificate. The CLI regenerates `TrustedKeys.java` from it. Private keys are never here |
| `channels/<channel>.json` | The signed manifest currently published at `https://cdn.tundu.al/channels/<channel>.json` (app version + rollout, game catalog, firmware) |
| `apks.json` | APKs staged on the CDN (`apk/<sha256>.apk`): versionCode, size, hash, signer |
| `firmware.json` | Firmware images staged on the CDN (`ota/<sha256>.zip`) |

See [docs/cloud.md](../docs/cloud.md) "Updates" for the release steps.
