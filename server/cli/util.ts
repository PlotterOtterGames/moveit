// Shared helpers for the CLI: paths, settings from the environment, prompts, running tools, JSON files.

import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

export const SERVER_DIR = resolve(dirname(fileURLToPath(import.meta.url)), "..");
export const REPO_DIR = resolve(SERVER_DIR, "..");
export const RELEASES_DIR = join(REPO_DIR, "releases");
/** releases/ (signed manifests, staged APK list, pinned keys); TUNDU_RELEASES_DIR overrides it for tests. */
export const releasesDir = () => process.env["TUNDU_RELEASES_DIR"] ?? RELEASES_DIR;

/** Settings, from environment variables (never stored in the repo). */
export const config = {
  api: process.env["TUNDU_API"] ?? "https://api.tundu.al",
  cdn: process.env["TUNDU_CDN"] ?? "https://cdn.tundu.al",
  bucket: process.env["TUNDU_BUCKET"] ?? "tundu-updates",
  releaseKey: process.env["TUNDU_RELEASE_KEY"] ?? join(homedir(), ".tundu", "release-r1.key"),
  buildTools: process.env["ANDROID_BUILD_TOOLS"] ?? "D:\\Unity\\6000.3.9f1\\Editor\\Data\\PlaybackEngines\\AndroidPlayer\\SDK\\build-tools\\36.0.0",
};

export class CliError extends Error {}

export function fail(message: string): never {
  throw new CliError(message);
}

export function readJsonFile<T>(path: string, fallback?: T): T {
  if (!existsSync(path)) {
    if (fallback !== undefined) return fallback;
    fail(`missing file: ${path}`);
  }
  return JSON.parse(readFileSync(path, "utf8")) as T;
}

/** Writes via a temporary file + rename, so a crash never leaves a half-written file. */
export function writeFileAtomic(path: string, content: string | Uint8Array, mode?: number): void {
  mkdirSync(dirname(path), { recursive: true });
  const tmp = `${path}.tmp-${process.pid}`;
  writeFileSync(tmp, content, mode === undefined ? undefined : { mode });
  renameSync(tmp, path);
}

export function writeJsonFile(path: string, value: unknown): void {
  writeFileAtomic(path, JSON.stringify(value, null, 2) + "\n");
}

/** Asks for a secret without echoing it (falls back to an environment variable for scripts). */
export async function askSecret(prompt: string, envName?: string): Promise<string> {
  if (envName && process.env[envName]) return process.env[envName]!;
  if (!process.stdin.isTTY) fail(`${prompt}: no terminal to ask in${envName ? `; set ${envName}` : ""}`);
  process.stdout.write(prompt + ": ");
  const stdin = process.stdin;
  stdin.setRawMode(true);
  stdin.resume();
  stdin.setEncoding("utf8");
  return new Promise((resolvePromise, reject) => {
    let value = "";
    const onData = (chunk: string) => {
      for (const ch of chunk) {
        if (ch === "\r" || ch === "\n") {
          stdin.setRawMode(false);
          stdin.pause();
          stdin.off("data", onData);
          process.stdout.write("\n");
          resolvePromise(value);
          return;
        }
        if (ch === "\u0003") {
          stdin.setRawMode(false);
          reject(new CliError("cancelled"));
          return;
        }
        if (ch === "\u007f" || ch === "\b") value = value.slice(0, -1);
        else value += ch;
      }
    };
    stdin.on("data", onData);
  });
}

export async function confirm(question: string): Promise<boolean> {
  if (process.env["TUNDU_YES"] === "1") return true;
  if (!process.stdin.isTTY) fail(`${question} (no terminal to confirm in; set TUNDU_YES=1 to skip)`);
  process.stdout.write(`${question} [y/N] `);
  return new Promise((resolvePromise) => {
    process.stdin.resume();
    process.stdin.setEncoding("utf8");
    process.stdin.once("data", (answer: string) => {
      process.stdin.pause();
      resolvePromise(/^y(es)?$/i.test(answer.trim()));
    });
  });
}

export interface RunResult {
  status: number;
  stdout: string;
  stderr: string;
}

export function run(command: string, args: string[], options: { cwd?: string } = {}): RunResult {
  const r = spawnSync(command, args, { cwd: options.cwd, encoding: "utf8", shell: process.platform === "win32" });
  if (r.error) fail(`could not run ${command}: ${r.error.message}`);
  return { status: r.status ?? 1, stdout: r.stdout ?? "", stderr: r.stderr ?? "" };
}

/** Uploads a file to the R2 bucket with wrangler (uses the wrangler login on this computer). */
export function uploadToR2(key: string, file: string, contentType: string, cacheControl: string): void {
  const npx = process.platform === "win32" ? "npx.cmd" : "npx";
  const r = run(
    npx,
    ["wrangler", "r2", "object", "put", `${config.bucket}/${key}`, "--file", file, "--content-type", contentType, "--cache-control", cacheControl, "--remote"],
    { cwd: SERVER_DIR },
  );
  if (r.status !== 0) fail(`upload of ${key} failed:\n${r.stderr || r.stdout}`);
}

export function formatTime(seconds: number): string {
  return seconds ? new Date(seconds * 1000).toISOString().replace("T", " ").slice(0, 16) + " UTC" : "-";
}
