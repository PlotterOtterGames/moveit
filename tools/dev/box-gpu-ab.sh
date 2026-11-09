#!/bin/bash
# A/B of PerfLab settings on the box, any scene: each argument is "name|settings", settings being PerfLab lines joined
# with \n (scene=, activity=, scale=, res=, hide= ...). Each runs uncapped (fps=60, so a cheaper frame shows as more
# frames) with the NPU load of one tracked person (npubench), 60 s after 15 s settling (an activity gets 25 s to start).
# -g pins the GPU clock for the whole run (root; the hot box sits at 200-250 MHz: compare there), simple_ondemand comes
# back at the end. One log per configuration in <out_dir>, then a summary (tools/dev/box_two_summary.py).
#
#   bash tools/dev/box-gpu-ab.sh [-d ip:port] [-t seconds] [-w warmup_seconds] [-g MHz] <out_dir> "name|settings" ...
#   e.g. bash tools/dev/box-gpu-ab.sh -g 300 out "runner|scene=Runner" "runner_res|scene=Runner\nscale=1\nres=0.8"
#
# Don't run a Unity build or Unity in batch mode meanwhile: Unity restarts adb, which drops the box.
export MSYS_NO_PATHCONV=1
DEVICE=192.168.1.46:5555
SECONDS_EACH=60
WARMUP=60
GPU_MHZ=
while getopts "d:t:w:g:" o; do case $o in d) DEVICE=$OPTARG;; t) SECONDS_EACH=$OPTARG;; w) WARMUP=$OPTARG;; g) GPU_MHZ=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1))
OUT=${1:?out_dir}
shift
ADB="/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe -s $DEVICE"
F=/sdcard/Android/data/al.tundu.games/files/perflab.txt
GPU=/sys/class/devfreq/fde60000.gpu
mkdir -p "$OUT"

measure() {
    local name=$1 settings=$2 seconds=$3
    echo "[$(date +%H:%M:%S)] $name" >&2
    $ADB shell "printf 'tag=$name\nfps=60\npeople=1\nnpubench=1\n$settings\n' > $F"
    case $settings in *activity=*) sleep 25 ;; *) sleep 15 ;; esac
    $ADB logcat -c
    local log="$OUT/$name.txt"
    [ -e "$log" ] && log="$OUT/${name}_$(date +%H%M%S).txt"
    : > "$log"
    local end=$(( $(date +%s) + seconds ))
    (timeout "$seconds" $ADB logcat -v time -s MoveItMotion:I Unity:I 2>&1 | grep --line-buffered -E "stats:|\[Perf\]|PerfLab" >> "$log") &
    while [ "$(date +%s)" -lt "$end" ]; do
        $ADB shell 'echo "heat: SoC $(cat /sys/class/thermal/thermal_zone0/temp) GPU $(cat /sys/class/thermal/thermal_zone1/temp) NPU load $(su 0 cat /sys/kernel/debug/rknpu/load | tr -dc 0-9)% GPU $(( $(cat /sys/class/devfreq/fde60000.gpu/cur_freq) / 1000000 )) MHz $(cat /sys/class/devfreq/fde60000.gpu/load) CPU $(( $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq) / 1000 )) MHz"' | tr -d '\r' >> "$log"
        sleep 10
    done
    wait
}

if [ -n "$GPU_MHZ" ]; then
    $ADB shell "su 0 sh -c 'echo userspace > $GPU/governor; echo ${GPU_MHZ}000000 > $GPU/userspace/set_freq'"
    trap '$ADB shell "su 0 sh -c \"echo simple_ondemand > $GPU/governor\""; $ADB shell "rm -f $F"' EXIT
fi
$ADB shell am start -n al.tundu.games/com.unity3d.player.UnityPlayerActivity > /dev/null
first=${1#*|}
[ "$WARMUP" -gt 0 ] && measure warmup "$first" "$WARMUP"
names=()
for spec in "$@"; do
    name=${spec%%|*}
    measure "$name" "${spec#*|}" "$SECONDS_EACH"
    names+=("$name")
done
$ADB shell "rm -f $F"
python "$(dirname "$0")/box_two_summary.py" "$OUT" $(ls "$OUT" | grep '\.txt$' | sed 's/\.txt$//' | sort)
