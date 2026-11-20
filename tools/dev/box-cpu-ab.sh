#!/bin/bash
# A/B of PerfLab settings on the box with the CPU's side of it: per configuration the game's [Perf] lines, the camera
# AI's stats lines, the heat, and the CPU time of each of the app's threads over the measured stretch (which thread a
# change moves work to: the camera thread, Unity's main thread, its render thread). Each argument is "name|settings",
# settings being PerfLab lines joined with \n (scene=, video=, model=, scale= ...). By default uncapped (fps=60) with the
# NPU loaded like one tracked person (npubench) and the GPU clock pinned at 300 MHz; -x '' leaves the box as it runs
# (the game's own frame rate, the real camera AI), -g 0 leaves the GPU's governor alone.
#
#   bash tools/dev/box-cpu-ab.sh [-d ip:port] [-t seconds] [-g MHz] [-x 'extra PerfLab lines'] <out_dir> "name|settings" ...
#   e.g. bash tools/dev/box-cpu-ab.sh out "arcade|scene=Arcade" "arcade1024|scene=Arcade\nvideo=1024"
#        bash tools/dev/box-cpu-ab.sh -g 0 -x '' out "dress_live|scene=Dress"
#
# The summary (tools/dev/box_cpu_summary.py) comes at the end. The box is left on the home screen with no PerfLab file.
# Don't run a Unity build or Unity in batch mode meanwhile: Unity restarts adb, which drops the box.
export MSYS_NO_PATHCONV=1
DEVICE=192.168.1.46:5555
SECONDS_EACH=40
GPU_MHZ=300
EXTRA='fps=60\npeople=1\nnpubench=1\n'
while getopts "d:t:g:x:" o; do case $o in d) DEVICE=$OPTARG;; t) SECONDS_EACH=$OPTARG;; g) GPU_MHZ=$OPTARG;; x) EXTRA=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1))
OUT=${1:?out_dir}
shift
ADB="/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe -s $DEVICE"
F=/sdcard/Android/data/al.tundu.games/files/perflab.txt
GPU=/sys/class/devfreq/fde60000.gpu
mkdir -p "$OUT"

threads() {  # every thread of the app as name|jiffies (10 ms of CPU each), then the time in ms
    $ADB shell 'P=$(pidof al.tundu.games); for t in /proc/$P/task/*; do echo "$(cat $t/comm 2>/dev/null)|$(sed "s/.*) //" $t/stat 2>/dev/null | awk "{print \$12+\$13}")"; done; echo "time|$(date +%s%N | cut -c1-13)"' | tr -d '\r'
}

measure() {
    local name=$1 settings=$2
    echo "[$(date +%H:%M:%S)] $name" >&2
    $ADB shell "printf 'tag=$name\n$EXTRA$settings\n' > $F"
    sleep 18
    $ADB logcat -c
    local log="$OUT/$name.txt"
    : > "$log"
    threads | sed 's/^/A|/' >> "$log"
    local end=$(( $(date +%s) + SECONDS_EACH ))
    (timeout "$SECONDS_EACH" $ADB logcat -v time -s MoveItMotion:I Unity:I 2>&1 | grep --line-buffered -E "stats:|\[Perf\]" >> "$log") &
    while [ "$(date +%s)" -lt "$end" ]; do
        $ADB shell 'echo "heat: SoC $(cat /sys/class/thermal/thermal_zone0/temp) NPU load $(su 0 cat /sys/kernel/debug/rknpu/load | tr -dc 0-9)% GPU $(( $(cat /sys/class/devfreq/fde60000.gpu/cur_freq) / 1000000 )) MHz $(cat /sys/class/devfreq/fde60000.gpu/load) CPU $(( $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq) / 1000 )) MHz"' | tr -d '\r' >> "$log"
        sleep 10
    done
    wait
    threads | sed 's/^/B|/' >> "$log"
}

if [ "$GPU_MHZ" != "0" ]; then
    $ADB shell "su 0 sh -c 'echo userspace > $GPU/governor; echo ${GPU_MHZ}000000 > $GPU/userspace/set_freq'"
fi
trap '$ADB shell "su 0 sh -c \"echo simple_ondemand > $GPU/governor\""; $ADB shell "printf \"scene=Menu\n\" > $F"; sleep 4; $ADB shell "rm -f $F"' EXIT
$ADB shell "printf 'scene=Menu\n' > $F"
sleep 6
names=()
for spec in "$@"; do
    measure "${spec%%|*}" "${spec#*|}"
    names+=("${spec%%|*}")
done
python "$(dirname "$0")/box_cpu_summary.py" "$OUT" "${names[@]}"
