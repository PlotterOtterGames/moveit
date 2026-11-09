#!/bin/bash
# Two players' cost on the box, in Tenis!, without anyone in front of the camera: PerfLab's npubench runs the pose AI's
# landmark model on fixed crops as if one or two people were followed (the NPU's and CPU's load of that many people;
# docs/hardware.md "Two players"). Each configuration runs for N seconds after a settling pause; one log per
# configuration in <out_dir> (the AI's stats lines, the game's [Perf] lines, the heat every ~10 s), then a summary
# (tools/dev/box_two_summary.py).
#
#   bash tools/dev/box-two.sh [-d ip:port] [-t seconds] <out_dir> [config ...]
#
# Configurations (default: all, in this order): one (one person), two30 (two taking turns: 30 landmark runs a second
# shared), two45, two60 (both every frame), split30 (the versus split screen, two taking turns), split60, one_end (one
# person again at the end: the heat built up meanwhile).
# Don't run a Unity build or Unity in batch mode meanwhile: Unity restarts adb, which drops the box.
export MSYS_NO_PATHCONV=1
DEVICE=192.168.1.46:5555
SECONDS_EACH=120
while getopts "d:t:" o; do case $o in d) DEVICE=$OPTARG;; t) SECONDS_EACH=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1))
OUT=${1:?out_dir}
shift
CONFIGS=${*:-one two30 two45 two60 split30 split60 one_end}
ADB="/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe -s $DEVICE"
F=/sdcard/Android/data/al.tundu.games/files/perflab.txt
mkdir -p "$OUT"

settings() {
    case $1 in
        one|one_end) echo 'people=1\nnpubench=1\n' ;;
        two30) echo 'people=2\nnpubench=1\nnpurate=30\n' ;;
        two45) echo 'people=2\nnpubench=1\nnpurate=45\n' ;;
        two60) echo 'people=2\nnpubench=1\nnpurate=0\n' ;;
        split30) echo 'people=2\nnpubench=1\nnpurate=30\nsplit=1\n' ;;
        split60) echo 'people=2\nnpubench=1\nnpurate=0\nsplit=1\n' ;;
        # where the split screen's GPU time goes: render scale 0.65, without the crowd, without the toys
        split30_s65) echo 'people=2\nnpubench=1\nnpurate=30\nsplit=1\nscale=0.65\n' ;;
        split30_nocrowd) echo 'people=2\nnpubench=1\nnpurate=30\nsplit=1\nhide=crowd\n' ;;
        split30_noskin) echo 'people=2\nnpubench=1\nnpurate=30\nsplit=1\nhide=skinned\n' ;;
        # the chibis at full detail (before 2026-11-06: the low-detail ones are the far team's and the split screen's)
        split30_full) echo 'people=2\nnpubench=1\nnpurate=30\nsplit=1\nchibi=full\n' ;;
        one_full) echo 'people=1\nnpubench=1\nchibi=full\n' ;;
        warmup) echo 'people=2\nnpubench=1\nnpurate=30\nsplit=1\nchibi=full\n' ;;
        *) echo "unknown configuration $1" >&2; exit 2 ;;
    esac
}

for c in $CONFIGS; do
    s=$(settings "$c") || exit 2
    echo "[$(date +%H:%M:%S)] $c" >&2
    $ADB shell "printf 'tag=$c\nscene=Tennis\n$s' > $F"
    sleep 20   # the scene loads, the pose AI switches, the first [Perf] lines settle
    $ADB logcat -c
    log="$OUT/$c.txt"
    : > "$log"
    end=$(( $(date +%s) + SECONDS_EACH ))
    (timeout "$SECONDS_EACH" $ADB logcat -v time -s MoveItMotion:I Unity:I 2>&1 | grep --line-buffered -E "stats:|\[Perf\]|People" >> "$log") &
    while [ "$(date +%s)" -lt "$end" ]; do
        $ADB shell 'echo "heat: SoC $(cat /sys/class/thermal/thermal_zone0/temp) GPU $(cat /sys/class/thermal/thermal_zone1/temp) NPU load $(su 0 cat /sys/kernel/debug/rknpu/load | tr -dc 0-9)% GPU $(( $(cat /sys/class/devfreq/fde60000.gpu/cur_freq) / 1000000 )) MHz $(cat /sys/class/devfreq/fde60000.gpu/load) CPU $(( $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq) / 1000 )) MHz"' | tr -d '\r' >> "$log"
        sleep 10
    done
    wait
done
$ADB shell "rm -f $F"
python "$(dirname "$0")/box_two_summary.py" "$OUT" $CONFIGS
