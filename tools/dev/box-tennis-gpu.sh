#!/bin/bash
# Where Tenis!'s GPU time goes on the box, one player: PerfLab switches one thing at a time while Tenis! runs
# uncapped (fps=60, so a cheaper frame shows as more frames, not as an idle GPU), with the pose AI's NPU load of one
# tracked person (npubench: fixed crops, nobody needed in front of the camera). A warm-up first (the box's heat decides
# its clocks: compare configurations only back to back), "base" again between the others to see the drift. One log
# per configuration in <out_dir> (the game's [Perf] lines, the AI's stats, the heat every ~10 s), then a summary
# (tools/dev/box_two_summary.py).
#
#   bash tools/dev/box-tennis-gpu.sh [-d ip:port] [-t seconds] [-w warmup_seconds] [-g MHz] <out_dir> [config ...]
#
# -g pins the GPU at that clock (root, the userspace governor; the box's: 200 300 400 600 700) for the whole run and puts
# simple_ondemand back at the end: the heat then can't change the clock between configurations (the power allocator
# took it from 700 to 333-617 MHz above ~80 degrees C and made later configurations look cheaper). 400 is where it sits
# when the box is hot.
# Don't run a Unity build or Unity in batch mode meanwhile: Unity restarts adb, which drops the box.
export MSYS_NO_PATHCONV=1
DEVICE=192.168.1.46:5555
SECONDS_EACH=60
WARMUP=240
GPU_MHZ=
while getopts "d:t:w:g:" o; do case $o in d) DEVICE=$OPTARG;; t) SECONDS_EACH=$OPTARG;; w) WARMUP=$OPTARG;; g) GPU_MHZ=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1))
OUT=${1:?out_dir}
shift
CONFIGS=${*:-base s70 nocrowd noskin base lowall notrans nosky nostadium s100 base}
ADB="/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe -s $DEVICE"
F=/sdcard/Android/data/al.tundu.games/files/perflab.txt
COMMON='scene=Tennis\nfps=60\npeople=1\nnpubench=1\n'
mkdir -p "$OUT"

settings() {
    case $1 in
        base|warmup) echo '' ;;
        s60) echo 'scale=0.6\n' ;;
        s70) echo 'scale=0.7\n' ;;
        s100) echo 'scale=1\n' ;;
        # the hardware scaler instead of the render scale (a build with PerfLab's res switch)
        res80) echo 'scale=1\nres=0.8\n' ;;
        res75) echo 'scale=1\nres=0.75\n' ;;
        res70) echo 'scale=1\nres=0.7\n' ;;
        boxres0) echo 'boxres=0\n' ;;                # BoxGraphics' render scale whatever the scene says
        boxres1) echo 'boxres=1\n' ;;                # BoxGraphics' scale as the screen's resolution
        nocrowd) echo 'hide=crowd\n' ;;
        noskin) echo 'hide=skinned\n' ;;
        lowall) echo 'chibi=low\n' ;;
        fullall) echo 'chibi=full\n' ;;
        notrans) echo 'hide=transparent\n' ;;
        nosky) echo 'sky=0\n' ;;
        nostadium) echo 'hide=court\n' ;;
        nohud) echo 'disable=MotionDebugOverlay\n' ;;
        nogui) echo 'disable=MotionDebugOverlay,TennisGame\n' ;;
        nonear) echo 'hide=player\n' ;;              # the near back player (the biggest toy on the screen)
        nofar) echo 'hide=opponent\n' ;;             # the far team (low-detail chibis)
        nonet) echo 'hide=net\n' ;;
        # the sun's shadows drawn as shapes on the grass (2026-11-07: the toys', the ball's, the stadium's fixed ones)
        noshadows) echo 'hide=shadows\n' ;;
        # all of 2026-11-07's effects: those shadows, the particles, the ball's glow
        nofx) echo 'hide=tennis fx,court shadows\n' ;;
        # the stadium without its world-space grass detail (Tundu/Toon's _WORLD_DETAIL; stays off until the scene reloads)
        nodetail) echo 'detail=0\n' ;;
        nostands) echo 'hide=crowd,near end\n' ;;
        # the split screen (versus) without two people (PerfLab split=1; since 2026-11-07 singles: two toys drawn twice)
        split) echo 'split=1\n' ;;
        *) echo "unknown configuration $1" >&2; exit 2 ;;
    esac
}

measure() {
    local c=$1 seconds=$2
    local s
    s=$(settings "$c") || exit 2
    echo "[$(date +%H:%M:%S)] $c" >&2
    $ADB shell "printf 'tag=$c\n$COMMON$s' > $F"
    sleep 15   # applied within 0.5 s; the first [Perf] lines settle
    $ADB logcat -c
    local log="$OUT/$c.txt"
    [ -e "$log" ] && log="$OUT/${c}_$(date +%H%M%S).txt"
    : > "$log"
    local end=$(( $(date +%s) + seconds ))
    (timeout "$seconds" $ADB logcat -v time -s MoveItMotion:I Unity:I 2>&1 | grep --line-buffered -E "stats:|\[Perf\]|PerfLab" >> "$log") &
    while [ "$(date +%s)" -lt "$end" ]; do
        $ADB shell 'echo "heat: SoC $(cat /sys/class/thermal/thermal_zone0/temp) GPU $(cat /sys/class/thermal/thermal_zone1/temp) NPU load $(su 0 cat /sys/kernel/debug/rknpu/load | tr -dc 0-9)% GPU $(( $(cat /sys/class/devfreq/fde60000.gpu/cur_freq) / 1000000 )) MHz $(cat /sys/class/devfreq/fde60000.gpu/load) CPU $(( $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq) / 1000 )) MHz"' | tr -d '\r' >> "$log"
        sleep 10
    done
    wait
}

GPU=/sys/class/devfreq/fde60000.gpu
if [ -n "$GPU_MHZ" ]; then
    $ADB shell "su 0 sh -c 'echo userspace > $GPU/governor; echo ${GPU_MHZ}000000 > $GPU/userspace/set_freq 2>/dev/null || echo ${GPU_MHZ}000000 > $GPU/min_freq; echo ${GPU_MHZ}000000 > $GPU/max_freq'"
    trap '$ADB shell "su 0 sh -c \"echo 200000000 > $GPU/min_freq; echo 700000000 > $GPU/max_freq; echo simple_ondemand > $GPU/governor\""' EXIT
    echo "GPU pinned: $($ADB shell cat $GPU/cur_freq)" >&2
fi
$ADB shell am start -n al.tundu.games/com.unity3d.player.UnityPlayerActivity > /dev/null
[ "$WARMUP" -gt 0 ] && measure warmup "$WARMUP"
for c in $CONFIGS; do measure "$c" "$SECONDS_EACH"; done
$ADB shell "rm -f $F"
python "$(dirname "$0")/box_two_summary.py" "$OUT" $(ls "$OUT" | sed 's/\.txt$//' | sort)
