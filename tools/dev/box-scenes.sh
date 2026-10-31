#!/bin/bash
# One line per scene on the box: loads each through PerfLab, waits for its second [Perf] line (the first includes the
# load), then adds Tundu's CPU use over the next 10 s (% of one core), the GPU clock and load, the NPU load and the SoC
# temperature.
#
#   bash tools/dev/box-scenes.sh [-d ip:port] Menu Runner Dress Bowling Paint Sword Fly Tennis Sports
#
# Don't run a Unity build or Unity in batch mode meanwhile: Unity restarts adb, which drops the box.
export MSYS_NO_PATHCONV=1
DEVICE=192.168.100.103:5555
while getopts "d:" o; do case $o in d) DEVICE=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1))
ADB="/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe -s $DEVICE"
F=/sdcard/Android/data/al.tundu.games/files/perflab.txt
sample() {
    $ADB shell 'P=$(pidof al.tundu.games); echo "$(awk "{print \$14+\$15}" /proc/$P/stat) $(date +%s%N | cut -c1-13) $(cat /sys/class/devfreq/fde60000.gpu/cur_freq) $(cat /sys/class/devfreq/fde60000.gpu/load | cut -d@ -f1) $(su 0 cat /sys/kernel/debug/rknpu/load | tr -dc 0-9) $(cat /sys/class/thermal/thermal_zone0/temp)"' | tr -d '\r'
}
for scene in "$@"; do
    tag=s$RANDOM
    $ADB shell "printf 'tag=$tag\nscene=$scene\n' > $F"
    line=$(timeout 30 $ADB logcat -T 1 -s Unity:I 2>&1 | grep --line-buffered "\[Perf\] $scene \[$tag\]" | head -2 | tail -1)
    a=$(sample); timeout 10 $ADB logcat -T 1 -s NOTHING:V >/dev/null 2>&1; b=$(sample)
    set -- $a; j0=$1; t0=$2
    set -- $b; j1=$1; t1=$2; mhz=$(( $3 / 1000000 )); gload=$4; npu=$5; soc=$6
    echo "$scene: $(echo "$line" | sed 's/.*\]: //' | cut -c1-175) | app $(( (j1 - j0) * 1000 / (t1 - t0) ))% | GPU ${mhz} MHz ${gload}% | NPU ${npu}% | SoC ${soc:0:2}.${soc:2:1}"
done
$ADB shell "rm -f $F"
