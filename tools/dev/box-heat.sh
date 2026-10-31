#!/bin/bash
# The box's heat and load over time while Tundu runs: every ~20 s one line with the SoC and GPU temperatures, the GPU
# clock and load, the NPU load, the CPU clock, the whole box's and Tundu's CPU use (% of one core since the last line),
# the scene's latest [Perf] line and the camera AI's stats line.
#
#   bash tools/dev/box-heat.sh [-d ip:port] <label> <minutes> [scene]
#
# With a scene it's loaded through PerfLab first (and the switch file removed at the end). Don't run a Unity build or
# Unity in batch mode meanwhile: Unity restarts adb, which drops the box.
export MSYS_NO_PATHCONV=1
DEVICE=192.168.100.103:5555
while getopts "d:" o; do case $o in d) DEVICE=$OPTARG;; *) exit 2;; esac; done
shift $((OPTIND - 1))
ADB="/d/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe -s $DEVICE"
label=${1:?label}; minutes=${2:-6}; scene=${3:-}
F=/sdcard/Android/data/al.tundu.games/files/perflab.txt
[ -n "$scene" ] && $ADB shell "printf 'tag=heat\nscene=$scene\n' > $F"
start=$(date +%s)
end=$(( start + minutes * 60 ))
prev=""
while [ $(date +%s) -lt $end ]; do
    t=$(( $(date +%s) - start ))
    raw=$($ADB shell 'P=$(pidof al.tundu.games); echo "$(cat /sys/class/thermal/thermal_zone0/temp) $(cat /sys/class/thermal/thermal_zone1/temp) $(cat /sys/class/devfreq/fde60000.gpu/cur_freq) $(cat /sys/class/devfreq/fde60000.gpu/load | cut -d@ -f1) $(su 0 cat /sys/kernel/debug/rknpu/load | tr -dc 0-9) $(cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq) $(head -1 /proc/stat | awk "{print \$2+\$3+\$4+\$7+\$8, \$5+\$6}") $(awk "{print \$14+\$15}" /proc/$P/stat 2>/dev/null) $(date +%s%N | cut -c1-13)"' | tr -d '\r')
    set -- $raw
    [ $# -ge 10 ] || { echo "$label t=${t}s | no answer from the box"; sleep 5; continue; }
    soc=$1; gpuT=$2; gmhz=$(( $3 / 1000000 )); gload=$4; npu=$5; cpu=$(( $6 / 1000 )); busy=$7; idle=$8; app=$9; now=${10}
    usage=""
    if [ -n "$prev" ]; then
        set -- $prev
        dt_ms=$(( now - $4 )); dbusy=$(( busy - $1 )); dapp=$(( app - $3 ))
        usage="box $(( dbusy * 1000 / dt_ms ))% app $(( dapp * 1000 / dt_ms ))%"   # jiffies of 10 ms
    fi
    prev="$busy $idle $app $now"
    perf=$($ADB logcat -d -s Unity:I | grep "\[Perf\]" | tail -1 | sed 's/.*\[Perf\] //' | cut -c1-150)
    ai=$($ADB logcat -d -s MoveItMotion:V | grep "stats:" | tail -1 | sed 's/.*stats: //' | cut -c1-110)
    echo "$label t=${t}s | SoC ${soc:0:2}.${soc:2:1} GPU ${gpuT:0:2} C | GPU ${gmhz} MHz ${gload}% | NPU ${npu}% | CPU ${cpu} MHz | $usage | $perf | $ai"
    timeout 20 $ADB logcat -T 1 -s NOTHING:V >/dev/null 2>&1   # wait ~20 s
done
[ -n "$scene" ] && $ADB shell "rm -f $F"
