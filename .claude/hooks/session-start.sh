#!/bin/bash
# Claude Code cloud sessions: get the Linux container ready to run what can run without Unity.
#  - Python venv for the motion service and the tools/ scripts (sim, fake player, music, pages, branding)
#  - .NET 8 SDK + the C# logic tests (tools/logic-tests: game rules and their EditMode tests outside Unity)
#  - the compile check (tools/compile-check: all game C# against Unity's reference assemblies)
#  - the Tundu server's dev tools (server/: Worker tests in workerd, CLI tests)
# The laptop setup (Windows, Unity, Blender, MuMu) is in CLAUDE.md; this hook does nothing there.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"

SUDO=""
if [ "$(id -u)" -ne 0 ]; then SUDO="sudo"; fi

apt_packages=()
command -v dotnet >/dev/null 2>&1 || apt_packages+=(dotnet-sdk-8.0)
command -v ffmpeg >/dev/null 2>&1 || apt_packages+=(ffmpeg)   # tools/music/render.py encodes OGG with it
if [ ${#apt_packages[@]} -gt 0 ]; then
  echo "[session-start] apt install ${apt_packages[*]}"
  $SUDO apt-get update -qq
  DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq "${apt_packages[@]}" >/dev/null
fi

# Python: the same venv the laptop uses (bin/ instead of Scripts/ on Linux). Pillow is for tools/branding.
if [ ! -x motion-service-py/.venv/bin/python ]; then
  echo "[session-start] creating motion-service-py/.venv"
  python3 -m venv motion-service-py/.venv
fi
motion-service-py/.venv/bin/pip install -q --disable-pip-version-check -r motion-service-py/requirements.txt pillow

# C#: restore and build once so `dotnet test tools/logic-tests` is quick.
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1' >> "$CLAUDE_ENV_FILE"
fi
dotnet build tools/logic-tests -v quiet -nologo
# Server (Cloudflare Worker + CLI) dev tools from the lockfile, without install scripts (server/.npmrc).
if command -v npm >/dev/null 2>&1; then
  (cd server && npm ci --no-audit --no-fund >/dev/null 2>&1) || echo "[session-start] npm ci in server/ failed"
fi
# The Unity code against Unity's reference assemblies (downloads them once); a failure here is reported, not fatal.
python3 tools/compile-check/check.py || echo "[session-start] compile check found errors (see above)"

echo "[session-start] ready: motion-service-py/.venv/bin/python, dotnet test tools/logic-tests, python3 tools/compile-check/check.py, (cd server && npm run test:all), python3 tools/cloud-java-tests/run.py"
