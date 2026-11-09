#!/bin/bash
# The camera AI and a game side by side on the box, for N seconds: the AI's stats lines (MoveItMotion: poses/s, the
# model, crop / NPU times, frames skipped, people lost), the game's [Perf] lines (fps, frames late) and every ~10 s the
# SoC temperature, the NPU load and the GPU clock. Optionally loads a scene and a pose model first (PerfLab).
#
#   bash tools/dev/box-ai.sh [-d ip:port] [-s scene] [-m auto|full|lite] <seconds> > out.txt
#
# Someone has to be in front of the camera for the tracking numbers (else the AI idles: the detector every 150 ms).
# Don't run a Unity build or Unity in batch mode meanwhile: Unity restarts adb, which drops the box.
export MSYS_NO_PATHCONV=1
DEVICE=192.168.1.57:5555
SCENE=
MODEL=
while getopts "d:s:m:" o; do case $o in d) DEVICE=$OPTARG;; s) SCENE=$OPTARG;; m) MODEL=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1))
SECONDS_TOTAL=${1:-60}
ADB="/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe -s $DEVICE"
F=/sdcard/Android/data/al.tundu.games/files/perflab.txt
if [ -n "$SCENE$MODEL" ]; then
    tag=a$RANDOM
    $ADB shell "printf 'tag=$tag\n${SCENE:+scene=$SCENE\n}${MODEL:+model=$MODEL\n}' > $F"
    sleep 8
fi
$ADB logcat -c
end=$(( $(date +%s) + SECONDS_TOTAL ))
(timeout "$SECONDS_TOTAL" $ADB logcat -v time -s MoveItMotion:I Unity:I 2>&1 | grep --line-buffered -E "stats:|\[Perf\]|Pose model|switching") &
while [ "$(date +%s)" -lt "$end" ]; do
    $ADB shell 'echo "heat: SoC $(cat /sys/class/thermal/thermal_zone0/temp) GPU $(cat /sys/class/thermal/thermal_zone1/temp) NPU load $(su 0 cat /sys/kernel/debug/rknpu/load | tr -dc 0-9)% GPU $(( $(cat /sys/class/devfreq/fde60000.gpu/cur_freq) / 1000000 )) MHz $(cat /sys/class/devfreq/fde60000.gpu/load)"' | tr -d '\r'
    sleep 10
done
wait
