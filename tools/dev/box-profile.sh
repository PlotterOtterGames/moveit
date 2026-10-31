#!/bin/bash
# Where one of Tundu's threads spends its time on the box, with symbols: C# methods (IL2CPP), Unity's engine and the
# pose engine. Records with simpleperf (root on the box: this firmware's su), DWARF call graphs (frame-pointer chains
# break inside libunity), and reports inclusive time per function ("Children").
#
#   bash tools/dev/box-profile.sh [-d ip:port] [-t thread] [-s seconds] <name>
#
# Threads: UnityMain (game code and IMGUI), Thread-5 (Unity's render thread on the X98K Pro), pool-5-thread-1 (the pose
# AI), CameraX-camerax (CameraX's frame conversion). Output in builds/profiles/<name>.data and <name>.txt.
# The symbols come from the last APK build (unity/MoveItGames/Library/Bee): profile the APK that build made. Unity's
# own libunity.sym.so in the editor folder is a different build (its build id doesn't match: wrong names).
set -e
export MSYS_NO_PATHCONV=1
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
ADB=/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe
SP=/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/NDK/simpleperf/bin/windows/x86_64/simpleperf.exe
DEVICE=192.168.100.103:5555; THREAD=UnityMain; SECS=6
while getopts "d:t:s:" o; do case $o in d) DEVICE=$OPTARG;; t) THREAD=$OPTARG;; s) SECS=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1)); NAME=${1:?name}
OUT=$ROOT/builds/profiles; mkdir -p "$OUT"
BEE=$ROOT/unity/MoveItGames/Library/Bee/artifacts/Android
A="$ADB -s $DEVICE"

P=$($A shell pidof al.tundu.games | tr -d '\r')
[ -n "$P" ] || { echo "Tundu isn't running on $DEVICE"; exit 1; }
T=$($A shell "for t in /proc/$P/task/*; do if [ \"\$(cat \$t/comm)\" = \"$THREAD\" ]; then basename \$t; break; fi; done" | tr -d '\r')
[ -n "$T" ] || { echo "no thread '$THREAD' in Tundu"; exit 1; }
echo "Recording $THREAD (tid $T) for $SECS s"
$A shell "su 0 simpleperf record -t $T --call-graph dwarf,16384 -f 400 --duration $SECS -o /data/local/tmp/$NAME.data" 2>&1 | tail -1
$A shell "su 0 chmod 644 /data/local/tmp/$NAME.data"
$A pull /data/local/tmp/$NAME.data "$(cygpath -w "$OUT/$NAME.data")" | tail -1
$A shell "su 0 rm /data/local/tmp/$NAME.data"

# simpleperf looks for the libraries under --symfs at their path on the device
LIB=$($A shell pm path al.tundu.games | tr -d '\r' | sed 's/^package://; s#/base.apk$#/lib/arm64#')
SYMFS=$OUT/symfs; mkdir -p "$SYMFS$LIB"
cp -u "$BEE/il2cppOutput/build/arm64-v8a/libil2cpp.dbg.so" "$SYMFS$LIB/libil2cpp.so"
cp -u "$BEE/libunity/arm64-v8a/libunity.sym.so" "$SYMFS$LIB/libunity.so"
cd "$OUT"
"$SP" report -i "$NAME.data" --symfs symfs --children --sort symbol --percent-limit 0.8 > "$NAME.txt" 2>&1
grep "build id mismatch" "$NAME.txt" | head -2 || true
# IL2CPP's method names end in a 40-digit hash: dropped for reading
grep -E "^ *[0-9.]+% +[0-9.]+% " "$NAME.txt" | sed -E 's/_m[0-9A-F]{40}//; s/_gshared//' | cut -c1-160 \
    | grep -v -E "art::|art_|[Mm]terp|android\.os\.|__start_thread|__pthread_start|com\.unity3d" | head -${LINES_SHOWN:-60}
echo "Full report: $OUT/$NAME.txt"
